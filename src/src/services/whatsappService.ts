import axios from 'axios';

const EVOLUTION_API_URL = process.env.EVOLUTION_API_URL!;
const EVOLUTION_API_KEY = process.env.EVOLUTION_API_KEY!;

const isMock = !EVOLUTION_API_KEY || 
               EVOLUTION_API_KEY === 'sua_chave_secreta_aqui' || 
               process.env.MOCK_MODE === 'true';

const evolutionClient = isMock ? null : axios.create({
  baseURL: EVOLUTION_API_URL,
  headers: {
    apikey: EVOLUTION_API_KEY,
    'Content-Type': 'application/json',
  },
  timeout: 15000,
});

// Mock connection state that transitions from close -> connecting -> open
let mockConnectionState = 'close';

// ─── Tipos ────────────────────────────────────────────────────────────────────

export interface SendTextOptions {
  instanceName: string;
  to: string;
  text: string;
  delay?: number;
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
  if (isMock) {
    console.log(`[Mock WhatsApp] Sending message via '${instanceName}' to ${to} (delay: ${delay}ms):`);
    console.log(`> ${text}`);
    return;
  }
  await evolutionClient!.post(`/message/sendText/${instanceName}`, {
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
  if (isMock) {
    console.log(`[Mock WhatsApp] Sending reaction '${emoji}' to message '${messageId}' via '${instanceName}' to ${to}`);
    return;
  }
  await evolutionClient!.post(`/message/sendReaction/${instanceName}`, {
    key: { remoteJid: `${to}@s.whatsapp.net`, id: messageId },
    reaction: emoji,
  });
}

// ─── Gerenciamento de Instâncias ──────────────────────────────────────────────

/**
 * Cria uma nova instância do WhatsApp para um cliente.
 */
export async function createInstance(instanceName: string, webhookUrl: string): Promise<CreateInstanceResult> {
  if (isMock) {
    console.log(`[Mock WhatsApp] Creating instance '${instanceName}' with webhook '${webhookUrl}'`);
    mockConnectionState = 'close';
    return {
      instance: { instanceName, status: 'created' },
      hash: { apikey: 'mock_instance_key' }
    };
  }
  const { data } = await evolutionClient!.post('/instance/create', {
    instanceName,
    token: instanceName,
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
  if (isMock) {
    console.log(`[Mock WhatsApp] Generating QR Code for instance '${instanceName}'`);
    // Simulated small green box / fake QR code base64
    const fakeQrBase64 = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAQAAAAEAAQMAAABFGoRRAAAABlBMVEUAAAD///+l2Z/dAAAAMklEQVR42mP8z8AARjDw/2fADn7+h0Ew+PkfBsHg538YBIOf/2EQDH7+h0Ew+PkfBsHgRwAAXJ/w98x27CgAAAAASUVORK5CYII=';
    mockConnectionState = 'connecting';
    return {
      base64: fakeQrBase64,
      code: 'mock_qr_code_string'
    };
  }
  try {
    const { data } = await evolutionClient!.get(`/instance/connect/${instanceName}`);
    return data;
  } catch {
    return null;
  }
}

/**
 * Verifica o status de conexão de uma instância.
 */
export async function getInstanceStatus(instanceName: string): Promise<'open' | 'close' | 'connecting' | null> {
  if (isMock) {
    // If it was close, transition to connecting. If it was connecting, transition to open.
    if (mockConnectionState === 'close') {
      mockConnectionState = 'connecting';
    } else if (mockConnectionState === 'connecting') {
      mockConnectionState = 'open';
    }
    return mockConnectionState as any;
  }
  try {
    const { data } = await evolutionClient!.get(`/instance/connectionState/${instanceName}`);
    return data?.instance?.state ?? null;
  } catch {
    return null;
  }
}

/**
 * Deleta uma instância.
 */
export async function deleteInstance(instanceName: string): Promise<void> {
  if (isMock) {
    console.log(`[Mock WhatsApp] Deleting instance '${instanceName}'`);
    mockConnectionState = 'close';
    return;
  }
  await evolutionClient!.delete(`/instance/delete/${instanceName}`);
}
