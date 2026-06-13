import { Router, Request, Response } from 'express';
import { getAuthUrl, exchangeCodeForTokens, isGoogleCalendarConnected, disconnectGoogleCalendar } from '../services/calendarService';

export const googleRouter = Router();

// ─── GET /google/auth?clientId=xxx ───────────────────────────────────────────
// Redireciona o usuário para a tela de login/consentimento do Google
googleRouter.get('/auth', async (req: Request, res: Response) => {
  const { clientId } = req.query;
  if (!clientId || typeof clientId !== 'string') {
    return res.status(400).json({ error: 'clientId is required' });
  }

  const authUrl = getAuthUrl(clientId);
  return res.redirect(authUrl);
});

// ─── GET /google/callback ─────────────────────────────────────────────────────
// Callback da Google que recebe o código OAuth e o clientId no parâmetro 'state'
googleRouter.get('/callback', async (req: Request, res: Response) => {
  const { code, state } = req.query;
  const dashboardUrl = process.env.DASHBOARD_URL ?? 'http://localhost:3000';

  if (!code || typeof code !== 'string' || !state || typeof state !== 'string') {
    return res.redirect(`${dashboardUrl}/dashboard/settings?google=error&reason=invalid_params`);
  }

  const clientId = state; // O clientId foi passado como o 'state' no link do Google

  try {
    await exchangeCodeForTokens(clientId, code);
    return res.redirect(`${dashboardUrl}/dashboard/settings?google=connected`);
  } catch (err: any) {
    console.error('[Google Callback Route] Error exchange code:', err.message);
    return res.redirect(`${dashboardUrl}/dashboard/settings?google=error&reason=exchange_failed`);
  }
});

// ─── GET /google/status?clientId=xxx ──────────────────────────────────────────
// Retorna se o cliente possui uma agenda Google integrada
googleRouter.get('/status', async (req: Request, res: Response) => {
  const { clientId } = req.query;
  if (!clientId || typeof clientId !== 'string') {
    return res.status(400).json({ error: 'clientId is required' });
  }

  const connected = await isGoogleCalendarConnected(clientId);
  return res.json({ connected });
});

// ─── POST /google/disconnect?clientId=xxx ──────────────────────────────────────
// Remove a integração do Google Calendar do cliente
googleRouter.post('/disconnect', async (req: Request, res: Response) => {
  const { clientId } = req.query;
  if (!clientId || typeof clientId !== 'string') {
    return res.status(400).json({ error: 'clientId is required' });
  }

  try {
    await disconnectGoogleCalendar(clientId);
    return res.status(204).send();
  } catch (err: any) {
    return res.status(500).json({ error: err.message });
  }
});
