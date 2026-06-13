# Runbook de Deploy — AceleraBot

Guia passo a passo para subir a infraestrutura completa (Cards 05, 06, 07).

## Estratégia de hospedagem

O código fica no **Azure DevOps**, mas Render e Vercel só fazem auto-deploy a
partir de GitHub. Solução: um **pipeline de espelhamento** empurra cada push da
`main` (Azure) para um repositório-espelho no **GitHub**; Render e Vercel ficam
conectados ao GitHub e fazem o deploy sozinhos.

```
push na main (Azure DevOps)
   └─ azure-pipelines.yml (mirror) ─→ GitHub (espelho)
                                         ├─→ Render  : backend + Evolution API
                                         └─→ Vercel  : frontend
```

Você continua commitando **apenas no Azure DevOps**.

> Todos os valores secretos estão no doc **"Credenciais e Chaves de Ambiente"**.
> Nunca cole segredos neste arquivo (ele é versionado) — use o painel de cada serviço.

## Ordem de execução (há dependências de URL entre serviços)

```
0. GitHub: criar repos-espelho + PAT          → automação de mirror
1. Supabase (schema)                           → ✅ já aplicado
2. Render: Evolution API (+ Postgres + Redis)  → EVOLUTION_API_URL
3. Render: Backend (Blueprint)                 → APP_URL
4. Vercel: Frontend                            → DASHBOARD_URL
5. Fechar variáveis no backend + redeploy
6. Google Cloud: OAuth redirect URIs
```

---

## 0. GitHub — repos-espelho + automação

1. No [GitHub](https://github.com) (conta do doc), crie **dois repositórios vazios**
   (sem README), por exemplo:
   - `acelera-bot-api` (backend)
   - `acelera-bot-front` (frontend)
2. Gere um **Personal Access Token** com escrita nesses repos:
   Settings → Developer settings → **Fine-grained tokens** → Repository access nos
   2 repos → Permissions: **Contents = Read and write**. (Ou um token *classic* com
   escopo `repo`.) Copie o token.
3. Em **cada** projeto no Azure DevOps → **Pipelines → New pipeline → Azure Repos Git**
   → selecione o repo → "Existing Azure Pipelines YAML file" → `/azure-pipelines.yml`.
4. Antes de rodar, em **Variables** adicione:
   - `GITHUB_REPO` = `usuario_ou_org/acelera-bot-api` (no front, `.../acelera-bot-front`)
   - `GITHUB_PAT` = o token (marque **Keep this value secret**)
5. **Run**. Confirme que o código apareceu nos repos do GitHub. A partir daqui, todo
   push na `main` do Azure replica para o GitHub automaticamente.

---

## 1. Supabase — ✅ concluído

O schema (`database/schema.sql`) já foi aplicado; as tabelas existem. Nada a fazer.
Tenha em mãos `SUPABASE_URL` e `SUPABASE_SERVICE_KEY` (do doc) para o passo 3.

---

## 2. Evolution API no Render (Card 05)

1. No [Render](https://render.com): **New → PostgreSQL**, nome `evolution-db`,
   plano **Free**. Copie a **Internal Connection String**.
2. **New → Web Service → Deploy an existing image from a registry**:
   - Image URL: `atendai/evolution-api:latest`
   - Plano: **Free**
3. Em **Environment**, adicione (confira nomes na versão da imagem em
   [doc.evolution-api.com](https://doc.evolution-api.com)):

   | Variável | Valor |
   |---|---|
   | `AUTHENTICATION_API_KEY` | **defina** uma chave mestra forte (guarde-a = `EVOLUTION_API_KEY`) |
   | `DATABASE_ENABLED` | `true` |
   | `DATABASE_PROVIDER` | `postgresql` |
   | `DATABASE_CONNECTION_URI` | connection string do `evolution-db` |
   | `DATABASE_SAVE_DATA_INSTANCE` | `true` |
   | `DATABASE_SAVE_DATA_NEW_MESSAGE` | `true` |
   | `CACHE_REDIS_ENABLED` | `true` |
   | `CACHE_REDIS_URI` | `REDIS_URL` do Upstash (formato `rediss://default:...@host:6379`) |
   | `CACHE_REDIS_PREFIX_KEY` | `evolution` |
   | `CACHE_LOCAL_ENABLED` | `false` |

4. Deploy. Copie a **URL pública** (ex.: `https://acelera-evolution-api.onrender.com`).
5. Volte em Environment, adicione `SERVER_URL` = essa URL, e redeploy.
6. **Anote a URL → será `EVOLUTION_API_URL` no backend.**

> ⚠️ **Free tier hiberna após ~15 min** de inatividade, derrubando a sessão do
> WhatsApp (precisa reescanear o QR). OK para validação; produção pede plano pago
> ou keep-alive.

---

## 3. Backend no Render (Card 06) — via Blueprint

1. **New → Blueprint** → conecte a conta GitHub → selecione o repo
   **`acelera-bot-api`** (o espelho). O Render lê o [`render.yaml`](render.yaml) e
   propõe criar o serviço `acelera-bot-api` (e o `evolution-db`, caso ainda não exista).
2. Preencha as variáveis marcadas (todas com valores do doc de credenciais):

   | Variável | Valor |
   |---|---|
   | `SUPABASE_URL` / `SUPABASE_SERVICE_KEY` | do doc |
   | `GEMINI_API_KEY` | do doc |
   | `EVOLUTION_API_URL` | URL do passo 2 |
   | `EVOLUTION_API_KEY` | a chave mestra que você definiu no passo 2 |
   | `REDIS_URL` | Upstash (`rediss://...`) |
   | `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | do doc |
   | `GOOGLE_REDIRECT_URI` | *(preenche no passo 5)* |
   | `DASHBOARD_URL` | *(preenche no passo 5)* |
   | `APP_URL` | *(preenche após conhecer a URL deste serviço)* |
   | `PLATFORM_OWNER_WHATSAPP` | seu número `55DDDNUMERO` |

3. Deploy. Copie a **URL pública** → essa é a `APP_URL`; volte e preencha `APP_URL`.
4. Valide: `GET {APP_URL}/health` → `{"status":"ok", ...}`.

> A cada push na `main` (Azure → GitHub), o Render redeploya automaticamente.

---

## 4. Frontend na Vercel (Card 07)

1. Em [vercel.com](https://vercel.com) → **Add New → Project** → **Import Git Repository**
   → conecte o GitHub → selecione **`acelera-bot-front`**. A Vercel detecta o Next.js.
2. **Environment Variables** → `NEXT_PUBLIC_BACKEND_URL` = `APP_URL` (URL do backend
   no Render). **Deploy.**
3. Copie a **URL de produção** (ex.: `https://acelera-bot-front.vercel.app`).
   **Anote → será `DASHBOARD_URL` no backend.**

> A cada push na `main`, a Vercel redeploya automaticamente.

---

## 5. Fechar o ciclo de variáveis no backend

No serviço `acelera-bot-api` (Render → Environment), preencha/atualize e **redeploy**:
- `DASHBOARD_URL` = URL do frontend (passo 4). Habilita CORS e o redirect pós-OAuth.
- `GOOGLE_REDIRECT_URI` = `{APP_URL}/google/callback`.

---

## 6. Google Cloud Console — OAuth

Em [console.cloud.google.com](https://console.cloud.google.com) → **APIs &
Services → Credentials → OAuth 2.0 Client ID** (o do doc):
- **Authorized redirect URIs:** adicione `{APP_URL}/google/callback`.
- **Authorized JavaScript origins:** adicione a URL do frontend (Vercel).
- Confirme que a **Google Calendar API** está habilitada no projeto.

---

## Verificação ponta a ponta

1. **Health backend:** `GET {APP_URL}/health` → `{"status":"ok"}`.
2. **Evolution viva:** `POST {APP_URL}/clients` (criar cliente) → `GET {APP_URL}/clients/{id}/qrcode`
   → escanear no WhatsApp → `GET {APP_URL}/clients/{id}/status` deve dar `open`.
3. **Frontend:** abrir a URL da Vercel, logar (`admin@teste.com` / `123456` — auth
   ainda mockada) e confirmar que o dashboard carrega dados **sem erro de CORS**.
4. **Fluxo WhatsApp:** enviar mensagem ao número conectado → webhook
   `POST {APP_URL}/webhook/{clientId}` responde via IA e grava conversa/lead no Supabase.
5. **OAuth Google:** `GET {APP_URL}/google/auth?clientId=...` → consentimento →
   callback grava tokens em `google_calendar_configs`.

> Para testar local sem credenciais reais: rode com `MOCK_MODE=true` (ver `.env.example`).

---

## Pendências fora do escopo de infraestrutura

- **Auth real no frontend** (hoje mockada com `admin@teste.com`/`123456`).
- **Integração Stripe** (pagamentos) — ainda não implementada no código.
- **Fila BullMQ/Redis** no backend — dependência instalada mas não usada.
- **`schema.sql` do repo está atrás do banco** (o banco já tem uma tabela `services`
  adicionada por outro dev) — sincronizar quando for mexer em serviços/durações.
