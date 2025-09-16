export interface JobCreateRequest {
  name: string;
  description?: string;
  type: JobType;
  configuration: JobConfiguration;
}

export interface JobSubmissionResponse {
  job_id: string;
  status_url: string;
}

export interface Job {
  id: string;
  name: string;
  description?: string;
  type: JobType;
  status: JobStatus;
  created_at: string;
  updated_at: string;
  completed_at?: string;
  estimated_completion?: string;
  progress: JobProgress;
  configuration: JobConfiguration;
  error_details?: JobErrorDetails;
}

export interface JobListResponse {
  jobs: Job[];
  pagination: {
    current_page: number;
    total_pages: number;
    total_items: number;
    items_per_page: number;
    has_next: boolean;
    has_previous: boolean;
  };
}

export interface JobProgress {
  total_items: number;
  completed_items: number;
  percentage: number;
}

export interface JobConfiguration {
  data_source?: string;
  data_source_blob_ref?: string;
  prompt_template?: string;
  evaluation_criteria?: EvaluationCriteria;
  agent_configuration?: AgentConfiguration;
}

export interface EvaluationCriteria {
  similarity_threshold?: number;
  use_semantic_scoring?: boolean;
}

export interface AgentConfiguration {
  selected_agent_id?: string;
  additional_instructions?: string;
  knowledge_source?: string;
}

export interface JobErrorDetails {
  code: string;
  message: string;
  details?: Record<string, any>;
}

export enum JobType {
  BulkEvaluation = 'bulk_evaluation',
  SingleEvaluation = 'single_evaluation',
  BatchProcessing = 'batch_processing'
}

export enum JobStatus {
  Pending = 'pending',
  Running = 'running',
  Completed = 'completed',
  Failed = 'failed',
  Cancelled = 'cancelled'
}

export interface JobListFilters {
  status?: JobStatus;
  type?: JobType;
  page?: number;
  limit?: number;
  sort?: string;
  order?: 'asc' | 'desc';
}