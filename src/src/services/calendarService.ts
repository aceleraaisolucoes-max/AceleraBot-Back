import axios from 'axios';
import { supabase } from '../lib/supabase';

const GOOGLE_CLIENT_ID = process.env.GOOGLE_CLIENT_ID;
const GOOGLE_CLIENT_SECRET = process.env.GOOGLE_CLIENT_SECRET;
const GOOGLE_REDIRECT_URI = process.env.GOOGLE_REDIRECT_URI;

const isMockGoogle = !GOOGLE_CLIENT_ID || 
                      GOOGLE_CLIENT_ID === 'seu_google_client_id' || 
                      process.env.MOCK_MODE === 'true';

export interface AppointmentDetails {
  name: string;
  phone: string;
  date: string; // YYYY-MM-DD
  time: string; // HH:MM
  service?: string;
  conversationId?: string;
}

/**
 * Gera a URL do consentimento do Google OAuth para o cliente do AceleraBot.
 */
export function getAuthUrl(clientId: string): string {
  if (isMockGoogle) {
    console.log(`[Mock Google OAuth] Generating login URL for client '${clientId}'`);
    const mockCallback = `${process.env.APP_URL ?? 'http://localhost:3000'}/google/callback?code=mock_authorization_code&state=${clientId}`;
    return mockCallback;
  }

  const scopes = [
    'https://www.googleapis.com/auth/calendar.events',
    'https://www.googleapis.com/auth/calendar.readonly'
  ];

  return `https://accounts.google.com/o/oauth2/v2/auth?` +
    `response_type=code` +
    `&client_id=${GOOGLE_CLIENT_ID}` +
    `&redirect_uri=${encodeURIComponent(GOOGLE_REDIRECT_URI!)}` +
    `&scope=${encodeURIComponent(scopes.join(' '))}` +
    `&access_type=offline` +
    `&prompt=consent` +
    `&state=${clientId}`;
}

/**
 * Troca o código recebido no callback do Google pelos tokens Access e Refresh.
 */
export async function exchangeCodeForTokens(clientId: string, code: string): Promise<void> {
  if (isMockGoogle || code === 'mock_authorization_code') {
    console.log(`[Mock Google OAuth] Exchanging code '${code}' for tokens for client '${clientId}'`);
    const expiryDate = Date.now() + 3600 * 1000; // 1 hora
    
    // Deleta se já existir configuração para evitar conflito de chave única
    await supabase.from('google_calendar_configs').delete().eq('client_id', clientId);

    await supabase.from('google_calendar_configs').insert({
      client_id: clientId,
      access_token: 'mock_access_token_123',
      refresh_token: 'mock_refresh_token_123',
      expiry_date: expiryDate,
      calendar_id: 'primary'
    });
    return;
  }

  try {
    const { data } = await axios.post('https://oauth2.googleapis.com/token', {
      code,
      client_id: GOOGLE_CLIENT_ID,
      client_secret: GOOGLE_CLIENT_SECRET,
      redirect_uri: GOOGLE_REDIRECT_URI,
      grant_type: 'authorization_code',
    });

    const expiryDate = Date.now() + (data.expires_in * 1000);

    // Salva ou atualiza a integração
    await supabase.from('google_calendar_configs').delete().eq('client_id', clientId);

    const { error } = await supabase.from('google_calendar_configs').insert({
      client_id: clientId,
      access_token: data.access_token,
      refresh_token: data.refresh_token,
      expiry_date: expiryDate,
      calendar_id: 'primary',
    });

    if (error) throw error;
  } catch (err: any) {
    console.error('[Google Calendar] Failed to exchange code for tokens:', err.response?.data ?? err.message);
    throw new Error('Falha na autenticação com o Google.');
  }
}

/**
 * Recupera e renova (se necessário) o access token de um cliente.
 */
async function getAccessToken(clientId: string): Promise<{ token: string; calendarId: string } | null> {
  const { data: config } = await supabase
    .from('google_calendar_configs')
    .select('*')
    .eq('client_id', clientId)
    .single();

  if (!config) return null;

  // Se for Mock, retorna as credenciais simuladas
  if (isMockGoogle || config.access_token === 'mock_access_token_123') {
    return { token: config.access_token, calendarId: config.calendar_id };
  }

  const isExpired = Date.now() > (config.expiry_date - 300000); // 5 min de margem

  if (!isExpired) {
    return { token: config.access_token, calendarId: config.calendar_id };
  }

  // Se expirou e temos refresh_token, renova
  if (config.refresh_token) {
    try {
      console.log(`[Google Calendar] Refreshing expired token for client '${clientId}'`);
      const { data } = await axios.post('https://oauth2.googleapis.com/token', {
        client_id: GOOGLE_CLIENT_ID,
        client_secret: GOOGLE_CLIENT_SECRET,
        refresh_token: config.refresh_token,
        grant_type: 'refresh_token',
      });

      const expiryDate = Date.now() + (data.expires_in * 1000);

      await supabase
        .from('google_calendar_configs')
        .update({
          access_token: data.access_token,
          expiry_date: expiryDate,
          updated_at: new Date().toISOString(),
        })
        .eq('client_id', clientId);

      return { token: data.access_token, calendarId: config.calendar_id };
    } catch (err: any) {
      console.error('[Google Calendar] Failed to refresh token:', err.response?.data ?? err.message);
      return null;
    }
  }

  return null;
}

/**
 * Verifica se um cliente tem a integração do Google Calendar ativa.
 */
export async function isGoogleCalendarConnected(clientId: string): Promise<boolean> {
  const { data } = await supabase
    .from('google_calendar_configs')
    .select('id')
    .eq('client_id', clientId)
    .single();
  return !!data;
}

/**
 * Desconecta o Google Calendar removendo a configuração do banco.
 */
export async function disconnectGoogleCalendar(clientId: string): Promise<void> {
  await supabase
    .from('google_calendar_configs')
    .delete()
    .eq('client_id', clientId);
}

/**
 * Retorna os horários livres de um cliente para uma data específica.
 * Combina o expediente comercial do cliente com os horários ocupados obtidos do Google Calendar.
 */
export async function listFreeSlots(clientId: string, dateStr: string): Promise<string[]> {
  const credentials = await getAccessToken(clientId);
  
  // Expediente padrão: Segunda a Sexta, das 08:00 às 18:00 (intervalos de 1 hora)
  // Caso de testes ou sem expediente cadastrado:
  const businessStartHour = 8;
  const businessEndHour = 18;
  const slotDurationMinutes = 60; // Duração dos slots em minutos

  // Se for Mock ou não tiver integração configurada, gera horários livres simulados usando hash da data
  if (!credentials) {
    console.log(`[Google Calendar] Client '${clientId}' is not integrated or in Mock. Simulating free slots for ${dateStr}.`);
    // Simula slots diferentes baseado no dia para parecer dinâmico
    const day = new Date(dateStr + 'T00:00:00').getDay();
    if (day === 0 || day === 6) return []; // Fechado nos finais de semana no mock
    
    // Gera slots com base no dia da semana
    const mockSlots = [];
    for (let hour = businessStartHour; hour < businessEndHour; hour++) {
      if ((hour + day) % 3 !== 0) { // Ocupa alguns horários aleatoriamente
        mockSlots.push(`${hour.toString().padStart(2, '0')}:00`);
      }
    }
    return mockSlots;
  }

  try {
    const timeMin = `${dateStr}T00:00:00Z`;
    const timeMax = `${dateStr}T23:59:59Z`;

    // Chamamos a API do Google Calendar para listar eventos ocupados naquele dia
    const response = await axios.get(
      `https://www.googleapis.com/calendar/v3/calendars/${credentials.calendarId}/events`,
      {
        headers: { Authorization: `Bearer ${credentials.token}` },
        params: {
          timeMin,
          timeMax,
          singleEvents: true,
          orderBy: 'startTime'
        }
      }
    );

    const events = response.data.items ?? [];
    const busyRanges = events.map((event: any) => {
      const start = new Date(event.start.dateTime ?? event.start.date);
      const end = new Date(event.end.dateTime ?? event.end.date);
      return { start, end };
    });

    // Gera todos os slots possíveis dentro do expediente
    const freeSlots: string[] = [];
    const dateObj = new Date(dateStr + 'T00:00:00');

    // Se final de semana, não há vagas
    if (dateObj.getDay() === 0 || dateObj.getDay() === 6) {
      return [];
    }

    for (let hour = businessStartHour; hour < businessEndHour; hour++) {
      const slotTimeStr = `${hour.toString().padStart(2, '0')}:00`;
      const slotStart = new Date(`${dateStr}T${slotTimeStr}:00`);
      const slotEnd = new Date(slotStart.getTime() + slotDurationMinutes * 60 * 1000);

      // Verifica se o slot de horário colide com algum compromisso
      const isBusy = busyRanges.some((busy: any) => {
        return (slotStart < busy.end && slotEnd > busy.start);
      });

      if (!isBusy) {
        freeSlots.push(slotTimeStr);
      }
    }

    return freeSlots;
  } catch (err: any) {
    console.error('[Google Calendar] Failed to fetch freeBusy / events:', err.response?.data ?? err.message);
    // Em caso de erro, retorna vazio
    return [];
  }
}

/**
 * Cria um agendamento no banco de dados e adiciona o evento no Google Calendar.
 */
export async function createCalendarEvent(clientId: string, details: AppointmentDetails): Promise<any> {
  const credentials = await getAccessToken(clientId);
  
  // Duração padrão de 1 hora
  const startISO = `${details.date}T${details.time}:00`;
  const startDate = new Date(startISO);
  const endDate = new Date(startDate.getTime() + 60 * 60 * 1000);
  const endISO = endDate.toISOString();

  let googleEventId: string | null = null;

  if (credentials) {
    try {
      // Chama Google Calendar API para agendar
      const response = await axios.post(
        `https://www.googleapis.com/calendar/v3/calendars/${credentials.calendarId}/events`,
        {
          summary: `Agendamento: ${details.name}`,
          description: `Serviço: ${details.service ?? 'Geral'}\nTelefone: +${details.phone}\nAgendado pelo AceleraAssistente 🤖`,
          start: {
            dateTime: startDate.toISOString(),
            timeZone: 'America/Sao_Paulo'
          },
          end: {
            dateTime: endDate.toISOString(),
            timeZone: 'America/Sao_Paulo'
          }
        },
        {
          headers: { Authorization: `Bearer ${credentials.token}` }
        }
      );
      googleEventId = response.data.id;
    } catch (err: any) {
      console.error('[Google Calendar] Failed to create event in Google:', err.response?.data ?? err.message);
      // Salva no banco de dados local de qualquer forma
    }
  } else {
    // Modo Mock
    googleEventId = 'mock_evt_' + Math.random().toString(36).substring(7);
    console.log(`[Mock Google Calendar] Created event '${googleEventId}' for ${details.name} at ${startISO}`);
  }

  // Registra no banco de dados local
  const { data: appointment, error } = await supabase
    .from('appointments')
    .insert({
      client_id: clientId,
      conversation_id: details.conversationId ?? null,
      lead_phone: details.phone,
      lead_name: details.name,
      service_name: details.service ?? 'Geral',
      start_time: startDate.toISOString(),
      end_time: endDate.toISOString(),
      google_event_id: googleEventId,
      status: 'scheduled'
    })
    .select()
    .single();

  if (error) {
    console.log('[Google Calendar] Failed to save appointment in Supabase:', error);
    throw error;
  }

  return appointment;
}

/**
 * Exclui um evento do Google Calendar e do banco de dados (cancela).
 */
export async function deleteCalendarEvent(clientId: string, googleEventId: string): Promise<void> {
  const credentials = await getAccessToken(clientId);

  if (credentials && googleEventId && !googleEventId.startsWith('mock_')) {
    try {
      await axios.delete(
        `https://www.googleapis.com/calendar/v3/calendars/${credentials.calendarId}/events/${googleEventId}`,
        {
          headers: { Authorization: `Bearer ${credentials.token}` }
        }
      );
      console.log(`[Google Calendar] Deleted event '${googleEventId}' from Google.`);
    } catch (err: any) {
      console.error('[Google Calendar] Failed to delete event in Google:', err.response?.data ?? err.message);
      // Continua para cancelar localmente mesmo se falhar no Google
    }
  } else {
    console.log(`[Mock Google Calendar] Deleted event '${googleEventId}' from Mock Calendar.`);
  }
}

