-- ═══════════════════════════════════════════════════════════════════
-- AceleraBot — Schema Completo do Banco de Dados (Supabase PostgreSQL)
-- Execute no SQL Editor do Supabase: https://app.supabase.com
-- ═══════════════════════════════════════════════════════════════════

-- ─── Extensões ────────────────────────────────────────────────────────────────
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ─── Tabela: clients ──────────────────────────────────────────────────────────
-- Donos de negócio que usam o AceleraBot
CREATE TABLE IF NOT EXISTS public.clients (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id            UUID REFERENCES auth.users(id) ON DELETE CASCADE,
  business_name      TEXT NOT NULL,
  whatsapp_number    TEXT NOT NULL,
  notification_number TEXT, -- Número alternativo para receber alertas de lead
  instance_name      TEXT UNIQUE, -- Nome da instância na Evolution API
  plan               TEXT NOT NULL DEFAULT 'motor'
                     CHECK (plan IN ('motor', 'ecosystem')),
  status             TEXT NOT NULL DEFAULT 'trial'
                     CHECK (status IN ('active', 'inactive', 'trial')),
  ai_personality     TEXT DEFAULT 'friendly', -- 'friendly' | 'formal' | 'casual'
  welcome_message    TEXT, -- Mensagem de boas-vindas personalizada
  business_hours     JSONB, -- { mon: { open: '08:00', close: '18:00' }, ... }
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─── Tabela: knowledge_base ───────────────────────────────────────────────────
-- Base de conhecimento do negócio que alimenta a IA
CREATE TABLE IF NOT EXISTS public.knowledge_base (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  client_id   UUID NOT NULL REFERENCES public.clients(id) ON DELETE CASCADE,
  category    TEXT NOT NULL DEFAULT 'faq'
              CHECK (category IN ('services', 'hours', 'pricing', 'faq', 'about', 'custom')),
  question    TEXT, -- Pergunta associada (opcional)
  answer      TEXT NOT NULL,
  is_active   BOOLEAN NOT NULL DEFAULT TRUE,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─── Tabela: conversations ────────────────────────────────────────────────────
-- Conversas entre o bot e os clientes finais (leads)
CREATE TABLE IF NOT EXISTS public.conversations (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  client_id       UUID NOT NULL REFERENCES public.clients(id) ON DELETE CASCADE,
  lead_phone      TEXT NOT NULL,
  lead_name       TEXT,
  status          TEXT NOT NULL DEFAULT 'active'
                  CHECK (status IN ('active', 'qualified', 'closed', 'human_takeover')),
  lead_score      INTEGER NOT NULL DEFAULT 0 CHECK (lead_score BETWEEN 0 AND 100),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Índice para busca rápida por telefone
CREATE INDEX IF NOT EXISTS idx_conversations_lead_phone ON public.conversations(client_id, lead_phone, status);

-- ─── Tabela: messages ─────────────────────────────────────────────────────────
-- Histórico de mensagens de cada conversa
CREATE TABLE IF NOT EXISTS public.messages (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  conversation_id UUID NOT NULL REFERENCES public.conversations(id) ON DELETE CASCADE,
  role            TEXT NOT NULL CHECK (role IN ('user', 'assistant', 'system')),
  content         TEXT NOT NULL,
  media_url       TEXT, -- URL de áudio/imagem no Supabase Storage
  media_type      TEXT, -- 'audio' | 'image' | 'document'
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_messages_conversation ON public.messages(conversation_id, created_at);

-- ─── Tabela: leads ────────────────────────────────────────────────────────────
-- Leads qualificados pela IA (para o dono do negócio)
CREATE TABLE IF NOT EXISTS public.leads (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  conversation_id  UUID NOT NULL REFERENCES public.conversations(id) ON DELETE CASCADE,
  client_id        UUID NOT NULL REFERENCES public.clients(id) ON DELETE CASCADE,
  lead_phone       TEXT NOT NULL,
  lead_name        TEXT,
  service_interest TEXT,
  urgency          TEXT,
  details          TEXT,
  notified_at      TIMESTAMPTZ,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_leads_client ON public.leads(client_id, created_at DESC);

-- ─── Row Level Security (RLS) ─────────────────────────────────────────────────

ALTER TABLE public.clients       ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.knowledge_base ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.conversations  ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.messages       ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.leads          ENABLE ROW LEVEL SECURITY;

-- Política: cada usuário só acessa seu próprio cliente
CREATE POLICY "Users manage own client"
  ON public.clients FOR ALL
  USING (user_id = auth.uid())
  WITH CHECK (user_id = auth.uid());

-- Política: cliente acessa seus dados via client_id
CREATE POLICY "Client accesses own knowledge"
  ON public.knowledge_base FOR ALL
  USING (client_id IN (SELECT id FROM public.clients WHERE user_id = auth.uid()));

CREATE POLICY "Client accesses own conversations"
  ON public.conversations FOR ALL
  USING (client_id IN (SELECT id FROM public.clients WHERE user_id = auth.uid()));

CREATE POLICY "Client accesses own messages via conversation"
  ON public.messages FOR ALL
  USING (conversation_id IN (
    SELECT c.id FROM public.conversations c
    JOIN public.clients cl ON cl.id = c.client_id
    WHERE cl.user_id = auth.uid()
  ));

CREATE POLICY "Client accesses own leads"
  ON public.leads FOR ALL
  USING (client_id IN (SELECT id FROM public.clients WHERE user_id = auth.uid()));

-- ─── Trigger: updated_at ──────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_clients_updated_at
  BEFORE UPDATE ON public.clients
  FOR EACH ROW EXECUTE FUNCTION update_updated_at();
