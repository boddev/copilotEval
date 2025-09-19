import React, { useState } from 'react';
import { JobCreateRequest, JobType } from '../types/Job';
import { jobService } from '../services/jobService';
import { ExternalConnection } from '../services/authService';

interface JobSubmissionProps {
  onJobSubmitted?: (jobId: string, statusUrl: string) => void;
  knowledgeSources?: ExternalConnection[];
  knowledgeSourcesLoading?: boolean;
}

export default function JobSubmission({ onJobSubmitted, knowledgeSources, knowledgeSourcesLoading }: JobSubmissionProps) {
  const [formData, setFormData] = useState<JobCreateRequest>({
    name: '',
    description: '',
    type: JobType.BulkEvaluation,
    configuration: {
      data_source: '',
      prompt_template: '',
      evaluation_criteria: {
        similarity_threshold: 0.8,
        use_semantic_scoring: true
      },
      agent_configuration: {
        additional_instructions: '',
        knowledge_source: ''
      }
    }
  });

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  const validateForm = (): boolean => {
    const errors: Record<string, string> = {};

    if (!formData.name.trim()) {
      errors.name = 'Job name is required';
    }

    if (!formData.configuration.data_source?.trim()) {
      errors.data_source = 'Data source is required';
    }

    if (!formData.configuration.prompt_template?.trim()) {
      errors.prompt_template = 'Prompt template is required';
    }

    setValidationErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm()) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const response = await jobService.submitJob(formData);
      console.log('🎉 Job submitted successfully:', response);
      
      if (onJobSubmitted) {
        onJobSubmitted(response.job_id, response.status_url);
      }

      // Reset form
      setFormData({
        name: '',
        description: '',
        type: JobType.BulkEvaluation,
        configuration: {
          data_source: '',
          prompt_template: '',
          evaluation_criteria: {
            similarity_threshold: 0.8,
            use_semantic_scoring: true
          },
          agent_configuration: {
            additional_instructions: '',
            knowledge_source: ''
          }
        }
      });
    } catch (err) {
      console.error('❌ Failed to submit job:', err);
      setError(err instanceof Error ? err.message : 'Failed to submit job');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleInputChange = (field: string, value: any) => {
    if (field.includes('.')) {
      const [parent, child] = field.split('.');
      setFormData(prev => ({
        ...prev,
        configuration: {
          ...prev.configuration,
          [parent]: {
            ...prev.configuration[parent as keyof typeof prev.configuration],
            [child]: value
          }
        }
      }));
    } else {
      setFormData(prev => ({
        ...prev,
        [field]: value
      }));
    }

    // Clear validation error when field is modified
    if (validationErrors[field]) {
      setValidationErrors(prev => {
        const updated = { ...prev };
        delete updated[field];
        return updated;
      });
    }
  };

  return (
    <div className="card">
      <div className="card-header">
        <h5 className="card-title mb-0">
          <i className="bi bi-plus-circle me-2"></i>
          Submit New Job
        </h5>
      </div>
      <div className="card-body">
        {error && (
          <div className="alert alert-danger" role="alert">
            <i className="bi bi-exclamation-triangle-fill me-2"></i>
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit}>
          {/* Job Name */}
          <div className="mb-3">
            <label htmlFor="jobName" className="form-label">
              Job Name <span className="text-danger">*</span>
            </label>
            <input
              type="text"
              className={`form-control ${validationErrors.name ? 'is-invalid' : ''}`}
              id="jobName"
              value={formData.name}
              onChange={(e) => handleInputChange('name', e.target.value)}
              placeholder="Enter job name"
            />
            {validationErrors.name && (
              <div className="invalid-feedback">{validationErrors.name}</div>
            )}
          </div>

          {/* Job Description */}
          <div className="mb-3">
            <label htmlFor="jobDescription" className="form-label">
              Description
            </label>
            <textarea
              className="form-control"
              id="jobDescription"
              rows={2}
              value={formData.description}
              onChange={(e) => handleInputChange('description', e.target.value)}
              placeholder="Optional job description"
            />
          </div>

          {/* Data Source */}
          <div className="mb-3">
            <label htmlFor="dataSource" className="form-label">
              Data Source <span className="text-danger">*</span>
            </label>
            <input
              type="text"
              className={`form-control ${validationErrors.data_source ? 'is-invalid' : ''}`}
              id="dataSource"
              value={formData.configuration.data_source}
              onChange={(e) => handleInputChange('data_source', e.target.value)}
              placeholder="e.g., validation-data.csv"
            />
            {validationErrors.data_source && (
              <div className="invalid-feedback">{validationErrors.data_source}</div>
            )}
          </div>

          {/* Prompt Template */}
          <div className="mb-3">
            <label htmlFor="promptTemplate" className="form-label">
              Prompt Template <span className="text-danger">*</span>
            </label>
            <textarea
              className={`form-control ${validationErrors.prompt_template ? 'is-invalid' : ''}`}
              id="promptTemplate"
              rows={3}
              value={formData.configuration.prompt_template}
              onChange={(e) => handleInputChange('prompt_template', e.target.value)}
              placeholder="Enter your prompt template with placeholders like {context}"
            />
            {validationErrors.prompt_template && (
              <div className="invalid-feedback">{validationErrors.prompt_template}</div>
            )}
          </div>

          {/* Evaluation Criteria */}
          <div className="row mb-3">
            <div className="col-md-6">
              <label htmlFor="similarityThreshold" className="form-label">
                Similarity Threshold
              </label>
              <input
                type="number"
                className="form-control"
                id="similarityThreshold"
                min="0"
                max="1"
                step="0.1"
                value={formData.configuration.evaluation_criteria?.similarity_threshold}
                onChange={(e) => handleInputChange('evaluation_criteria.similarity_threshold', parseFloat(e.target.value))}
              />
            </div>
            <div className="col-md-6">
              <div className="form-check mt-4">
                <input
                  className="form-check-input"
                  type="checkbox"
                  id="useSemanticScoring"
                  checked={formData.configuration.evaluation_criteria?.use_semantic_scoring}
                  onChange={(e) => handleInputChange('evaluation_criteria.use_semantic_scoring', e.target.checked)}
                />
                <label className="form-check-label" htmlFor="useSemanticScoring">
                  Use Semantic Scoring
                </label>
              </div>
            </div>
          </div>

          {/* Agent Configuration */}
          <div className="mb-3">
            <label htmlFor="additionalInstructions" className="form-label">
              Agent Instructions
            </label>
            <textarea
              className="form-control"
              id="additionalInstructions"
              rows={2}
              value={formData.configuration.agent_configuration?.additional_instructions}
              onChange={(e) => handleInputChange('agent_configuration.additional_instructions', e.target.value)}
              placeholder="Additional instructions for the evaluation agent"
            />
          </div>

          <div className="mb-3">
            <label htmlFor="knowledgeSource" className="form-label">
              Knowledge Source
            </label>
            <select
              className="form-select"
              id="knowledgeSource"
              value={formData.configuration.agent_configuration?.knowledge_source || ''}
              onChange={(e) => handleInputChange('agent_configuration.knowledge_source', e.target.value)}
              disabled={!!knowledgeSourcesLoading}
            >
              {knowledgeSourcesLoading ? (
                <option value="">Loading knowledge sources...</option>
              ) : (
                <>
                  <option value="">No specific source</option>
                  {knowledgeSources?.map((source) => (
                    <option key={source.id} value={source.id}>
                      {source.name}
                    </option>
                  ))}
                </>
              )}
            </select>
            {knowledgeSourcesLoading && (
              <div className="form-text mt-2">
                <span className="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
                Loading knowledge sources...
              </div>
            )}
          </div>

          {/* Submit Button */}
          <div className="d-grid">
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSubmitting}
            >
              {isSubmitting ? (
                <>
                  <span className="spinner-border spinner-border-sm me-2" role="status"></span>
                  Submitting Job...
                </>
              ) : (
                <>
                  <i className="bi bi-play-circle me-2"></i>
                  Submit Job
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}