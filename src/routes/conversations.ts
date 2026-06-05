import { Router, Request, Response } from 'express';
import { supabase } from '../lib/supabase';

export const conversationsRouter = Router();

// ─── GET /conversations?clientId=xxx ─────────────────────────────────────────

conversationsRouter.get('/', async (req: Request, res: Response) => {
  const { clientId, status, page = '1', limit = '20' } = req.query;
  if (!clientId) return res.status(400).json({ error: 'clientId is required' });

  const offset = (Number(page) - 1) * Number(limit);

  let query = supabase
    .from('conversations')
    .select('*', { count: 'exact' })
    .eq('client_id', clientId as string)
    .order('last_message_at', { ascending: false })
    .range(offset, offset + Number(limit) - 1);

  if (status) query = query.eq('status', status as string);

  const { data, error, count } = await query;
  if (error) return res.status(500).json({ error: error.message });

  return res.json({ data, total: count, page: Number(page), limit: Number(limit) });
});

// ─── GET /conversations/:conversationId/messages ──────────────────────────────

conversationsRouter.get('/:conversationId/messages', async (req: Request, res: Response) => {
  const { data, error } = await supabase
    .from('messages')
    .select('*')
    .eq('conversation_id', req.params.conversationId)
    .order('created_at', { ascending: true });

  if (error) return res.status(500).json({ error: error.message });
  return res.json(data);
});

// ─── PATCH /conversations/:conversationId/takeover ───────────────────────────
// Dono do negócio assume o atendimento (pausa o bot)

conversationsRouter.patch('/:conversationId/takeover', async (req: Request, res: Response) => {
  const { data, error } = await supabase
    .from('conversations')
    .update({ status: 'human_takeover' })
    .eq('id', req.params.conversationId)
    .select()
    .single();

  if (error) return res.status(500).json({ error: error.message });
  return res.json(data);
});

// ─── PATCH /conversations/:conversationId/close ───────────────────────────────

conversationsRouter.patch('/:conversationId/close', async (req: Request, res: Response) => {
  const { data, error } = await supabase
    .from('conversations')
    .update({ status: 'closed' })
    .eq('id', req.params.conversationId)
    .select()
    .single();

  if (error) return res.status(500).json({ error: error.message });
  return res.json(data);
});
