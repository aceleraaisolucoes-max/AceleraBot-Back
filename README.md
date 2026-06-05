# AceleraBot API

Backend Node.js + TypeScript do sistema AceleraBot — chatbot de IA para WhatsApp.

## Stack

- **Runtime**: Node.js 20 + TypeScript
- **Framework**: Express 5
- **IA**: Google Gemini 1.5 Flash (grátis)
- **Banco de dados**: Supabase (PostgreSQL)
- **WhatsApp**: Evolution API (open-source)
- **Deploy**: Railway (plano gratuito)

## Estrutura

```
src/
├── app.ts                    ← Entry point
├── lib/
│   ├── supabase.ts           ← Cliente Supabase
│   └── gemini.ts             ← Cliente Gemini AI
├── routes/
│   ├── webhook.ts            ← POST /webhook/:clientId
│   ├── clients.ts            ← CRUD + QR Code
│   ├── conversations.ts      ← Histórico
│   └── leads.ts              ← Leads + stats
└── services/
    ├── aiService.ts          ← Lógica de IA + qualificação
    ├── whatsappService.ts    ← Evolution API wrapper
    └── notifyService.ts      ← Alertas para o dono
database/
└── schema.sql                ← Executar no Supabase
```

## Setup Local

```bash
# 1. Instalar dependências
npm install

# 2. Configurar variáveis
cp .env.example .env
# Edite o .env com suas credenciais

# 3. Banco de dados
# Execute o arquivo database/schema.sql no Supabase SQL Editor

# 4. Rodar em desenvolvimento
npm run dev
```

## Deploy no Railway

1. Crie um novo projeto em [railway.app](https://railway.app)
2. Conecte o repositório GitHub
3. Adicione as variáveis do `.env.example` no painel do Railway
4. O deploy é automático a cada push na branch `main`

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| `GET` | `/health` | Health check |
| `POST` | `/webhook/:clientId` | Recebe mensagem do WhatsApp |
| `POST` | `/clients` | Cria cliente + instância WhatsApp |
| `GET` | `/clients/:id/qrcode` | Gera QR Code para escanear |
| `GET` | `/clients/:id/status` | Status da conexão WhatsApp |
| `GET` | `/conversations` | Lista conversas do cliente |
| `GET` | `/conversations/:id/messages` | Histórico de mensagens |
| `PATCH` | `/conversations/:id/takeover` | Humano assume o atendimento |
| `GET` | `/leads` | Lista leads qualificados |
| `GET` | `/leads/stats` | Estatísticas para o dashboard |

## Custo

**R$ 0/mês** usando os planos gratuitos:
- Railway: 500h/mês grátis
- Supabase: 500MB + 50k usuários grátis
- Gemini 1.5 Flash: 1.5M tokens/dia grátis
