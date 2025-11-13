import axios from 'axios';
import { authService } from './authService';

const API_BASE_URL = '/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Add request interceptor to include access token in Authorization header
apiClient.interceptors.request.use((config) => {
  const token = authService.getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
}, (error) => {
  return Promise.reject(error);
});

export interface ChatRequest {
  prompt: string;
}

export interface ChatResponse {
  response: string;
  success: boolean;
  error?: string;
}

export interface SimilarityRequest {
  expected: string;
  actual: string;
  accessToken: string;
  additionalInstructions?: string;
}

export interface SimilarityResponse {
  score: number;
  success: boolean;
  error?: string;
  reasoning?: string;
  differences?: string;
}

export const apiService = {
  // Health check
  async checkHealth(): Promise<{ status: string; timestamp: string }> {
    const response = await apiClient.get('/health');
    return response.data;
  },

  // Get response from Copilot Chat API
  async getCopilotResponse(prompt: string): Promise<string> {
    const response = await apiClient.post<ChatResponse>('/copilot/chat', {
      prompt,
    });

    if (!response.data.success) {
      throw new Error(response.data.error || 'Failed to get Copilot response');
    }

    return response.data.response;
  },

  // Get similarity score between expected and actual output
  async getSimilarityScore(expected: string, actual: string, accessToken: string, additionalInstructions?: string): Promise<SimilarityResponse> {
    const response = await apiClient.post<SimilarityResponse>('/similarity/score', {
      expected,
      actual,
      accessToken,
      additionalInstructions,
    });

    if (!response.data.success) {
      throw new Error(response.data.error || 'Failed to calculate similarity score');
    }

    return response.data;
  },
};
