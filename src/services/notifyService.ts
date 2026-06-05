import { supabase } from '../lib/supabase';
import { sendTextMessage } from './whatsappService';

// ─── Tipos ────────────────────────────────────────────────────────────────────

interface LeadData {
  name?: string;
  service?: string;
  urgency?: string;
  details?: string;
}

// ─── Notificação Principal ────────────────────────────────────────────────────

/**
 * Notifica o dono do negócio quando um lead qualificado é identificado.
 * A notificação é enviada via WhatsApp para o número de NOTIFICAÇÃO do cliente.
 */
export async function notifyBusinessOwner(
  clientId: string,
  leadPhone: string,
  conversationId: string,
  leadData: LeadData
): Promise<void> {
  // 1. Buscar dados do cliente (dono do negócio)
  const { data: client } = await supabase
    .from('clients')
    .select('business_name, whatsapp_number, notification_number, instance_name')
    .eq('id', clientId)
    .single();

  if (!client) return;

  // 2. Formatar a mensagem de notificação
  const notificationPhone = client.notification_number ?? client.whatsapp_number;
  const instanceName = client.instance_name;
  const message = buildNotificationMessage(leadPhone, leadData);

  // 3. Enviar mensagem WhatsApp para o dono
  await sendTextMessage({
    instanceName,
    to: notificationPhone,
    text: message,
    delay: 500,
  });

  // 4. Registrar o lead no banco
  await supabase.from('leads').insert({
    conversation_id: conversationId,
    client_id: clientId,
    lead_phone: leadPhone,
    lead_name: leadData.name,
    service_interest: leadData.service,
    urgency: leadData.urgency,
    notified_at: new Date().toISOString(),
  });

  // 5. Atualizar status da conversa
  await supabase
    .from('conversations')
    .update({ status: 'qualified' })
    .eq('id', conversationId);
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function buildNotificationMessage(leadPhone: string, leadData: LeadData): string {
  const lines = [
    '🔥 *NOVO LEAD QUENTE!*',
    '',
    `👤 *Cliente:* ${leadData.name ?? 'Não informado'}`,
    `📱 *WhatsApp:* +${leadPhone}`,
  ];

  if (leadData.service) lines.push(`🛠️ *Interesse:* ${leadData.service}`);
  if (leadData.details) lines.push(`📋 *Detalhes:* ${leadData.details}`);
  if (leadData.urgency) lines.push(`⏰ *Urgência:* ${leadData.urgency}`);

  lines.push('');
  lines.push('👇 *Clique para continuar o atendimento:*');
  lines.push(`https://wa.me/${leadPhone}`);
  lines.push('');
  lines.push('_AceleraBot — Seu assistente de IA_ 🤖');

  return lines.join('\n');
}
