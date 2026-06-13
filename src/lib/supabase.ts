import { createClient } from '@supabase/supabase-js';

const supabaseUrl = process.env.SUPABASE_URL!;
const supabaseServiceKey = process.env.SUPABASE_SERVICE_KEY!;

const isMock = !supabaseUrl || 
               supabaseUrl.includes('xxxxxxxxxxxx') || 
               process.env.MOCK_MODE === 'true';

let supabaseClient: any;

if (isMock) {
  console.log('⚠️ Running Supabase in Mock Mode (using in-memory data)');
  
  // In-memory tables
  const db: Record<string, any[]> = {
    clients: [
      {
        id: 'demo-client-id',
        user_id: 'demo-user-id',
        business_name: 'Oficina Demo',
        whatsapp_number: '5511999999999',
        notification_number: '5511999999999',
        instance_name: 'acelera_demo',
        plan: 'motor',
        status: 'trial',
        ai_personality: 'friendly',
        welcome_message: 'Olá! Bem-vindo à Oficina Demo. Como podemos te ajudar hoje?',
        created_at: new Date().toISOString(),
        updated_at: new Date().toISOString()
      }
    ],
    knowledge_base: [
      {
        id: 'kb-1',
        client_id: 'demo-client-id',
        category: 'services',
        question: 'Quais serviços vocês oferecem?',
        answer: 'Oferecemos alinhamento, balanceamento, troca de óleo, revisão geral e consertos mecânicos em geral.',
        is_active: true,
        created_at: new Date().toISOString()
      },
      {
        id: 'kb-2',
        client_id: 'demo-client-id',
        category: 'hours',
        question: 'Qual o horário de funcionamento?',
        answer: 'Funcionamos de segunda a sexta, das 8h às 18h, e aos sábados das 8h às 12h.',
        is_active: true,
        created_at: new Date().toISOString()
      }
    ],
    conversations: [
      {
        id: 'conv-1',
        client_id: 'demo-client-id',
        lead_phone: '5511988888888',
        lead_name: 'Carlos Silva',
        status: 'qualified',
        lead_score: 85,
        created_at: new Date(Date.now() - 3600000).toISOString(),
        last_message_at: new Date(Date.now() - 600000).toISOString()
      },
      {
        id: 'conv-2',
        client_id: 'demo-client-id',
        lead_phone: '5511977777777',
        lead_name: 'Ana Souza',
        status: 'active',
        lead_score: 45,
        created_at: new Date(Date.now() - 7200000).toISOString(),
        last_message_at: new Date(Date.now() - 1800000).toISOString()
      }
    ],
    leads: [
      {
        id: 'lead-1',
        conversation_id: 'conv-1',
        client_id: 'demo-client-id',
        lead_phone: '5511988888888',
        lead_name: 'Carlos Silva',
        service_interest: 'Troca de óleo e revisão',
        urgency: 'Urgente (esta semana)',
        details: 'Precisa trocar óleo de um Honda Civic 2020.',
        notified_at: new Date(Date.now() - 600000).toISOString(),
        created_at: new Date(Date.now() - 600000).toISOString()
      }
    ],
    messages: [
      {
        id: 'msg-1',
        conversation_id: 'conv-1',
        role: 'user',
        content: 'Olá, gostaria de saber o preço da troca de óleo',
        created_at: new Date(Date.now() - 3600000).toISOString()
      },
      {
        id: 'msg-2',
        conversation_id: 'conv-1',
        role: 'assistant',
        content: 'Olá! A troca de óleo com filtro para a maioria dos veículos de passeio fica em torno de R$ 180 a R$ 250, dependendo do tipo de óleo recomendado (mineral, semi-sintético ou sintético). Qual o modelo e ano do seu carro?',
        created_at: new Date(Date.now() - 3500000).toISOString()
      },
      {
        id: 'msg-3',
        conversation_id: 'conv-1',
        role: 'user',
        content: 'É um Honda Civic 2020. Preciso fazer isso urgente essa semana ainda.',
        created_at: new Date(Date.now() - 600000).toISOString()
      }
    ],
    google_calendar_configs: [],
    appointments: []
  };

  // Helper builder class to emulate Supabase queries
  class MockQueryBuilder {
    private table: string;
    private filters: Array<(item: any) => boolean> = [];
    private orderField?: string;
    private orderAscending: boolean = true;
    private limitVal?: number;
    private rangeStart?: number;
    private rangeEnd?: number;

    constructor(table: string) {
      this.table = table;
    }

    select(fields?: string, options?: { count?: string; head?: boolean }) {
      return this;
    }

    eq(field: string, value: any) {
      this.filters.push((item) => item[field] === value);
      return this;
    }

    gte(field: string, value: any) {
      this.filters.push((item) => new Date(item[field]) >= new Date(value));
      return this;
    }

    order(field: string, options?: { ascending?: boolean }) {
      this.orderField = field;
      this.orderAscending = options?.ascending ?? true;
      return this;
    }

    limit(val: number) {
      this.limitVal = val;
      return this;
    }

    range(start: number, end: number) {
      this.rangeStart = start;
      this.rangeEnd = end;
      return this;
    }

    private getItems() {
      let items = db[this.table] || [];
      // Apply filters
      for (const filter of this.filters) {
        items = items.filter(filter);
      }
      // Apply order
      if (this.orderField) {
        items = [...items].sort((a, b) => {
          const valA = a[this.orderField!];
          const valB = b[this.orderField!];
          if (valA < valB) return this.orderAscending ? -1 : 1;
          if (valA > valB) return this.orderAscending ? 1 : -1;
          return 0;
        });
      }
      
      const count = items.length;

      // Apply range / limit
      if (this.rangeStart !== undefined && this.rangeEnd !== undefined) {
        items = items.slice(this.rangeStart, this.rangeEnd + 1);
      } else if (this.limitVal !== undefined) {
        items = items.slice(0, this.limitVal);
      }

      // Relation mapping for leads -> conversations
      if (this.table === 'leads') {
        items = items.map(lead => {
          const conv = db.conversations.find(c => c.id === lead.conversation_id);
          return {
            ...lead,
            conversations: conv ? {
              lead_name: conv.lead_name,
              lead_phone: conv.lead_phone,
              lead_score: conv.lead_score
            } : null
          };
        });
      }

      // Relation mapping for appointments -> conversations
      if (this.table === 'appointments') {
        items = items.map(appt => {
          const conv = db.conversations.find(c => c.id === appt.conversation_id);
          return {
            ...appt,
            conversations: conv ? {
              lead_name: conv.lead_name,
              lead_phone: conv.lead_phone,
              lead_score: conv.lead_score
            } : null
          };
        });
      }

      return { data: items, count };
    }

    then(onfulfilled?: (value: any) => any, onrejected?: (reason: any) => any) {
      const res = this.getItems();
      return Promise.resolve({ data: res.data, error: null, count: res.count }).then(onfulfilled, onrejected);
    }

    async single() {
      const res = this.getItems();
      return { data: res.data[0] || null, error: res.data[0] ? null : null }; // Return null error on success, avoid throwing
    }

    insert(payload: any) {
      const items = Array.isArray(payload) ? payload : [payload];
      const inserted: any[] = [];
      for (const item of items) {
        const row = {
          id: Math.random().toString(36).substring(7),
          created_at: new Date().toISOString(),
          ...item
        };
        db[this.table] = db[this.table] || [];
        db[this.table].push(row);
        inserted.push(row);
      }
      
      const resultData = Array.isArray(payload) ? inserted : inserted[0];

      return {
        data: resultData,
        error: null,
        select: () => ({
          single: async () => ({ data: inserted[0], error: null })
        }),
        then(onfulfilled?: (value: any) => any) {
          return Promise.resolve({ data: resultData, error: null }).then(onfulfilled);
        }
      } as any;
    }

    update(payload: any) {
      const { data: matching } = this.getItems();
      for (const match of matching) {
        Object.assign(match, payload);
      }
      const updatedRow = matching[0] || null;
      return {
        data: updatedRow,
        error: null,
        select: () => ({
          single: async () => ({ data: updatedRow, error: null })
        }),
        then(onfulfilled?: (value: any) => any) {
          return Promise.resolve({ data: updatedRow, error: null }).then(onfulfilled);
        }
      } as any;
    }

    delete() {
      const { data: matching } = this.getItems();
      const idsToDelete = new Set(matching.map(m => m.id));
      db[this.table] = (db[this.table] || []).filter(item => !idsToDelete.has(item.id));
      return {
        data: null,
        error: null,
        then(onfulfilled?: (value: any) => any) {
          return Promise.resolve({ data: null, error: null }).then(onfulfilled);
        }
      } as any;
    }
  }

  supabaseClient = {
    auth: {
      autoRefreshToken: false,
      persistSession: false,
      getUser: async () => ({ data: { user: { id: 'demo-user-id', email: 'admin@teste.com' } }, error: null })
    },
    from: (table: string) => new MockQueryBuilder(table)
  };
} else {
  if (!supabaseUrl || !supabaseServiceKey) {
    throw new Error('Missing Supabase environment variables: SUPABASE_URL and SUPABASE_SERVICE_KEY are required.');
  }

  supabaseClient = createClient(supabaseUrl, supabaseServiceKey, {
    auth: {
      autoRefreshToken: false,
      persistSession: false,
    },
  });
}

export const supabase = supabaseClient;
