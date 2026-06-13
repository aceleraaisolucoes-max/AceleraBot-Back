import { defaultModel } from '../lib/gemini';
import { supabase } from '../lib/supabase';
import { listFreeSlots, createCalendarEvent } from './calendarService';

const apiKey = process.env.GEMINI_API_KEY!;
const isMockMode = !apiKey || apiKey.includes('AIzaSy...') || process.env.MOCK_MODE === 'true';

// ─── Tipos ────────────────────────────────────────────────────────────────────

export interface KnowledgeEntry {
  category: string;
  question?: string;
  answer: string;
}

export interface ChatMessage {
  role: 'user' | 'model';
  parts: Array<{ text: string }>;
}

export interface AIResponse {
  text: string;
  isScheduled: boolean;
  appointmentData?: {
    name: string;
    service: string;
    date: string;
    time: string;
  };
}

// ─── Prompt do Sistema ────────────────────────────────────────────────────────

function buildSystemPrompt(businessName: string, knowledge: KnowledgeEntry[], currentDateStr: string): string {
  const knowledgeText = knowledge
    .map(k => `[${k.category.toUpperCase()}] ${k.question ? `P: ${k.question}\nR: ` : ''}${k.answer}`)
    .join('\n\n');

  return `Você é um assistente de atendimento virtual e recepcionista para "${businessName}". 
Seu objetivo principal é responder a dúvidas de clientes no WhatsApp de forma natural, humana e agendar serviços no Google Calendar.

═══════════════════════════════════════════
CONTEXTO DE DATA E HORA ATUAL:
${currentDateStr}
═══════════════════════════════════════════

═══════════════════════════════════════════
BASE DE CONHECIMENTO DO NEGÓCIO:
${knowledgeText}
═══════════════════════════════════════════

REGRAS DE COMPORTAMENTO:
1. Responda de forma amigável e direta (mensagens curtas, ideal para leitura rápida no WhatsApp).
2. Se o cliente quiser marcar um horário, perguntar por vagas ou sugerir uma data, você DEVE chamar a função/ferramenta 'list_available_slots' passando a data no formato YYYY-MM-DD. Use o CONTEXTO DE DATA E HORA acima para calcular dias da semana ou termos como "amanhã", "segunda que vem", etc.
3. Se o cliente escolher um horário, obtenha as seguintes informações se ainda não as tiver:
   - Nome completo do cliente
   - O serviço que ele deseja fazer (pergunte se ele não disser)
4. Com o nome, serviço, data e horário confirmado, chame 'schedule_appointment' para registrar o compromisso.
5. Sempre confirme ao cliente que o horário foi agendado com sucesso e salvo no calendário do negócio.
6. Nunca invente dados que não estão na base de conhecimento. Se não souber, diga que vai chamar um atendente humano.
`;
}

// ─── Função Principal ─────────────────────────────────────────────────────────

/**
 * Gera uma resposta da IA com base no contexto da conversa e executa chamadas de ferramenta do Google Calendar.
 */
export async function generateAIResponse(
  clientId: string,
  userMessage: string,
  history: ChatMessage[],
  leadPhone: string,
  conversationId: string
): Promise<AIResponse> {
  // ─── Fluxo Mock Mode ────────────────────────────────────────────────────────
  if (isMockMode) {
    console.log(`[Mock AI] Processing message: "${userMessage}" for conversation ${conversationId}`);
    const msg = userMessage.toLowerCase();
    let replyText = "";
    let isScheduled = false;
    let appointmentData: AIResponse['appointmentData'];

    // Obter data de amanhã
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const tomorrowStr = tomorrow.toISOString().split('T')[0];

    if (msg.includes('olá') || msg.includes('oi') || msg.includes('bom dia') || msg.includes('boa tarde')) {
      replyText = "Olá! Sou o assistente virtual da nossa oficina. Gostaria de agendar um serviço ou tirar alguma dúvida?";
    } else if (msg.includes('14h') || msg.includes('14:00') || msg.includes('16:30') || msg.includes('16h30')) {
      const selectedTime = (msg.includes('14h') || msg.includes('14:00')) ? '14:00' : '16:30';
      
      // Simula agendamento
      try {
        await createCalendarEvent(clientId, {
          name: 'Carlos Silva (Simulado)',
          phone: leadPhone,
          date: tomorrowStr,
          time: selectedTime,
          service: 'Troca de óleo',
          conversationId
        });
        
        replyText = `*Agendado com sucesso!* 📅\nJá salvei o compromisso (Troca de óleo) para amanhã às *${selectedTime}* no Google Calendar da clínica.\nPosso ajudar com mais alguma coisa?`;
        isScheduled = true;
        appointmentData = {
          name: 'Carlos Silva (Simulado)',
          service: 'Troca de óleo',
          date: tomorrowStr,
          time: selectedTime
        };
      } catch (err: any) {
        console.error('[Mock AI] Error scheduling appointment:', err);
        replyText = "Tivemos um problema ao registrar seu agendamento. Um atendente humano entrará em contato para confirmar.";
      }
    } else if (msg.includes('agendar') || msg.includes('horário') || msg.includes('vaga') || msg.includes('amanhã') || msg.includes('marcar')) {
      replyText = `Temos horários disponíveis para amanhã (${tomorrowStr}) às *14:00* e às *16:30*. Qual desses horários você prefere?`;
    } else {
      replyText = "Entendido! Se tiver alguma dúvida sobre nossos serviços ou quiser marcar outro horário, estou à disposição.";
    }

    return { text: replyText, isScheduled, appointmentData };
  }

  // ─── Fluxo Real com Gemini Tool Calls (Function Calling) ────────────────────
  
  // 1. Buscar dados do cliente e base de conhecimento
  const { data: client } = await supabase
    .from('clients')
    .select('business_name')
    .eq('id', clientId)
    .single();

  const { data: knowledge } = await supabase
    .from('knowledge_base')
    .select('category, question, answer')
    .eq('client_id', clientId);

  const businessName = client?.business_name ?? 'o negócio';
  
  // Contexto dinâmico de Data/Hora (Horário de Brasília)
  const formatter = new Intl.DateTimeFormat('pt-BR', {
    dateStyle: 'full',
    timeStyle: 'short',
    timeZone: 'America/Sao_Paulo',
  });
  const currentDateStr = formatter.format(new Date());

  const systemPrompt = buildSystemPrompt(businessName, knowledge ?? [], currentDateStr);

  // 2. Preparar histórico com system prompt injetado no início
  const chatHistory: ChatMessage[] = [
    {
      role: 'user',
      parts: [{ text: `[CONFIGURAÇÃO DO SISTEMA - NÃO RESPONDA ISSO]\n${systemPrompt}` }],
    },
    {
      role: 'model',
      parts: [{ text: 'Entendido! Sou o recepcionista virtual e estou pronto para tirar dúvidas e agendar compromissos utilizando as ferramentas de calendário.' }],
    },
    ...history,
  ];

  // 3. Iniciar Chat com Gemini
  const chat = defaultModel.startChat({ history: chatHistory });
  let response = await chat.sendMessage(userMessage);
  let functionCalls = response.response.functionCalls;
  
  let isScheduled = false;
  let appointmentData: AIResponse['appointmentData'];

  // 4. Loop de resolução de Tool Calls
  while (functionCalls && functionCalls.length > 0) {
    const call = functionCalls[0];
    const { name, args } = call;
    
    console.log(`[Gemini Tool Exec] Calling ${name} with args:`, args);
    let toolResult: any;

    if (name === 'list_available_slots') {
      const { date } = args as { date: string };
      const slots = await listFreeSlots(clientId, date);
      toolResult = { slots };
    } else if (name === 'schedule_appointment') {
      const { name: clientName, date, time, service } = args as { name: string; date: string; time: string; service?: string };
      
      try {
        const appt = await createCalendarEvent(clientId, {
          name: clientName,
          phone: leadPhone,
          date,
          time,
          service: service ?? 'Geral',
          conversationId
        });
        
        isScheduled = true;
        appointmentData = {
          name: clientName,
          service: service ?? 'Geral',
          date,
          time
        };
        
        toolResult = { success: true, message: 'Agendamento registrado com sucesso no Google Calendar e banco de dados.', appointmentId: appt.id };
      } catch (err: any) {
        toolResult = { success: false, error: err.message };
      }
    } else {
      toolResult = { error: 'Ferramenta não encontrada.' };
    }

    // Envia o retorno da ferramenta de volta ao chat
    response = await chat.sendMessage([
      {
        functionResponse: {
          name,
          response: toolResult
        }
      }
    ]);
    
    functionCalls = response.response.functionCalls;
  }

  const finalReplyText = response.response.text();
  return { text: finalReplyText, isScheduled, appointmentData };
}
