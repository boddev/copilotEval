import axios from 'axios';

const API_BASE_URL = '/api';

export interface AuthUrlResponse {
  authUrl: string;
  state: string;
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  refreshToken?: string;
}

export interface CopilotChatRequest {
  prompt: string;
  conversationId?: string;
  accessToken: string;
  timeZone?: string;
  selectedAgentId?: string;
  additionalInstructions?: string;
  selectedKnowledgeSource?: string;
}

export interface CopilotChatResponse {
  response: string;
  success: boolean;
  error?: string;
  conversationId?: string;
  attributions?: Attribution[];
}

export interface Attribution {
  attributionType: string;
  providerDisplayName: string;
  attributionSource: string;
  seeMoreWebUrl: string;
  imageWebUrl?: string;
  imageFavIcon?: string;
  imageWidth: number;
  imageHeight: number;
}

export interface SimilarityRequest {
  expected: string;
  actual: string;
  accessToken: string;
  additionalInstructions?: string;
  selectedKnowledgeSource?: string;
}

export interface SimilarityResponse {
  score: number;
  success: boolean;
  error?: string;
  reasoning?: string;
  differences?: string;
}

export interface ExternalConnection {
  id: string;
  name: string;
  description?: string;
  state?: string;
  configuration?: {
    authorizedAppIds?: string[];
  };
}

export interface ExternalConnectionsResponse {
  value: ExternalConnection[];
}

export interface TeamsApp {
  id: string;
  externalId?: string;
  displayName: string;
  distributionMethod?: string;
  shortDescription?: string;
  description?: string;
  version?: string;
  packageName?: string;
  isBlocked?: boolean;
  publishingState?: string;
  teamsAppDefinition?: {
    id?: string;
    teamsAppId?: string;
    displayName?: string;
    version?: string;
    publishingState?: string;
    shortDescription?: string;
    description?: string;
    lastModifiedDateTime?: string;
    createdBy?: {
      application?: {
        id?: string;
        displayName?: string;
      };
      user?: {
        id?: string;
        displayName?: string;
        userIdentityType?: string;
      };
    };
    bot?: {
      id?: string;
    };
  };
}

export interface TeamsAppsResponse {
  value: TeamsApp[];
}

class AuthService {
  private accessToken: string | null = null;
  private tokenExpiry: Date | null = null;

  constructor() {
    // Try to load token from localStorage
    this.loadTokenFromStorage();
  }

  private loadTokenFromStorage() {
    const token = localStorage.getItem('copilot_access_token');
    const expiry = localStorage.getItem('copilot_token_expiry');
    
    if (token && expiry) {
      const expiryDate = new Date(expiry);
      if (expiryDate > new Date()) {
        this.accessToken = token;
        this.tokenExpiry = expiryDate;
      } else {
        this.clearToken();
      }
    }
  }

  private saveTokenToStorage(token: string, expiresIn: number) {
    const expiryDate = new Date(Date.now() + (expiresIn * 1000));
    localStorage.setItem('copilot_access_token', token);
    localStorage.setItem('copilot_token_expiry', expiryDate.toISOString());
    this.accessToken = token;
    this.tokenExpiry = expiryDate;
  }

  private clearToken() {
    localStorage.removeItem('copilot_access_token');
    localStorage.removeItem('copilot_token_expiry');
    this.accessToken = null;
    this.tokenExpiry = null;
  }

  public isAuthenticated(): boolean {
    return this.accessToken !== null && this.tokenExpiry !== null && this.tokenExpiry > new Date();
  }

  public getAccessToken(): string | null {
    return this.isAuthenticated() ? this.accessToken : null;
  }

  public async getAuthUrl(redirectUri?: string): Promise<AuthUrlResponse> {
    const params = redirectUri ? `?redirectUri=${encodeURIComponent(redirectUri)}` : '';
    const response = await axios.get<AuthUrlResponse>(`${API_BASE_URL}/auth/url${params}`);
    return response.data;
  }

  public async exchangeCodeForToken(code: string, state: string, redirectUri?: string): Promise<void> {
    const response = await axios.post<TokenResponse>(`${API_BASE_URL}/auth/token`, {
      code,
      state,
      redirectUri
    });

    this.saveTokenToStorage(response.data.accessToken, response.data.expiresIn);
  }

  public logout(): void {
    this.clearToken();
  }

  public async chatWithCopilot(request: Omit<CopilotChatRequest, 'accessToken'>): Promise<CopilotChatResponse> {
    console.log('🔐 Checking authentication...');
    const accessToken = this.getAccessToken();
    
    if (!accessToken) {
      console.error('❌ No access token available');
      throw new Error('Not authenticated. Please log in first.');
    }
    
    console.log('✅ Access token found, length:', accessToken.length);

    const chatRequest: CopilotChatRequest = {
      ...request,
      accessToken,
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone
    };

    console.log('📤 Sending chat request to backend:', {
      prompt: request.prompt.substring(0, 50) + '...',
      conversationId: request.conversationId,
      hasAccessToken: !!chatRequest.accessToken,
      timeZone: chatRequest.timeZone
    });

    try {
      const response = await axios.post<CopilotChatResponse>(`${API_BASE_URL}/copilot/chat`, chatRequest);
      console.log('📥 Backend response:', response.data);
      return response.data;
    } catch (error) {
      console.error('❌ Chat request failed:', error);
      if (axios.isAxiosError(error)) {
        console.error('❌ Response status:', error.response?.status);
        console.error('❌ Response data:', error.response?.data);
      }
      throw error;
    }
  }

  public async calculateSimilarity(request: Omit<SimilarityRequest, 'accessToken'>): Promise<SimilarityResponse> {
    const accessToken = this.getAccessToken();
    if (!accessToken) {
      throw new Error('No access token available. Please authenticate first.');
    }

    const fullRequest: SimilarityRequest = {
      ...request,
      accessToken
    };

    console.log('📊 Sending similarity request:', {
      expected: request.expected?.substring(0, 100) + '...',
      actual: request.actual?.substring(0, 100) + '...',
      hasAccessToken: !!accessToken
    });

    const response = await axios.post<SimilarityResponse>(`${API_BASE_URL}/similarity/score`, fullRequest);
    
    console.log('📈 Similarity response:', {
      score: response.data.score,
      success: response.data.success,
      reasoning: response.data.reasoning?.substring(0, 100) + '...',
      error: response.data.error
    });

    return response.data;
  }

  public async getInstalledAgents(): Promise<TeamsAppsResponse> {
    console.log('🤖 Getting installed Copilot agents...');
    const accessToken = this.getAccessToken();
    
    if (!accessToken) {
      console.error('❌ No access token available');
      throw new Error('Not authenticated. Please log in first.');
    }
    
    console.log('✅ Access token found for agents query');

    try {
      const response = await axios.get<TeamsAppsResponse>(`${API_BASE_URL}/copilot/agents`, {
        params: { accessToken }
      });
      
      console.log('📱 Agents response:', {
        agentCount: response.data.value?.length || 0,
        agents: response.data.value?.slice(0, 3).map(app => ({
          name: app.displayName,
          id: app.id,
          distribution: app.distributionMethod
        }))
      });
      
      return response.data;
    } catch (error) {
      console.error('❌ Failed to get installed agents:', error);
      if (axios.isAxiosError(error)) {
        console.error('❌ Response status:', error.response?.status);
        console.error('❌ Response data:', error.response?.data);
      }
      throw error;
    }
  }

  public async getKnowledgeSources(): Promise<ExternalConnectionsResponse> {
    console.log('🗃️ Getting external knowledge sources...');
    const accessToken = this.getAccessToken();
    
    if (!accessToken) {
      console.error('❌ No access token available');
      throw new Error('Not authenticated. Please log in first.');
    }
    
    console.log('✅ Access token found for knowledge sources query');

    try {
      const response = await axios.get<ExternalConnectionsResponse>(`${API_BASE_URL}/copilot/knowledge-sources`, {
        params: { accessToken }
      });
      
      console.log('🗃️ Knowledge sources response:', {
        connectionCount: response.data.value?.length || 0,
        connections: response.data.value?.slice(0, 3).map(conn => ({
          name: conn.name,
          id: conn.id,
          description: conn.description
        }))
      });
      
      return response.data;
    } catch (error) {
      console.error('❌ Failed to get knowledge sources:', error);
      if (axios.isAxiosError(error)) {
        console.error('❌ Response status:', error.response?.status);
        console.error('❌ Response data:', error.response?.data);
      }
      throw error;
    }
  }
}

export const authService = new AuthService();
