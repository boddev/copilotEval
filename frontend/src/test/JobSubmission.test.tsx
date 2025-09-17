import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import JobSubmission from '../components/JobSubmission'
import { JobType } from '../types/Job'

// Mock the job service
vi.mock('../services/jobService', () => ({
  jobService: {
    submitJob: vi.fn(),
  },
}))

describe('JobSubmission Component', () => {
  const mockOnJobSubmitted = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the form with required fields', () => {
    render(<JobSubmission onJobSubmitted={mockOnJobSubmitted} />)
    
    expect(screen.getByLabelText(/job name/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/data source/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/prompt template/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /submit job/i })).toBeInTheDocument()
  })

  it('shows validation errors for empty required fields', async () => {
    const user = userEvent.setup()
    render(<JobSubmission onJobSubmitted={mockOnJobSubmitted} />)
    
    const submitButton = screen.getByRole('button', { name: /submit job/i })
    await user.click(submitButton)
    
    expect(screen.getByText(/job name is required/i)).toBeInTheDocument()
    expect(screen.getByText(/data source is required/i)).toBeInTheDocument()
    expect(screen.getByText(/prompt template is required/i)).toBeInTheDocument()
  })

  it('allows user to enter job name', async () => {
    const user = userEvent.setup()
    render(<JobSubmission onJobSubmitted={mockOnJobSubmitted} />)
    
    const jobNameInput = screen.getByLabelText(/job name/i)
    await user.type(jobNameInput, 'Test Job')
    
    expect(jobNameInput).toHaveValue('Test Job')
  })

  it('has job type selection', () => {
    render(<JobSubmission onJobSubmitted={mockOnJobSubmitted} />)
    
    const jobTypeSelect = screen.getByLabelText(/job type/i)
    expect(jobTypeSelect).toBeInTheDocument()
    expect(screen.getByText('Bulk Evaluation')).toBeInTheDocument()
    expect(screen.getByText('Single Evaluation')).toBeInTheDocument()
    expect(screen.getByText('Batch Processing')).toBeInTheDocument()
  })

  it('component is properly structured', () => {
    render(<JobSubmission onJobSubmitted={mockOnJobSubmitted} />)
    
    // Verify form structure
    expect(screen.getByText(/submit new job/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/job name/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/job type/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /submit job/i })).toBeInTheDocument()
  })
})