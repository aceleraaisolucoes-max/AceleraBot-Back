# Runbook de Deploy — AceleraBot

Guia passo a passo para subir a infraestrutura completa (Cards 05, 06, 07).

> **Hospedagem do código:** os repositórios estão no **Azure DevOps**. Render e
> Vercel **não** conectam auto-deploy ao Azure DevOps, então usamos:
> - **Backend:** imagem Docker publicada num registry → serviço Render "Existing Image".
> - **Evolution API:** imagem pública oficial → serviço Render "Existing Image".
> - **Frontend:** deploy via **Vercel CLI**.
>
> A automação está nos arquivos `azure-pipelines.yml` (um em cada repo).

## Visão geral da ordem (há dependências de URL entre serviços)

```
1. Supabase (schema)          →  banco pronto
2. Registry + imagem backend  →  imagem disponível
3. Evolution API (Render)     →  EVOLUTION_API_URL
4. Backend (Render)           →  APP_URL
5. Frontend (Vercel)          →  DASHBOARD_URL
6. Fechar variáveis no backend (DASHBOARD_URL, GOOGLE_REDIRECT_URI) + redeploy
7. Google Cloud OAuth (redirect URIs)
```

Use o doc de **Credenciais e Chaves de Ambiente** para todos os valores secretos.

---

## 1. Supabase — schema do banco

1. Acesse o projeto no [Supabase](https://supabase.com) (Project ID do doc de credenciais).
2. **SQL Editor → New query** → cole o conteúdo de [`database/schema.sql`](database/schema.sql) → **Run**.
3. **Table Editor**: confirme as **7 tabelas**: `clients`, `knowledge_base`,
   `conversations`, `messages`, `leads`, `google_calendar_configs`, `appointments`.
4. Guarde `SUPABASE_URL` e a **service key** (`SUPABASE_SERVICE_KEY`).

---

## 2. Registry + imagem do backend

O backend roda como imagem Docker. Recomendado: **GHCR** (GitHub Container
Registry), usando a conta GitHub do doc de credenciais.

**Opção A — Build manual (primeiro deploy, sem pipeline):**
```bash
# na raiz do backend
docker build -t ghcr.io/SEU_USUARIO/acelera-bot-api:latest .
echo "<SEU_PAT>" | docker login ghcr.io -u SEU_USUARIO --password-stdin
docker push ghcr.io/SEU_USUARIO/acelera-bot-api:latest
```
- `<SEU_PAT>`: GitHub Personal Access Token com escopo `write:packages`.
- Em GHCR, deixe o pacote **público** (Package settings → Change visibility) para o
  Render puxar sem credenciais, ou configure credenciais no Render (passo 4).

**Opção B — Automático (Azure Pipelines):** configure `azure-pipelines.yml`
(variáveis `REGISTRY`, `IMAGE`, `REGISTRY_USERNAME`, `REGISTRY_PASSWORD`,
`RENDER_DEPLOY_HOOK`) — ele builda, publica e dispara o redeploy a cada push na `main`.
Faça isto **depois** de criar o serviço no Render (passo 4), pois precisa do Deploy Hook.

---

## 3. Evolution API no Render (Card 05)

1. No [Render](https://render.com): primeiro crie o **Postgres** da Evolution:
   **New → Postgres**, nome `evolution-db`, plano **Free**. Copie a **Internal
   Connection String**.
2. **New → Web Service → Deploy an existing image from a registry**:
   - Image URL: `atendai/evolution-api:latest`
   - Plano: **Free**
3. Em **Environment**, adicione (confira nomes na versão da imagem em
   [doc.evolution-api.com](https://doc.evolution-api.com)):

   | Variável | Valor |
   |---|---|
   | `AUTHENTICATION_API_KEY` | mesma chave mestra (= `EVOLUTION_API_KEY`) |
   | `DATABASE_ENABLED` | `true` |
   | `DATABASE_PROVIDER` | `postgresql` |
   | `DATABASE_CONNECTION_URI` | connection string do `evolution-db` |
   | `DATABASE_SAVE_DATA_INSTANCE` | `true` |
   | `DATABASE_SAVE_DATA_NEW_MESSAGE` | `true` |
   | `CACHE_REDIS_ENABLED` | `true` |
   | `CACHE_REDIS_URI` | `REDIS_URL` do Upstash (`rediss://...`) |
   | `CACHE_REDIS_PREFIX_KEY` | `evolution` |
   | `CACHE_LOCAL_ENABLED` | `false` |

4. Faça o deploy. Quando subir, copie a **URL pública** (ex.:
   `https://acelera-evolution-api.onrender.com`).
5. Volte em Environment e adicione `SERVER_URL` = essa URL. Redeploy.
6. **Anote essa URL → será `EVOLUTION_API_URL` no backend.**

> ⚠️ **Caveat free tier:** o serviço **hiberna após ~15 min de inatividade**, o que
> derruba a sessão do WhatsApp (precisa reescanear o QR). Para produção real,
> considere um plano pago ou um cron de "keep-alive". Aceitável para validação/MVP.

---

## 4. Backend no Render (Card 06)

1. **New → Web Service → Deploy an existing image from a registry**:
   - Image URL: `ghcr.io/SEU_USUARIO/acelera-bot-api:latest` (do passo 2)
   - Plano: **Free**
   - Health Check Path: `/health`
   - Se a imagem for privada, configure as credenciais do registry no Render.
2. Em **Environment**, adicione todas as variáveis (valores no doc de credenciais):

   | Variável | Valor |
   |---|---|
   | `NODE_ENV` | `production` |
   | `PORT` | `3000` |
   | `SUPABASE_URL` / `SUPABASE_SERVICE_KEY` | do passo 1 |
   | `GEMINI_API_KEY` | do doc |
   | `EVOLUTION_API_URL` | URL do passo 3 |
   | `EVOLUTION_API_KEY` | mesma chave mestra do passo 3 |
   | `REDIS_URL` | Upstash (`rediss://...`) |
   | `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | do doc |
   | `GOOGLE_REDIRECT_URI` | *(preenche no passo 6)* |
   | `DASHBOARD_URL` | *(preenche no passo 6)* |
   | `APP_URL` | *(preenche após conhecer a URL deste serviço)* |
   | `PLATFORM_OWNER_WHATSAPP` | seu número `55DDDNUMERO` |

3. Deploy. Copie a **URL pública** → essa é a `APP_URL`. Volte e preencha `APP_URL`.
4. **Copie o Deploy Hook** (Settings → Deploy Hook) e use na variável
   `RENDER_DEPLOY_HOOK` do `azure-pipelines.yml` (passo 2, Opção B).
5. Valide: `GET {APP_URL}/health` deve retornar `{"status":"ok", ...}`.

---

## 5. Frontend na Vercel (Card 07)

A Vercel não conecta ao Azure DevOps → deploy via CLI.

**Primeiro deploy (local):**
```bash
# na raiz do frontend
npm install -g vercel
vercel login
vercel link          # cria .vercel/project.json (ORG_ID e PROJECT_ID)
vercel --prod
```
1. Em **Vercel → projeto → Settings → Environment Variables**, adicione
   `NEXT_PUBLIC_BACKEND_URL` = `APP_URL` (URL do backend no Render). Redeploy.
2. Copie a **URL de produção** (ex.: `https://acelera-front.vercel.app`).
   **Anote → será `DASHBOARD_URL` no backend.**

**Deploys seguintes (automático):** configure `azure-pipelines.yml` do front com
`VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` (os dois últimos saem do
`.vercel/project.json` gerado pelo `vercel link`).

---

## 6. Fechar o ciclo de variáveis no backend

No serviço backend do Render (Environment), preencha/atualize e **redeploy**:
- `DASHBOARD_URL` = URL do frontend (passo 5). Habilita CORS e o redirect pós-OAuth.
- `GOOGLE_REDIRECT_URI` = `{APP_URL}/google/callback`.

---

## 7. Google Cloud Console — OAuth

Em [console.cloud.google.com](https://console.cloud.google.com) → **APIs &
Services → Credentials → OAuth 2.0 Client ID**:
- **Authorized redirect URIs:** adicione `{APP_URL}/google/callback`.
- **Authorized JavaScript origins:** adicione a URL do frontend (Vercel).
- Confirme que a **Google Calendar API** está habilitada no projeto.

---

## Verificação ponta a ponta

1. **Health backend:** `GET {APP_URL}/health` → `{"status":"ok"}`.
2. **Evolution viva:** criar instância via `POST {APP_URL}/clients` (payload de
   cliente) e obter o QR code em `GET {APP_URL}/clients/{clientId}/qrcode`.
   Escanear no WhatsApp e checar `GET {APP_URL}/clients/{clientId}/status` → `open`.
3. **Frontend:** abrir a URL da Vercel, logar (`admin@teste.com` / `123456` —
   auth ainda mockada) e confirmar que o dashboard carrega os dados do backend
   **sem erro de CORS** (valida `DASHBOARD_URL`).
4. **Fluxo WhatsApp:** enviar mensagem ao número conectado → o webhook
   `POST {APP_URL}/webhook/{clientId}` deve responder via IA e registrar a
   conversa/lead no Supabase.
5. **OAuth Google:** abrir `GET {APP_URL}/google/auth?clientId=...` → consentimento
   → callback grava tokens em `google_calendar_configs`.

> Para testar localmente sem credenciais reais, rode com `MOCK_MODE=true` (ver
> `.env.example`): Gemini, WhatsApp e Google Calendar respondem com dados simulados.

---

## Pendências fora do escopo de infraestrutura

- **Auth real no frontend** (hoje mockada com `admin@teste.com`/`123456`).
- **Integração Stripe** (pagamentos) — ainda não implementada no código.
- **Fila BullMQ/Redis** no backend — dependência instalada mas não usada.
