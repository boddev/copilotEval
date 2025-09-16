import axios from 'axios';
import { 
  JobCreateRequest, 
  JobSubmissionResponse, 
  Job, 
  JobListResponse, 
  JobListFilters 
} from '../types/Job';

const API_BASE_URL = '/api';

const jobApiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export interface ErrorResponse {
  error: {
    code: string;
    message: string;
    details?: Record<string, any>;
  };
}

export const jobService = {
  // Submit a new job
  async submitJob(request: JobCreateRequest): Promise<JobSubmissionResponse> {
    try {
      const response = await jobApiClient.post<JobSubmissionResponse>('/jobs', request);
      return response.data;
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.data) {
        const errorData = error.response.data as ErrorResponse;
        throw new Error(errorData.error?.message || 'Failed to submit job');
      }
      throw new Error('Failed to submit job');
    }
  },

  // Get list of jobs with optional filters
  async getJobs(filters?: JobListFilters): Promise<JobListResponse> {
    try {
      const params = new URLSearchParams();
      
      if (filters?.status) params.append('status', filters.status);
      if (filters?.type) params.append('type', filters.type);
      if (filters?.page) params.append('page', filters.page.toString());
      if (filters?.limit) params.append('limit', filters.limit.toString());
      if (filters?.sort) params.append('sort', filters.sort);
      if (filters?.order) params.append('order', filters.order);
      
      const response = await jobApiClient.get<JobListResponse>(`/jobs?${params.toString()}`);
      return response.data;
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.data) {
        const errorData = error.response.data as ErrorResponse;
        throw new Error(errorData.error?.message || 'Failed to fetch jobs');
      }
      throw new Error('Failed to fetch jobs');
    }
  },

  // Get specific job details
  async getJob(jobId: string): Promise<Job> {
    try {
      const response = await jobApiClient.get<Job>(`/jobs/${jobId}`);
      return response.data;
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.data) {
        const errorData = error.response.data as ErrorResponse;
        throw new Error(errorData.error?.message || 'Failed to fetch job details');
      }
      throw new Error('Failed to fetch job details');
    }
  },

  // Cancel a job
  async cancelJob(jobId: string): Promise<void> {
    try {
      await jobApiClient.post(`/jobs/${jobId}/cancel`);
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.data) {
        const errorData = error.response.data as ErrorResponse;
        throw new Error(errorData.error?.message || 'Failed to cancel job');
      }
      throw new Error('Failed to cancel job');
    }
  },

  // Helper function to build status URL
  buildStatusUrl(jobId: string): string {
    return `${window.location.origin}/jobs/${jobId}`;
  },
};