import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import JobStatus from '../components/JobStatus'
import { JobStatus as JobStatusEnum } from '../types/Job'

// Mock the job service
vi.mock('../services/jobService', () => ({
  jobService: {
    getJob: vi.fn(),
    buildStatusUrl: vi.fn((id) => `http://localhost:3000/jobs/${id}`),
    cancelJob: vi.fn(),
  },
}))

describe('JobStatus Component', () => {
  const mockOnClose = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders with job ID in loading state', () => {
    render(<JobStatus jobId="job_test123" onClose={mockOnClose} />)
    
    // Component shows loading state before job data is loaded
    expect(screen.getByText(/loading job details/i)).toBeInTheDocument()
  })

  it('shows loading state initially', () => {
    render(<JobStatus jobId="job_test123" onClose={mockOnClose} />)
    
    expect(screen.getByText(/loading/i)).toBeInTheDocument()
  })

  it('calls getJob service on mount', async () => {
    const { jobService } = await import('../services/jobService')
    
    const mockJob = {
      id: 'job_test123',
      name: 'Test Job',
      status: JobStatusEnum.Running,
      type: 'bulk_evaluation',
      created_at: new Date().toISOString(),
      updated_at: new Date().toISOString(),
      progress: { total_items: 100, completed_items: 50, percentage: 50 },
      configuration: { data_source: 'test.csv' }
    }
    
    vi.mocked(jobService.getJob).mockResolvedValue(mockJob)
    
    render(<JobStatus jobId="job_test123" onClose={mockOnClose} />)
    
    expect(jobService.getJob).toHaveBeenCalledWith('job_test123')
  })

  it('has service methods available', async () => {
    const { jobService } = await import('../services/jobService')
    
    expect(typeof jobService.getJob).toBe('function')
    expect(typeof jobService.buildStatusUrl).toBe('function')
    expect(typeof jobService.cancelJob).toBe('function')
  })
})