import { GoogleGenerativeAI } from '@google/generative-ai';

const apiKey = process.env.GEMINI_API_KEY!;

if (!apiKey) {
  throw new Error('Missing GEMINI_API_KEY environment variable.');
}

export const genAI = new GoogleGenerativeAI(apiKey);

/**
 * Modelo padrão: gemini-1.5-flash
 * - Gratuito: 1.500.000 tokens/dia, 15 req/min no free tier
 * - Ideal para uso em produção inicial (SaaS pequeno)
 */
export const defaultModel = genAI.getGenerativeModel({
  model: 'gemini-1.5-flash',
  generationConfig: {
    temperature: 0.7,
    topK: 40,
    topP: 0.95,
    maxOutputTokens: 1024,
  },
});
