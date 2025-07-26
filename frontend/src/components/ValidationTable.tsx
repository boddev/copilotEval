import React from 'react';
import { ValidationEntry } from '../types/ValidationEntry';
import { ExternalConnection } from '../services/authService';

interface ValidationTableProps {
  entries: ValidationEntry[];
  onAddEntry: () => void;
  onRemoveEntry: (id: number) => void;
  onUpdateEntry: (id: number, updates: Partial<ValidationEntry>) => void;
  isAuthenticated: boolean;
  knowledgeSources: ExternalConnection[];
}

const ValidationTable: React.FC<ValidationTableProps> = ({
  entries,
  onAddEntry,
  onRemoveEntry,
  onUpdateEntry,
  knowledgeSources
}) => {
  const handlePromptChange = (id: number, prompt: string) => {
    onUpdateEntry(id, { prompt });
  };

  const handleExpectedOutputChange = (id: number, expectedOutput: string) => {
    onUpdateEntry(id, { expectedOutput });
  };

  const handleKnowledgeSourceChange = (id: number, selectedKnowledgeSource: string) => {
    onUpdateEntry(id, { selectedKnowledgeSource: selectedKnowledgeSource || undefined });
  };

  const handleAdditionalInstructionsChange = (id: number, additionalInstructions: string) => {
    onUpdateEntry(id, { additionalInstructions: additionalInstructions || undefined });
  };

  const getStatusBadge = (status: ValidationEntry['status']) => {
    const statusClasses = {
      pending: 'bg-secondary',
      processing: 'bg-warning',
      completed: 'bg-success',
      error: 'bg-danger'
    };
    return `badge ${statusClasses[status]}`;
  };

  const getScoreBadge = (score: number | null) => {
    if (score === null) return 'bg-secondary';
    if (score >= 0.8) return 'bg-success';
    if (score >= 0.6) return 'bg-warning';
    return 'bg-danger';
  };

  return (
    <div className="container-fluid px-0">
      {/* Desktop/Tablet View */}
      <div className="d-none d-md-block">
        <div className="table-responsive">
          <table className="table table-hover mb-0">
            <thead className="table-dark">
              <tr>
                <th style={{ width: '3%', minWidth: '40px' }} className="text-center">#</th>
                <th style={{ width: '13%', minWidth: '150px' }}>
                  <i className="bi bi-chat-quote me-1"></i>
                  Prompt
                </th>
                <th style={{ width: '10%', minWidth: '120px' }}>
                  <i className="bi bi-database me-1"></i>
                  Knowledge Source
                </th>
                <th style={{ width: '13%', minWidth: '150px' }}>
                  <i className="bi bi-gear me-1"></i>
                  Additional Instructions
                </th>
                <th style={{ width: '13%', minWidth: '150px' }}>
                  <i className="bi bi-check-circle me-1"></i>
                  Expected Output
                </th>
                <th style={{ width: '13%', minWidth: '150px' }}>
                  <i className="bi bi-cpu me-1"></i>
                  Actual Output
                </th>
                <th style={{ width: '15%', minWidth: '160px' }}>
                  <i className="bi bi-lightbulb me-1"></i>
                  Reasoning
                </th>
                <th style={{ width: '7%', minWidth: '70px' }} className="text-center">
                  <i className="bi bi-graph-up me-1"></i>
                  Score
                </th>
                <th style={{ width: '5%', minWidth: '60px' }} className="text-center">Actions</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry, index) => (
                <tr key={entry.id} className={index % 2 === 0 ? 'table-light' : ''}>
                  <td className="text-center fw-bold text-muted align-middle">
                    {entry.id}
                  </td>
                  <td>
                    <textarea
                      className="form-control"
                      rows={4}
                      value={entry.prompt}
                      onChange={(e) => handlePromptChange(entry.id, e.target.value)}
                      placeholder="Enter prompt..."
                      style={{ minHeight: '120px', resize: 'vertical' }}
                    />
                  </td>
                  <td>
                    <select
                      className="form-select"
                      value={entry.selectedKnowledgeSource || ''}
                      onChange={(e) => handleKnowledgeSourceChange(entry.id, e.target.value)}
                    >
                      <option value="">No specific source</option>
                      {knowledgeSources.map((source) => (
                        <option key={source.id} value={source.id}>
                          {source.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <textarea
                      className="form-control"
                      rows={4}
                      value={entry.additionalInstructions || ''}
                      onChange={(e) => handleAdditionalInstructionsChange(entry.id, e.target.value)}
                      placeholder="Enter additional instructions or context for this prompt..."
                      style={{ minHeight: '120px', resize: 'vertical' }}
                    />
                    <small className="text-muted mt-1 d-block">
                      <i className="bi bi-info-circle me-1"></i>
                      Optional: Add specific instructions that will be sent as additional context with each prompt
                    </small>
                  </td>
                  <td>
                    <textarea
                      className="form-control"
                      rows={4}
                      value={entry.expectedOutput}
                      onChange={(e) => handleExpectedOutputChange(entry.id, e.target.value)}
                      placeholder="Enter expected output..."
                      style={{ minHeight: '120px', resize: 'vertical' }}
                    />
                  </td>
                  <td>
                    <textarea
                      className="form-control bg-light"
                      rows={4}
                      value={entry.actualOutput}
                      readOnly
                      placeholder="Will be filled automatically"
                      style={{ minHeight: '120px', resize: 'vertical' }}
                    />
                    {!entry.actualOutput && (
                      <small className="text-muted mt-1 d-block">
                        <i className="bi bi-clock me-1"></i>
                        Awaiting API response
                      </small>
                    )}
                  </td>
                  <td>
                    <textarea
                      className="form-control bg-light"
                      rows={4}
                      value={entry.reasoning || ''}
                      readOnly
                      placeholder="Similarity reasoning will appear here..."
                      style={{ minHeight: '120px', resize: 'vertical' }}
                    />
                    {entry.differences && (
                      <small className="text-muted mt-1 d-block">
                        <strong>Differences:</strong> {entry.differences}
                      </small>
                    )}
                  </td>
                  <td className="text-center align-middle">
                    <div className="d-flex flex-column align-items-center gap-2">
                      {entry.score !== null ? (
                        <span className={`badge ${getScoreBadge(entry.score)} fs-5 px-3 py-2`}>
                          {entry.score.toFixed(3)}
                        </span>
                      ) : (
                        <span className="badge bg-secondary fs-6 px-3 py-2">
                          <i className="bi bi-dash"></i>
                        </span>
                      )}
                      <span className={`badge ${getStatusBadge(entry.status)} text-uppercase`} style={{ fontSize: '0.65rem' }}>
                        {entry.status}
                      </span>
                    </div>
                  </td>
                  <td className="text-center align-middle">
                    <button
                      className="btn btn-outline-danger btn-sm"
                      onClick={() => onRemoveEntry(entry.id)}
                      title="Remove entry"
                    >
                      <i className="bi bi-trash"></i>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Mobile View */}
      <div className="d-md-none">
        {entries.map((entry) => (
          <div key={entry.id} className="card mb-3 shadow-sm">
            <div className="card-header bg-primary text-white d-flex justify-content-between align-items-center">
              <h6 className="mb-0">
                <i className="bi bi-hash me-1"></i>
                Entry {entry.id}
              </h6>
              <div className="d-flex gap-2 align-items-center">
                <span className={`badge ${getStatusBadge(entry.status)} text-uppercase`}>
                  {entry.status}
                </span>
                <button
                  className="btn btn-outline-light btn-sm"
                  onClick={() => onRemoveEntry(entry.id)}
                  title="Remove entry"
                >
                  <i className="bi bi-trash"></i>
                </button>
              </div>
            </div>
            <div className="card-body">
              <div className="mb-3">
                <label className="form-label fw-bold">
                  <i className="bi bi-chat-quote me-1"></i>
                  Prompt
                </label>
                <textarea
                  className="form-control"
                  rows={3}
                  value={entry.prompt}
                  onChange={(e) => handlePromptChange(entry.id, e.target.value)}
                  placeholder="Enter prompt..."
                  style={{ minHeight: '100px', resize: 'vertical' }}
                />
              </div>
              
              <div className="mb-3">
                <label className="form-label fw-bold">
                  <i className="bi bi-database me-1"></i>
                  Knowledge Source
                </label>
                <select
                  className="form-select"
                  value={entry.selectedKnowledgeSource || ''}
                  onChange={(e) => handleKnowledgeSourceChange(entry.id, e.target.value)}
                >
                  <option value="">No specific source</option>
                  {knowledgeSources.map((source) => (
                    <option key={source.id} value={source.id}>
                      {source.name}
                    </option>
                  ))}
                </select>
              </div>
              
              <div className="mb-3">
                <label className="form-label fw-bold">
                  <i className="bi bi-gear me-1"></i>
                  Additional Instructions
                </label>
                <textarea
                  className="form-control"
                  rows={3}
                  value={entry.additionalInstructions || ''}
                  onChange={(e) => handleAdditionalInstructionsChange(entry.id, e.target.value)}
                  placeholder="Enter additional instructions or context for this prompt..."
                  style={{ minHeight: '100px', resize: 'vertical' }}
                />
                <small className="text-muted mt-1">
                  <i className="bi bi-info-circle me-1"></i>
                  Optional: Add specific instructions that will be sent as additional context with each prompt
                </small>
              </div>
              
              <div className="mb-3">
                <label className="form-label fw-bold">
                  <i className="bi bi-check-circle me-1"></i>
                  Expected Output
                </label>
                <textarea
                  className="form-control"
                  rows={3}
                  value={entry.expectedOutput}
                  onChange={(e) => handleExpectedOutputChange(entry.id, e.target.value)}
                  placeholder="Enter expected output..."
                  style={{ minHeight: '100px', resize: 'vertical' }}
                />
              </div>
              
              <div className="mb-3">
                <label className="form-label fw-bold">
                  <i className="bi bi-robot me-1"></i>
                  Actual Output
                </label>
                <textarea
                  className="form-control bg-light"
                  rows={3}
                  value={entry.actualOutput}
                  readOnly
                  placeholder="Will be filled automatically"
                  style={{ minHeight: '100px', resize: 'vertical' }}
                />
                {!entry.actualOutput && (
                  <small className="text-muted mt-1 d-block">
                    <i className="bi bi-clock me-1"></i>
                    Awaiting API response
                  </small>
                )}
              </div>

              {(entry.reasoning || entry.differences) && (
                <div className="mb-3">
                  <label className="form-label fw-bold">
                    <i className="bi bi-lightbulb me-1"></i>
                    Similarity Analysis
                  </label>
                  {entry.reasoning && (
                    <textarea
                      className="form-control bg-light mb-2"
                      rows={3}
                      value={entry.reasoning}
                      readOnly
                      placeholder="Similarity reasoning will appear here..."
                      style={{ minHeight: '100px', resize: 'vertical' }}
                    />
                  )}
                  {entry.differences && (
                    <div className="alert alert-info py-2">
                      <small>
                        <strong>Key Differences:</strong> {entry.differences}
                      </small>
                    </div>
                  )}
                </div>
              )}
              
              <div className="d-flex justify-content-center">
                {entry.score !== null ? (
                  <span className={`badge ${getScoreBadge(entry.score)} fs-4 px-4 py-3`}>
                    <i className="bi bi-graph-up me-2"></i>
                    Score: {entry.score.toFixed(3)}
                  </span>
                ) : (
                  <span className="badge bg-secondary fs-6 px-3 py-2">
                    <i className="bi bi-dash me-1"></i>
                    No Score
                  </span>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>
      
      {/* Table Footer with Stats */}
      <div className="card-footer bg-light mt-3">
        <div className="row text-center g-2">
          <div className="col-6 col-sm-3">
            <small className="text-muted">
              <i className="bi bi-list-ol me-1"></i>
              Total: <strong>{entries.length}</strong>
            </small>
          </div>
          <div className="col-6 col-sm-3">
            <small className="text-success">
              <i className="bi bi-check-circle me-1"></i>
              Completed: <strong>{entries.filter(e => e.status === 'completed').length}</strong>
            </small>
          </div>
          <div className="col-6 col-sm-3">
            <small className="text-warning">
              <i className="bi bi-clock me-1"></i>
              Pending: <strong>{entries.filter(e => e.status === 'pending').length}</strong>
            </small>
          </div>
          <div className="col-6 col-sm-3">
            <small className="text-danger">
              <i className="bi bi-exclamation-triangle me-1"></i>
              Errors: <strong>{entries.filter(e => e.status === 'error').length}</strong>
            </small>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ValidationTable;
