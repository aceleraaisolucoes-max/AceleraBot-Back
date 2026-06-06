import { Router, Request, Response } from 'express';
import { z } from 'zod';
import { supabase } from '../lib/supabase';
import { generateAIResponse, ChatMessage } from '../services/aiService';
import { sendTextMessage } from '../services/whatsappService';
import { notifyBusinessOwner, notifyBusinessOwnerAboutAppointment } from '../services/notifyService';

export const webhookRouter = Router();

// ─── Schema de validação do payload da Evolution API ─────────────────────────

const MessageUpsertSchema = z.object({
  event: z.string(),
  instance: z.string(),
  data: z.object({
    key: z.object({
      remoteJid: z.string(),
      fromMe: z.boolean(),
      id: z.string(),
    }),
    pushName: z.string().optional(),
    message: z.object({
      conversation: z.string().optional(),
      extendedTextMessage: z.object({ text: z.string() }).optional(),
      audioMessage: z.any().optional(),
    }).optional(),
    messageType: z.string(),
    messageTimestamp: z.number(),
  }),
});

// ─── POST /webhook/:clientId ──────────────────────────────────────────────────

webhookRouter.post('/:clientId', async (req: Request, res: Response) => {
  // Responde imediatamente para a Evolution API não re-tentar
  res.sendStatus(200);

  try {
    const parsed = MessageUpsertSchema.safeParse(req.body);
    if (!parsed.success) return;

    const { event, data } = parsed.data;

    // Só processa eventos de mensagens recebidas (não enviadas pelo bot)
    if (event !== 'messages.upsert') return;
    if (data.key.fromMe) return;

    const clientId = req.params.clientId;
    const leadPhone = data.key.remoteJid.replace('@s.whatsapp.net', '');
    const leadName = data.pushName;

    // Extrai o texto da mensagem (suporte a texto simples e extendido)
    const userMessage =
      data.message?.conversation ??
      data.message?.extendedTextMessage?.text;

    // Por ora ignora mídias (áudio, imagem) — implementar transcrição depois
    if (!userMessage) return;

    // ── 1. Buscar ou criar conversa ──────────────────────────────────────────

    let { data: conversation } = await supabase
      .from('conversations')
      .select('id, status, lead_score')
      .eq('client_id', clientId)
      .eq('lead_phone', leadPhone)
      .eq('status', 'active')
      .single();

    if (!conversation) {
      const { data: newConv } = await supabase
        .from('conversations')
        .insert({
          client_id: clientId,
          lead_phone: leadPhone,
          lead_name: leadName,
          status: 'active',
          lead_score: 0,
        })
        .select('id, status, lead_score')
        .single();
      conversation = newConv;
    }

    if (!conversation) return;

    // ── 2. Atualizar nome e timestamp ────────────────────────────────────────

    await supabase
      .from('conversations')
      .update({ lead_name: leadName, last_message_at: new Date().toISOString() })
      .eq('id', conversation.id);

    // ── 3. Salvar mensagem do usuário ────────────────────────────────────────

    await supabase.from('messages').insert({
      conversation_id: conversation.id,
      role: 'user',
      content: userMessage,
    });

    // ── 4. Buscar histórico de mensagens para contexto ───────────────────────

    const { data: history } = await supabase
      .from('messages')
      .select('role, content')
      .eq('conversation_id', conversation.id)
      .order('created_at', { ascending: true })
      .limit(30); // Últimas 30 mensagens para evitar exceder contexto

    const chatHistory: ChatMessage[] = (history ?? []).map(m => ({
      role: m.role === 'user' ? 'user' : 'model',
      parts: [{ text: m.content }],
    }));

    // ── 5. Gerar resposta da IA ──────────────────────────────────────────────

    const aiResponse = await generateAIResponse(
      clientId,
      userMessage,
      chatHistory,
      leadPhone,
      conversation.id
    );

    // ── 6. Salvar resposta da IA ─────────────────────────────────────────────

    await supabase.from('messages').insert({
      conversation_id: conversation.id,
      role: 'assistant',
      content: aiResponse.text,
    });

    // Atualizar score/status da conversa se agendado
    await supabase
      .from('conversations')
      .update({ 
        lead_score: aiResponse.isScheduled ? 100 : conversation.lead_score,
        status: aiResponse.isScheduled ? 'qualified' : conversation.status
      })
      .eq('id', conversation.id);

    // ── 7. Buscar instance_name do cliente ───────────────────────────────────

    const { data: client } = await supabase
      .from('clients')
      .select('instance_name')
      .eq('id', clientId)
      .single();

    if (!client?.instance_name) return;

    // ── 8. Enviar resposta pelo WhatsApp ─────────────────────────────────────

    await sendTextMessage({
      instanceName: client.instance_name,
      to: leadPhone,
      text: aiResponse.text,
    });

    // ── 9. Notificar dono se agendamento realizado ───────────────────────────

    if (aiResponse.isScheduled && aiResponse.appointmentData) {
      await notifyBusinessOwnerAboutAppointment(
        clientId,
        leadPhone,
        aiResponse.appointmentData
      );
    }
  } catch (err) {
    console.error('[Webhook] Error processing message:', err);
  }
});
