import 'dotenv/config';
import express from 'express';
import cors from 'cors';
import helmet from 'helmet';

import { webhookRouter } from './routes/webhook';
import { clientsRouter } from './routes/clients';
import { conversationsRouter } from './routes/conversations';
import { leadsRouter } from './routes/leads';

const app = express();
const PORT = process.env.PORT ?? 3000;

// ─── Middlewares ──────────────────────────────────────────────────────────────

app.use(helmet());
app.use(cors({
  origin: process.env.DASHBOARD_URL ?? '*',
  methods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'],
  allowedHeaders: ['Content-Type', 'Authorization'],
}));
app.use(express.json({ limit: '10mb' })); // Áudios/imagens em base64

// ─── Rotas ────────────────────────────────────────────────────────────────────

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

app.use('/webhook', webhookRouter);
app.use('/clients', clientsRouter);
app.use('/conversations', conversationsRouter);
app.use('/leads', leadsRouter);

// ─── Start ────────────────────────────────────────────────────────────────────

app.listen(PORT, () => {
  console.log(`🤖 AceleraBot API running on port ${PORT}`);
  console.log(`📡 Webhook: POST /webhook/:clientId`);
  console.log(`🔗 Health:  GET  /health`);
});

export default app;
