import React, { useState, useEffect } from 'react';
import { Job, JobStatus, JobType, JobListFilters } from '../types/Job';
import { jobService } from '../services/jobService';

interface JobListProps {
  onJobSelect?: (job: Job) => void;
  refreshTrigger?: number;
}

export default function JobList({ onJobSelect, refreshTrigger }: JobListProps) {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFilters] = useState<JobListFilters>({
    page: 1,
    limit: 20,
    sort: 'created_at',
    order: 'desc'
  });
  const [pagination, setPagination] = useState({
    current_page: 1,
    total_pages: 1,
    total_items: 0,
    items_per_page: 20,
    has_next: false,
    has_previous: false
  });

  useEffect(() => {
    fetchJobs();
  }, [filters, refreshTrigger]);

  const fetchJobs = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await jobService.getJobs(filters);
      setJobs(response.jobs);
      setPagination(response.pagination);
    } catch (err) {
      console.error('❌ Failed to fetch jobs:', err);
      setError(err instanceof Error ? err.message : 'Failed to fetch jobs');
    } finally {
      setLoading(false);
    }
  };

  const handleFilterChange = (key: keyof JobListFilters, value: any) => {
    setFilters(prev => ({
      ...prev,
      [key]: value,
      page: key !== 'page' ? 1 : value // Reset to page 1 when changing filters
    }));
  };

  const getStatusBadgeClass = (status: JobStatus): string => {
    switch (status) {
      case JobStatus.Pending:
        return 'bg-secondary';
      case JobStatus.Running:
        return 'bg-primary';
      case JobStatus.Completed:
        return 'bg-success';
      case JobStatus.Failed:
        return 'bg-danger';
      case JobStatus.Cancelled:
        return 'bg-warning text-dark';
      default:
        return 'bg-secondary';
    }
  };

  const getStatusIcon = (status: JobStatus): string => {
    switch (status) {
      case JobStatus.Pending:
        return 'bi-clock';
      case JobStatus.Running:
        return 'bi-play-circle';
      case JobStatus.Completed:
        return 'bi-check-circle';
      case JobStatus.Failed:
        return 'bi-x-circle';
      case JobStatus.Cancelled:
        return 'bi-stop-circle';
      default:
        return 'bi-question-circle';
    }
  };

  const formatDate = (dateString: string): string => {
    return new Date(dateString).toLocaleString();
  };

  const formatDuration = (startDate: string, endDate?: string): string => {
    const start = new Date(startDate);
    const end = endDate ? new Date(endDate) : new Date();
    const diffMs = end.getTime() - start.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffSecs = Math.floor((diffMs % 60000) / 1000);
    
    if (diffMins > 0) {
      return `${diffMins}m ${diffSecs}s`;
    }
    return `${diffSecs}s`;
  };

  if (loading && jobs.length === 0) {
    return (
      <div className="card">
        <div className="card-body text-center py-5">
          <div className="spinner-border text-primary mb-3" role="status"></div>
          <p className="text-muted">Loading jobs...</p>
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
              <i className="bi bi-list-ul me-2"></i>
              Jobs
              <span className="badge bg-secondary ms-2">{pagination.total_items}</span>
            </h5>
          </div>
          <div className="col-auto">
            <button
              className="btn btn-outline-primary btn-sm"
              onClick={fetchJobs}
              disabled={loading}
            >
              {loading ? (
                <span className="spinner-border spinner-border-sm me-1"></span>
              ) : (
                <i className="bi bi-arrow-clockwise me-1"></i>
              )}
              Refresh
            </button>
          </div>
        </div>
      </div>

      {/* Filters */}
      <div className="card-body border-bottom">
        <div className="row g-3">
          <div className="col-md-3">
            <label className="form-label">Status</label>
            <select
              className="form-select form-select-sm"
              value={filters.status || ''}
              onChange={(e) => handleFilterChange('status', e.target.value || undefined)}
            >
              <option value="">All Statuses</option>
              <option value={JobStatus.Pending}>Pending</option>
              <option value={JobStatus.Running}>Running</option>
              <option value={JobStatus.Completed}>Completed</option>
              <option value={JobStatus.Failed}>Failed</option>
              <option value={JobStatus.Cancelled}>Cancelled</option>
            </select>
          </div>
          <div className="col-md-3">
            <label className="form-label">Type</label>
            <select
              className="form-select form-select-sm"
              value={filters.type || ''}
              onChange={(e) => handleFilterChange('type', e.target.value || undefined)}
            >
              <option value="">All Types</option>
              <option value={JobType.BulkEvaluation}>Bulk Evaluation</option>
              <option value={JobType.SingleEvaluation}>Single Evaluation</option>
              <option value={JobType.BatchProcessing}>Batch Processing</option>
            </select>
          </div>
          <div className="col-md-3">
            <label className="form-label">Sort By</label>
            <select
              className="form-select form-select-sm"
              value={filters.sort || 'created_at'}
              onChange={(e) => handleFilterChange('sort', e.target.value)}
            >
              <option value="created_at">Created Date</option>
              <option value="updated_at">Updated Date</option>
              <option value="name">Name</option>
            </select>
          </div>
          <div className="col-md-3">
            <label className="form-label">Order</label>
            <select
              className="form-select form-select-sm"
              value={filters.order || 'desc'}
              onChange={(e) => handleFilterChange('order', e.target.value as 'asc' | 'desc')}
            >
              <option value="desc">Newest First</option>
              <option value="asc">Oldest First</option>
            </select>
          </div>
        </div>
      </div>

      <div className="card-body p-0">
        {error && (
          <div className="alert alert-danger m-3" role="alert">
            <i className="bi bi-exclamation-triangle-fill me-2"></i>
            {error}
          </div>
        )}

        {jobs.length === 0 && !loading ? (
          <div className="text-center py-5">
            <i className="bi bi-inbox display-1 text-muted"></i>
            <h5 className="text-muted mt-3">No jobs found</h5>
            <p className="text-muted">Try adjusting your filters or submit a new job.</p>
          </div>
        ) : (
          <div className="table-responsive">
            <table className="table table-hover mb-0">
              <thead className="table-light">
                <tr>
                  <th>Name</th>
                  <th>Type</th>
                  <th>Status</th>
                  <th>Progress</th>
                  <th>Created</th>
                  <th>Duration</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {jobs.map((job) => (
                  <tr key={job.id} className="cursor-pointer">
                    <td>
                      <div>
                        <strong>{job.name}</strong>
                        {job.description && (
                          <small className="d-block text-muted">{job.description}</small>
                        )}
                      </div>
                    </td>
                    <td>
                      <span className="badge bg-light text-dark">
                        {job.type.replace('_', ' ')}
                      </span>
                    </td>
                    <td>
                      <span className={`badge ${getStatusBadgeClass(job.status)}`}>
                        <i className={`bi ${getStatusIcon(job.status)} me-1`}></i>
                        {job.status}
                      </span>
                    </td>
                    <td>
                      <div className="d-flex align-items-center">
                        <div className="progress me-2" style={{ width: '60px', height: '8px' }}>
                          <div
                            className="progress-bar"
                            role="progressbar"
                            style={{ width: `${job.progress.percentage}%` }}
                          ></div>
                        </div>
                        <small className="text-muted">
                          {job.progress.completed_items}/{job.progress.total_items}
                        </small>
                      </div>
                    </td>
                    <td>
                      <small className="text-muted">
                        {formatDate(job.created_at)}
                      </small>
                    </td>
                    <td>
                      <small className="text-muted">
                        {formatDuration(job.created_at, job.completed_at)}
                      </small>
                    </td>
                    <td>
                      <div className="btn-group btn-group-sm">
                        <button
                          className="btn btn-outline-primary"
                          onClick={() => onJobSelect?.(job)}
                          title="View Details"
                        >
                          <i className="bi bi-eye"></i>
                        </button>
                        <button
                          className="btn btn-outline-secondary"
                          onClick={() => {
                            const statusUrl = jobService.buildStatusUrl(job.id);
                            navigator.clipboard.writeText(statusUrl);
                          }}
                          title="Copy Status URL"
                        >
                          <i className="bi bi-link"></i>
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {pagination.total_pages > 1 && (
          <div className="d-flex justify-content-between align-items-center p-3 border-top">
            <small className="text-muted">
              Showing {Math.min(pagination.items_per_page * (pagination.current_page - 1) + 1, pagination.total_items)} to{' '}
              {Math.min(pagination.items_per_page * pagination.current_page, pagination.total_items)} of{' '}
              {pagination.total_items} jobs
            </small>
            <nav>
              <ul className="pagination pagination-sm mb-0">
                <li className={`page-item ${!pagination.has_previous ? 'disabled' : ''}`}>
                  <button
                    className="page-link"
                    onClick={() => handleFilterChange('page', pagination.current_page - 1)}
                    disabled={!pagination.has_previous}
                  >
                    Previous
                  </button>
                </li>
                {Array.from({ length: pagination.total_pages }, (_, i) => i + 1).map((page) => (
                  <li key={page} className={`page-item ${page === pagination.current_page ? 'active' : ''}`}>
                    <button
                      className="page-link"
                      onClick={() => handleFilterChange('page', page)}
                    >
                      {page}
                    </button>
                  </li>
                ))}
                <li className={`page-item ${!pagination.has_next ? 'disabled' : ''}`}>
                  <button
                    className="page-link"
                    onClick={() => handleFilterChange('page', pagination.current_page + 1)}
                    disabled={!pagination.has_next}
                  >
                    Next
                  </button>
                </li>
              </ul>
            </nav>
          </div>
        )}
      </div>
    </div>
  );
}