import axios from 'axios';

const EVOLUTION_API_URL = process.env.EVOLUTION_API_URL!;
const EVOLUTION_API_KEY = process.env.EVOLUTION_API_KEY!;

const evolutionClient = axios.create({
  baseURL: EVOLUTION_API_URL,
  headers: {
    apikey: EVOLUTION_API_KEY,
    'Content-Type': 'application/json',
  },
  timeout: 15000,
});

// ─── Tipos ────────────────────────────────────────────────────────────────────

export interface SendTextOptions {
  instanceName: string; // Nome da instância (= clientId ou número do WhatsApp)
  to: string;           // Número do destinatário com DDI (ex: 5511999999999)
  text: string;
  delay?: number;       // Delay em ms antes de enviar (simula digitação)
}

export interface CreateInstanceResult {
  instance: { instanceName: string; status: string };
  hash: { apikey: string };
  webhook?: { url: string; enabled: boolean };
  events?: string[];
}

// ─── Funções de Mensagem ──────────────────────────────────────────────────────

/**
 * Envia uma mensagem de texto para um número no WhatsApp.
 */
export async function sendTextMessage({
  instanceName,
  to,
  text,
  delay = 1200,
}: SendTextOptions): Promise<void> {
  await evolutionClient.post(`/message/sendText/${instanceName}`, {
    number: to,
    text,
    delay,
    linkPreview: false,
  });
}

/**
 * Envia uma reação (emoji) a uma mensagem.
 */
export async function sendReaction(
  instanceName: string,
  to: string,
  messageId: string,
  emoji: string
): Promise<void> {
  await evolutionClient.post(`/message/sendReaction/${instanceName}`, {
    key: { remoteJid: `${to}@s.whatsapp.net`, id: messageId },
    reaction: emoji,
  });
}

// ─── Gerenciamento de Instâncias ──────────────────────────────────────────────

/**
 * Cria uma nova instância do WhatsApp para um cliente.
 * Deve ser chamado durante o onboarding do cliente.
 */
export async function createInstance(instanceName: string, webhookUrl: string): Promise<CreateInstanceResult> {
  const { data } = await evolutionClient.post('/instance/create', {
    instanceName,
    token: instanceName, // token = instanceName para simplicidade
    qrcode: true,
    integration: 'WHATSAPP-BAILEYS',
    webhook: {
      url: webhookUrl,
      byEvents: true,
      base64: false,
      events: [
        'MESSAGES_UPSERT',
        'MESSAGES_UPDATE',
        'CONNECTION_UPDATE',
      ],
    },
  });
  return data;
}

/**
 * Obtém o QR Code de uma instância para o cliente escanear.
 */
export async function getQRCode(instanceName: string): Promise<{ base64: string; code: string } | null> {
  try {
    const { data } = await evolutionClient.get(`/instance/connect/${instanceName}`);
    return data;
  } catch {
    return null;
  }
}

/**
 * Verifica o status de conexão de uma instância.
 */
export async function getInstanceStatus(instanceName: string): Promise<'open' | 'close' | 'connecting' | null> {
  try {
    const { data } = await evolutionClient.get(`/instance/connectionState/${instanceName}`);
    return data?.instance?.state ?? null;
  } catch {
    return null;
  }
}

/**
 * Deleta uma instância (quando o cliente cancela o serviço).
 */
export async function deleteInstance(instanceName: string): Promise<void> {
  await evolutionClient.delete(`/instance/delete/${instanceName}`);
}
