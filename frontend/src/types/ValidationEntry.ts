export interface ValidationEntry {
  id: number;
  prompt: string;
  expectedOutput: string;
  actualOutput: string;
  score: number | null;
  reasoning?: string;
  differences?: string;
  status: 'pending' | 'processing' | 'completed' | 'error';
  errorMessage?: string;
  selectedAgentId?: string;
}
