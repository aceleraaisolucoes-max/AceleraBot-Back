import { defaultModel } from '../lib/gemini';
import { supabase } from '../lib/supabase';

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
  isQualifiedLead: boolean;
  leadScore: number;
  leadData?: {
    name?: string;
    service?: string;
    urgency?: string;
    details?: string;
  };
}

// ─── Prompt do Sistema ────────────────────────────────────────────────────────

function buildSystemPrompt(businessName: string, knowledge: KnowledgeEntry[]): string {
  const knowledgeText = knowledge
    .map(k => `[${k.category.toUpperCase()}] ${k.question ? `P: ${k.question}\nR: ` : ''}${k.answer}`)
    .join('\n\n');

  return `Você é um assistente de atendimento virtual para "${businessName}". 
Seu objetivo é atender clientes no WhatsApp de forma natural, humana e eficiente.

═══════════════════════════════════════════
BASE DE CONHECIMENTO DO NEGÓCIO:
═══════════════════════════════════════════
${knowledgeText}
═══════════════════════════════════════════

REGRAS DE COMPORTAMENTO:
1. Responda de forma natural como um atendente humano faria no WhatsApp (mensagens curtas, sem formalidade excessiva)
2. Faça perguntas de qualificação progressivamente: qual serviço precisa → detalhes (modelo, especificações) → urgência → disponibilidade
3. Nunca invente informações que não estão na base de conhecimento
4. Se não souber responder, diga: "Essa é uma boa pergunta! Vou chamar um atendente para te ajudar melhor. Um momento!"
5. Seja empático e positivo

QUALIFICAÇÃO DE LEADS:
Ao final de cada resposta, inclua um bloco JSON oculto (não visível ao cliente) no seguinte formato:
<lead_data>
{
  "score": 0,
  "isQualified": false,
  "name": null,
  "service": null,
  "urgency": null,
  "details": null
}
</lead_data>

- score: 0-100 (100 = pronto para fechar)
- isQualified: true quando score >= 80
- Atualize os campos conforme as informações forem coletadas

CRITÉRIOS DE PONTUAÇÃO:
- Cliente informou o serviço desejado: +30 pontos
- Cliente informou detalhes específicos (modelo, marca, etc.): +25 pontos  
- Cliente informou urgência (ex: "preciso essa semana"): +25 pontos
- Cliente não fez objeção de preço após ver o valor: +20 pontos`;
}

// ─── Parser do JSON da resposta da IA ────────────────────────────────────────

function parseAIResponse(rawText: string): AIResponse {
  const leadDataMatch = rawText.match(/<lead_data>([\s\S]*?)<\/lead_data>/);
  let isQualifiedLead = false;
  let leadScore = 0;
  let leadData: AIResponse['leadData'];

  if (leadDataMatch) {
    try {
      const parsed = JSON.parse(leadDataMatch[1].trim());
      isQualifiedLead = parsed.isQualified ?? false;
      leadScore = parsed.score ?? 0;
      leadData = {
        name: parsed.name ?? undefined,
        service: parsed.service ?? undefined,
        urgency: parsed.urgency ?? undefined,
        details: parsed.details ?? undefined,
      };
    } catch {
      // JSON malformado — ignora e segue com valores padrão
    }
  }

  // Remove o bloco <lead_data> da resposta visível ao cliente
  const cleanText = rawText.replace(/<lead_data>[\s\S]*?<\/lead_data>/g, '').trim();

  return { text: cleanText, isQualifiedLead, leadScore, leadData };
}

// ─── Função Principal ─────────────────────────────────────────────────────────

/**
 * Gera uma resposta da IA com base no contexto da conversa.
 *
 * @param clientId - ID do cliente AceleraBot (dono do negócio)
 * @param userMessage - Mensagem enviada pelo cliente final
 * @param history - Histórico de mensagens da conversa (Gemini format)
 */
export async function generateAIResponse(
  clientId: string,
  userMessage: string,
  history: ChatMessage[]
): Promise<AIResponse> {
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
  const systemPrompt = buildSystemPrompt(businessName, knowledge ?? []);

  // 2. Preparar histórico sem o system prompt (Gemini não aceita role 'system' no histórico)
  const chatHistory: ChatMessage[] = [
    // Primeiro turno injeta o system prompt como contexto inicial
    {
      role: 'user',
      parts: [{ text: `[CONFIGURAÇÃO DO SISTEMA - NÃO RESPONDA ISSO]\n${systemPrompt}` }],
    },
    {
      role: 'model',
      parts: [{ text: 'Entendido! Estou pronto para atender os clientes conforme as instruções.' }],
    },
    ...history,
  ];

  // 3. Chamar Gemini
  const chat = defaultModel.startChat({ history: chatHistory });
  const result = await chat.sendMessage(userMessage);
  const rawText = result.response.text();

  return parseAIResponse(rawText);
}
