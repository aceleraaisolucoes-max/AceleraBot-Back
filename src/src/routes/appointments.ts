import { Router, Request, Response } from 'express';
import { supabase } from '../lib/supabase';
import { deleteCalendarEvent } from '../services/calendarService';

export const appointmentsRouter = Router();

// ─── GET /appointments?clientId=xxx ───────────────────────────────────────────
appointmentsRouter.get('/', async (req: Request, res: Response) => {
  const { clientId, page = '1', limit = '20' } = req.query;
  if (!clientId) return res.status(400).json({ error: 'clientId is required' });

  const offset = (Number(page) - 1) * Number(limit);

  const { data, error, count } = await supabase
    .from('appointments')
    .select('*, conversations(lead_name, lead_phone, lead_score)', { count: 'exact' })
    .eq('client_id', clientId as string)
    .order('start_time', { ascending: true })
    .range(offset, offset + Number(limit) - 1);

  if (error) return res.status(500).json({ error: error.message });
  return res.json({ data, total: count, page: Number(page), limit: Number(limit) });
});

// ─── GET /appointments/stats?clientId=xxx ─────────────────────────────────────
appointmentsRouter.get('/stats', async (req: Request, res: Response) => {
  const { clientId } = req.query;
  if (!clientId) return res.status(400).json({ error: 'clientId is required' });

  // Pega a data de hoje formatada (America/Sao_Paulo timezone)
  const todayStart = new Date();
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date();
  todayEnd.setHours(23, 59, 59, 999);

  const [totalConvs, activeAppts, todayAppts] = await Promise.all([
    supabase.from('conversations').select('*', { count: 'exact', head: true }).eq('client_id', clientId as string),
    supabase.from('appointments').select('*', { count: 'exact', head: true }).eq('client_id', clientId as string).eq('status', 'scheduled'),
    supabase.from('appointments')
      .select('*', { count: 'exact', head: true })
      .eq('client_id', clientId as string)
      .eq('status', 'scheduled')
      .gte('start_time', todayStart.toISOString())
      .lte('start_time', todayEnd.toISOString()),
  ]);

  const totalConversations = totalConvs.count ?? 0;
  const totalAppointments = activeAppts.count ?? 0;
  const conversionRate = totalConversations > 0
    ? Math.round((totalAppointments / totalConversations) * 100)
    : 0;

  return res.json({
    totalConversations,
    totalLeads: totalAppointments, // Mantendo a chave "totalLeads" compatível com o dashboard antigo do front se ele pedir, mas mapeando para agendamentos
    totalAppointments,
    todayAppointments: todayAppts.count ?? 0,
    todayLeads: todayAppts.count ?? 0, // Compatibilidade com front antigo
    conversionRate,
  });
});

// ─── POST /appointments/:appointmentId/cancel ─────────────────────────────────
appointmentsRouter.post('/:appointmentId/cancel', async (req: Request, res: Response) => {
  const { appointmentId } = req.params;
  const { clientId } = req.body;

  if (!clientId) {
    return res.status(400).json({ error: 'clientId is required' });
  }

  try {
    // 1. Buscar detalhes do agendamento para pegar o google_event_id
    const { data: appointment, error: fetchError } = await supabase
      .from('appointments')
      .select('google_event_id, client_id')
      .eq('id', appointmentId)
      .single();

    if (fetchError || !appointment) {
      return res.status(404).json({ error: 'Appointment not found' });
    }

    if (appointment.client_id !== clientId) {
      return res.status(403).json({ error: 'Unauthorized' });
    }

    // 2. Excluir no Google Calendar
    if (appointment.google_event_id) {
      await deleteCalendarEvent(clientId, appointment.google_event_id);
    }

    // 3. Atualizar status para cancelado no banco
    const { error: updateError } = await supabase
      .from('appointments')
      .update({ status: 'cancelled' })
      .eq('id', appointmentId);

    if (updateError) throw updateError;

    return res.status(200).json({ success: true });
  } catch (err: any) {
    console.error('[Cancel Appointment Route] Error:', err.message);
    return res.status(500).json({ error: err.message });
  }
});
