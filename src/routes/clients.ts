import { Router, Request, Response } from 'express';
import { z } from 'zod';
import { supabase } from '../lib/supabase';
import { createInstance, getQRCode, getInstanceStatus, deleteInstance } from '../services/whatsappService';

export const clientsRouter = Router();

// ─── Schemas ──────────────────────────────────────────────────────────────────

const CreateClientSchema = z.object({
  user_id: z.string().uuid(),
  business_name: z.string().min(2).max(100),
  whatsapp_number: z.string().min(10).max(20),
  notification_number: z.string().min(10).max(20).optional(),
  plan: z.enum(['motor', 'ecosystem']).default('motor'),
});

// ─── GET /clients/:clientId ───────────────────────────────────────────────────

clientsRouter.get('/:clientId', async (req: Request, res: Response) => {
  const { data, error } = await supabase
    .from('clients')
    .select('*')
    .eq('id', req.params.clientId)
    .single();

  if (error || !data) return res.status(404).json({ error: 'Client not found' });
  return res.json(data);
});

// ─── POST /clients ────────────────────────────────────────────────────────────

clientsRouter.post('/', async (req: Request, res: Response) => {
  const parsed = CreateClientSchema.safeParse(req.body);
  if (!parsed.success) return res.status(400).json({ error: parsed.error.flatten() });

  const payload = parsed.data;
  const instanceName = `acelera_${payload.whatsapp_number}`;
  const webhookUrl = `${process.env.APP_URL}/webhook/${payload.user_id}`;

  // 1. Criar registro no banco
  const { data: client, error } = await supabase
    .from('clients')
    .insert({ ...payload, instance_name: instanceName, status: 'trial' })
    .select()
    .single();

  if (error) return res.status(500).json({ error: error.message });

  // 2. Criar instância na Evolution API (WhatsApp)
  try {
    await createInstance(instanceName, webhookUrl);
  } catch (err) {
    console.error('[Clients] Failed to create Evolution instance:', err);
    // Não bloqueia o cadastro — o usuário pode tentar reconectar depois
  }

  return res.status(201).json(client);
});

// ─── GET /clients/:clientId/qrcode ───────────────────────────────────────────

clientsRouter.get('/:clientId/qrcode', async (req: Request, res: Response) => {
  const { data: client } = await supabase
    .from('clients')
    .select('instance_name')
    .eq('id', req.params.clientId)
    .single();

  if (!client?.instance_name) return res.status(404).json({ error: 'Instance not found' });

  const qrCode = await getQRCode(client.instance_name);
  if (!qrCode) return res.status(503).json({ error: 'QR Code not available. Please try again.' });

  return res.json(qrCode);
});

// ─── GET /clients/:clientId/status ───────────────────────────────────────────

clientsRouter.get('/:clientId/status', async (req: Request, res: Response) => {
  const { data: client } = await supabase
    .from('clients')
    .select('instance_name')
    .eq('id', req.params.clientId)
    .single();

  if (!client?.instance_name) return res.status(404).json({ error: 'Instance not found' });

  const status = await getInstanceStatus(client.instance_name);
  return res.json({ status });
});

// ─── DELETE /clients/:clientId ────────────────────────────────────────────────

clientsRouter.delete('/:clientId', async (req: Request, res: Response) => {
  const { data: client } = await supabase
    .from('clients')
    .select('instance_name')
    .eq('id', req.params.clientId)
    .single();

  if (client?.instance_name) {
    await deleteInstance(client.instance_name).catch(console.error);
  }

  await supabase.from('clients').delete().eq('id', req.params.clientId);
  return res.status(204).send();
});
