import { GoogleGenerativeAI, SchemaType } from '@google/generative-ai';

const apiKey = process.env.GEMINI_API_KEY!;
const isMock = !apiKey || apiKey.includes('AIzaSy...') || process.env.MOCK_MODE === 'true';

let modelInstance: any;

if (isMock) {
  console.log('⚠️ Running Gemini in Mock Mode (returns simulated answers)');
  modelInstance = {
    startChat: ({ history }: any) => {
      return {
        sendMessage: async (userMessage: string) => {
          const msg = userMessage.toLowerCase();
          let replyText = "";
          let score = 30;
          let isQualified = false;
          let service = "Manutenção";
          let urgency = "Média";
          let details = "Conversa simulada";

          if (msg.includes('olá') || msg.includes('oi') || msg.includes('bom dia') || msg.includes('boa tarde')) {
            replyText = "Olá! Como posso ajudar você hoje na nossa oficina?";
            score = 15;
          } else if (msg.includes('preço') || msg.includes('valor') || msg.includes('quanto custa') || msg.includes('óleo')) {
            replyText = "A troca de óleo custa a partir de R$ 180. Fazemos também alinhamento e balanceamento. Qual serviço você gostaria?";
            score = 45;
            service = "Troca de óleo";
          } else if (msg.includes('urgente') || msg.includes('rápido') || msg.includes('hoje') || msg.includes('essa semana')) {
            replyText = "Temos horários disponíveis para hoje e amanhã! Qual o modelo e ano do seu carro para agendarmos?";
            score = 85;
            isQualified = true;
            urgency = "Urgente";
            details = "Cliente quer agendar troca de óleo com urgência";
            service = "Troca de óleo";
          } else {
            replyText = "Entendi! Vou repassar isso para um especialista para te dar a melhor resposta em instantes.";
            score = 90;
            isQualified = true;
            details = "Dúvida de cliente repassada: " + userMessage;
          }

          const rawText = `${replyText}\n\n<lead_data>\n{\n  "score": ${score},\n  "isQualified": ${isQualified},\n  "name": "Carlos Silva",\n  "service": "${service}",\n  "urgency": "${urgency}",\n  "details": "${details}"\n}\n</lead_data>`;
          
          return {
            response: {
              text: () => rawText
            }
          };
        }
      };
    }
  };
} else {
  if (!apiKey) {
    throw new Error('Missing GEMINI_API_KEY environment variable.');
  }
  const genAI = new GoogleGenerativeAI(apiKey);
  modelInstance = genAI.getGenerativeModel({
    model: 'gemini-1.5-flash',
    generationConfig: {
      temperature: 0.7,
      topK: 40,
      topP: 0.95,
      maxOutputTokens: 1024,
    },
    tools: [
      {
        functionDeclarations: [
          {
            name: 'list_available_slots',
            description: 'Busca os horários livres na agenda para uma data específica no formato YYYY-MM-DD. Use sempre que o cliente perguntar por vagas ou sugerir um dia.',
            parameters: {
              type: SchemaType.OBJECT,
              properties: {
                date: {
                  type: SchemaType.STRING,
                  description: 'A data a ser consultada no formato YYYY-MM-DD.'
                }
              },
              required: ['date']
            }
          },
          {
            name: 'schedule_appointment',
            description: 'Confirma e agenda um compromisso no calendário. Use apenas após obter o nome do cliente, serviço, data e horário confirmados pelo cliente.',
            parameters: {
              type: SchemaType.OBJECT,
              properties: {
                name: {
                  type: SchemaType.STRING,
                  description: 'Nome completo do cliente.'
                },
                date: {
                  type: SchemaType.STRING,
                  description: 'Data do compromisso no formato YYYY-MM-DD.'
                },
                time: {
                  type: SchemaType.STRING,
                  description: 'Horário do compromisso no formato HH:MM.'
                },
                service: {
                  type: SchemaType.STRING,
                  description: 'Serviço a ser realizado.'
                }
              },
              required: ['name', 'date', 'time', 'service']
            }
          }
        ]
      }
    ]
  });
}

export const defaultModel = modelInstance;
