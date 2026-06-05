import { Router, Request, Response } from 'express';
import { supabase } from '../lib/supabase';

export const leadsRouter = Router();

// ─── GET /leads?clientId=xxx ──────────────────────────────────────────────────

leadsRouter.get('/', async (req: Request, res: Response) => {
  const { clientId, page = '1', limit = '20' } = req.query;
  if (!clientId) return res.status(400).json({ error: 'clientId is required' });

  const offset = (Number(page) - 1) * Number(limit);

  const { data, error, count } = await supabase
    .from('leads')
    .select('*, conversations(lead_name, lead_phone, lead_score)', { count: 'exact' })
    .eq('client_id', clientId as string)
    .order('created_at', { ascending: false })
    .range(offset, offset + Number(limit) - 1);

  if (error) return res.status(500).json({ error: error.message });
  return res.json({ data, total: count, page: Number(page), limit: Number(limit) });
});

// ─── GET /leads/stats?clientId=xxx ───────────────────────────────────────────

leadsRouter.get('/stats', async (req: Request, res: Response) => {
  const { clientId } = req.query;
  if (!clientId) return res.status(400).json({ error: 'clientId is required' });

  const [totalConvs, qualifiedLeads, todayLeads] = await Promise.all([
    supabase.from('conversations').select('*', { count: 'exact', head: true }).eq('client_id', clientId as string),
    supabase.from('leads').select('*', { count: 'exact', head: true }).eq('client_id', clientId as string),
    supabase.from('leads')
      .select('*', { count: 'exact', head: true })
      .eq('client_id', clientId as string)
      .gte('created_at', new Date(new Date().setHours(0, 0, 0, 0)).toISOString()),
  ]);

  const totalConversations = totalConvs.count ?? 0;
  const totalLeads = qualifiedLeads.count ?? 0;
  const conversionRate = totalConversations > 0
    ? Math.round((totalLeads / totalConversations) * 100)
    : 0;

  return res.json({
    totalConversations,
    totalLeads,
    todayLeads: todayLeads.count ?? 0,
    conversionRate,
  });
});
