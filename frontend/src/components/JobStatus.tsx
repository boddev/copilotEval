import React, { useState, useEffect, useRef } from 'react';
import { Job, JobStatus as JobStatusEnum } from '../types/Job';
import { jobService } from '../services/jobService';

interface JobStatusProps {
  jobId: string;
  onClose?: () => void;
  autoRefresh?: boolean;
  refreshInterval?: number; // in milliseconds
}

export default function JobStatus({ 
  jobId, 
  onClose, 
  autoRefresh = true, 
  refreshInterval = 5000 
}: JobStatusProps) {
  const [job, setJob] = useState<Job | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [copying, setCopying] = useState(false);
  const intervalRef = useRef<NodeJS.Timeout | null>(null);

  useEffect(() => {
    fetchJobDetails();

    // Set up polling for auto-refresh
    if (autoRefresh) {
      intervalRef.current = setInterval(fetchJobDetails, refreshInterval);
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, [jobId, autoRefresh, refreshInterval]);

  // Stop polling when job reaches terminal state
  useEffect(() => {
    if (job && isTerminalStatus(job.status) && intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
  }, [job?.status]);

  const fetchJobDetails = async () => {
    try {
      const jobDetails = await jobService.getJob(jobId);
      setJob(jobDetails);
      setError(null);
    } catch (err) {
      console.error('❌ Failed to fetch job details:', err);
      setError(err instanceof Error ? err.message : 'Failed to fetch job details');
    } finally {
      setLoading(false);
    }
  };

  const isTerminalStatus = (status: JobStatusEnum): boolean => {
    return [JobStatusEnum.Completed, JobStatusEnum.Failed, JobStatusEnum.Cancelled].includes(status);
  };

  const getStatusIcon = (status: JobStatusEnum): string => {
    switch (status) {
      case JobStatusEnum.Pending:
        return 'bi-clock text-secondary';
      case JobStatusEnum.Running:
        return 'bi-play-circle text-primary';
      case JobStatusEnum.Completed:
        return 'bi-check-circle text-success';
      case JobStatusEnum.Failed:
        return 'bi-x-circle text-danger';
      case JobStatusEnum.Cancelled:
        return 'bi-stop-circle text-warning';
      default:
        return 'bi-question-circle text-muted';
    }
  };

  const getStatusText = (status: JobStatusEnum): string => {
    switch (status) {
      case JobStatusEnum.Pending:
        return 'Queued for processing';
      case JobStatusEnum.Running:
        return 'Processing in progress';
      case JobStatusEnum.Completed:
        return 'Successfully completed';
      case JobStatusEnum.Failed:
        return 'Failed to complete';
      case JobStatusEnum.Cancelled:
        return 'Cancelled by user';
      default:
        return 'Unknown status';
    }
  };

  const formatDate = (dateString: string): string => {
    return new Date(dateString).toLocaleString();
  };

  const copyStatusUrl = async () => {
    setCopying(true);
    try {
      const statusUrl = jobService.buildStatusUrl(jobId);
      await navigator.clipboard.writeText(statusUrl);
      
      // Show success feedback
      setTimeout(() => setCopying(false), 1000);
    } catch (err) {
      console.error('Failed to copy URL:', err);
      setCopying(false);
    }
  };

  const handleCancelJob = async () => {
    if (!job || isTerminalStatus(job.status)) return;

    if (confirm('Are you sure you want to cancel this job?')) {
      try {
        await jobService.cancelJob(jobId);
        await fetchJobDetails(); // Refresh to show updated status
      } catch (err) {
        console.error('Failed to cancel job:', err);
        setError('Failed to cancel job');
      }
    }
  };

  if (loading) {
    return (
      <div className="card">
        <div className="card-body text-center py-5">
          <div className="spinner-border text-primary mb-3" role="status"></div>
          <p className="text-muted">Loading job details...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="card">
        <div className="card-body">
          <div className="alert alert-danger" role="alert">
            <i className="bi bi-exclamation-triangle-fill me-2"></i>
            {error}
          </div>
          <button className="btn btn-primary" onClick={fetchJobDetails}>
            <i className="bi bi-arrow-clockwise me-2"></i>
            Retry
          </button>
        </div>
      </div>
    );
  }

  if (!job) {
    return (
      <div className="card">
        <div className="card-body text-center py-5">
          <i className="bi bi-exclamation-triangle display-1 text-muted"></i>
          <h5 className="text-muted mt-3">Job not found</h5>
          <p className="text-muted">The requested job could not be found.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="card">
      <div className="card-header">
        <div className="row align-items-center">
          <div className="col">
            <h5 className="card-title mb-0">
              <i className="bi bi-info-circle me-2"></i>
              Job Status
            </h5>
          </div>
          <div className="col-auto">
            <div className="btn-group btn-group-sm">
              <button
                className="btn btn-outline-primary"
                onClick={fetchJobDetails}
                title="Refresh"
              >
                <i className="bi bi-arrow-clockwise"></i>
              </button>
              <button
                className="btn btn-outline-secondary"
                onClick={copyStatusUrl}
                title="Copy Status URL"
                disabled={copying}
              >
                {copying ? (
                  <i className="bi bi-check text-success"></i>
                ) : (
                  <i className="bi bi-link"></i>
                )}
              </button>
              {onClose && (
                <button
                  className="btn btn-outline-secondary"
                  onClick={onClose}
                  title="Close"
                >
                  <i className="bi bi-x"></i>
                </button>
              )}
            </div>
          </div>
        </div>
      </div>

      <div className="card-body">
        {/* Job Overview */}
        <div className="row mb-4">
          <div className="col-md-8">
            <h6 className="fw-bold">{job.name}</h6>
            {job.description && (
              <p className="text-muted mb-2">{job.description}</p>
            )}
            <small className="text-muted">Job ID: {job.id}</small>
          </div>
          <div className="col-md-4 text-md-end">
            <div className="mb-2">
              <i className={`bi ${getStatusIcon(job.status)} me-2 fs-5`}></i>
              <span className="fw-bold">{job.status.toUpperCase()}</span>
            </div>
            <small className="text-muted">{getStatusText(job.status)}</small>
          </div>
        </div>

        {/* Progress Bar */}
        <div className="mb-4">
          <div className="d-flex justify-content-between mb-2">
            <span className="fw-bold">Progress</span>
            <span className="text-muted">
              {job.progress.completed_items} / {job.progress.total_items} items
            </span>
          </div>
          <div className="progress mb-2" style={{ height: '12px' }}>
            <div
              className={`progress-bar ${
                job.status === JobStatusEnum.Failed ? 'bg-danger' : 
                job.status === JobStatusEnum.Completed ? 'bg-success' : ''
              }`}
              role="progressbar"
              style={{ width: `${job.progress.percentage}%` }}
            >
              {job.progress.percentage.toFixed(1)}%
            </div>
          </div>
        </div>

        {/* Timeline */}
        <div className="mb-4">
          <h6 className="fw-bold mb-3">Timeline</h6>
          <div className="timeline">
            <div className="timeline-item completed">
              <div className="timeline-marker">
                <i className="bi bi-check-circle-fill text-success"></i>
              </div>
              <div className="timeline-content">
                <strong>Job Created</strong>
                <div className="text-muted small">{formatDate(job.created_at)}</div>
              </div>
            </div>

            {job.status !== JobStatusEnum.Pending && (
              <div className="timeline-item completed">
                <div className="timeline-marker">
                  <i className="bi bi-play-circle-fill text-primary"></i>
                </div>
                <div className="timeline-content">
                  <strong>Processing Started</strong>
                  <div className="text-muted small">{formatDate(job.updated_at)}</div>
                </div>
              </div>
            )}

            {job.completed_at && (
              <div className="timeline-item completed">
                <div className="timeline-marker">
                  <i className={`bi ${
                    job.status === JobStatusEnum.Completed ? 'bi-check-circle-fill text-success' :
                    job.status === JobStatusEnum.Failed ? 'bi-x-circle-fill text-danger' :
                    'bi-stop-circle-fill text-warning'
                  }`}></i>
                </div>
                <div className="timeline-content">
                  <strong>
                    {job.status === JobStatusEnum.Completed ? 'Completed' :
                     job.status === JobStatusEnum.Failed ? 'Failed' : 'Cancelled'}
                  </strong>
                  <div className="text-muted small">{formatDate(job.completed_at)}</div>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Configuration Details */}
        <div className="mb-4">
          <h6 className="fw-bold mb-3">Configuration</h6>
          <div className="row">
            <div className="col-md-6">
              <div className="mb-3">
                <label className="form-label fw-bold">Job Type:</label>
                <div>{job.type.replace('_', ' ')}</div>
              </div>
              {job.configuration.data_source && (
                <div className="mb-3">
                  <label className="form-label fw-bold">Data Source:</label>
                  <div>{job.configuration.data_source}</div>
                </div>
              )}
            </div>
            <div className="col-md-6">
              {job.configuration.evaluation_criteria && (
                <div className="mb-3">
                  <label className="form-label fw-bold">Similarity Threshold:</label>
                  <div>{job.configuration.evaluation_criteria.similarity_threshold}</div>
                </div>
              )}
              {job.estimated_completion && (
                <div className="mb-3">
                  <label className="form-label fw-bold">Estimated Completion:</label>
                  <div>{formatDate(job.estimated_completion)}</div>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Error Details */}
        {job.error_details && (
          <div className="mb-4">
            <h6 className="fw-bold mb-3">Error Details</h6>
            <div className="alert alert-danger">
              <strong>{job.error_details.code}:</strong> {job.error_details.message}
              {job.error_details.details && (
                <pre className="mt-2 mb-0 small">
                  {JSON.stringify(job.error_details.details, null, 2)}
                </pre>
              )}
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="d-flex gap-2">
          {!isTerminalStatus(job.status) && (
            <button
              className="btn btn-warning"
              onClick={handleCancelJob}
            >
              <i className="bi bi-stop-circle me-2"></i>
              Cancel Job
            </button>
          )}
          {job.status === JobStatusEnum.Completed && (
            <button className="btn btn-success" disabled>
              <i className="bi bi-download me-2"></i>
              Download Results
            </button>
          )}
        </div>

        {/* Auto-refresh indicator */}
        {autoRefresh && !isTerminalStatus(job.status) && (
          <div className="mt-3 text-center">
            <small className="text-muted">
              <i className="bi bi-arrow-clockwise me-1"></i>
              Auto-refreshing every {refreshInterval / 1000} seconds
            </small>
          </div>
        )}
      </div>
    </div>
  );
}