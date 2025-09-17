import { describe, it, expect, beforeEach, vi } from 'vitest'
import { jobService } from '../services/jobService'
import { JobType } from '../types/Job'

// Mock axios
vi.mock('axios', () => ({
  default: {
    create: vi.fn(() => ({
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    })),
    isAxiosError: vi.fn(),
  },
}))

// Mock window.location
Object.defineProperty(window, 'location', {
  value: {
    origin: 'http://localhost:3000'
  },
  writable: true
})

describe('JobService', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should build status URL correctly', () => {
    const jobId = 'job_test123'
    const statusUrl = jobService.buildStatusUrl(jobId)
    
    expect(statusUrl).toBe(`http://localhost:3000/jobs/${jobId}`)
  })

  it('should handle job types correctly', () => {
    expect(JobType.BulkEvaluation).toBe('bulk_evaluation')
    expect(JobType.SingleEvaluation).toBe('single_evaluation')
    expect(JobType.BatchProcessing).toBe('batch_processing')
  })

  it('should have required service methods', () => {
    expect(typeof jobService.submitJob).toBe('function')
    expect(typeof jobService.getJobs).toBe('function')
    expect(typeof jobService.getJob).toBe('function')
    expect(typeof jobService.buildStatusUrl).toBe('function')
  })
})