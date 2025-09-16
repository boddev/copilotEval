import React, { useState, useEffect } from 'react';
import Papa from 'papaparse';
import 'bootstrap/dist/css/bootstrap.min.css';
import 'bootstrap-icons/font/bootstrap-icons.css';
import ValidationTable from './components/ValidationTable';
import JobSubmission from './components/JobSubmission';
import JobList from './components/JobList';
import JobStatus from './components/JobStatus';
import { ValidationEntry } from './types/ValidationEntry';
import { Job } from './types/Job';
import { authService, CopilotChatRequest, ExternalConnection } from './services/authService';
import './App.css';

interface CsvRow {
  prompt?: string;
  expectedOutput?: string;
  Prompt?: string;
  'Expected Output'?: string;
  [key: string]: string | undefined;
}

function App() {
  // Existing validation state
  const [entries, setEntries] = useState<ValidationEntry[]>([]);
  const [isProcessing, setIsProcessing] = useState(false);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isAuthenticating, setIsAuthenticating] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [knowledgeSources, setKnowledgeSources] = useState<ExternalConnection[]>([]);
  const [isLoadingKnowledgeSources, setIsLoadingKnowledgeSources] = useState(false);

  // New job management state
  const [activeTab, setActiveTab] = useState<'validation' | 'jobs'>('validation');
  const [selectedJob, setSelectedJob] = useState<Job | null>(null);
  const [showJobStatus, setShowJobStatus] = useState(false);
  const [statusJobId, setStatusJobId] = useState<string | null>(null);
  const [jobListRefreshTrigger, setJobListRefreshTrigger] = useState(0);

  useEffect(() => {
    // Check authentication status on app load
    setIsAuthenticated(authService.isAuthenticated());

    // Check for OAuth callback parameters
    const urlParams = new URLSearchParams(window.location.search);
    const code = urlParams.get('code');
    const state = urlParams.get('state');

    if (code && state) {
      handleOAuthCallback(code, state);
    }
  }, []);

  const handleOAuthCallback = async (code: string, state: string) => {
    console.log('🔐 OAuth callback received - Code:', code.substring(0, 20) + '...', 'State:', state);
    setIsAuthenticating(true);
    setAuthError(null);

    try {
      const redirectUri = `${window.location.origin}${window.location.pathname}`;
      console.log('🔗 Redirect URI:', redirectUri);
      
      await authService.exchangeCodeForToken(code, state, redirectUri);
      console.log('✅ Token exchange successful');
      console.log('🎫 Access token available:', !!authService.getAccessToken());
      
      setIsAuthenticated(true);
      
      // Clear URL parameters
      const url = new URL(window.location.href);
      url.searchParams.delete('code');
      url.searchParams.delete('state');
      url.searchParams.delete('session_state');
      window.history.replaceState({}, document.title, url.toString());
    } catch (error) {
      console.error('❌ OAuth callback error:', error);
      setAuthError('Failed to complete authentication. Please try again.');
    } finally {
      setIsAuthenticating(false);
    }
  };

  const handleLogin = async () => {
    setIsAuthenticating(true);
    setAuthError(null);

    try {
      const redirectUri = `${window.location.origin}${window.location.pathname}`;
      const authResponse = await authService.getAuthUrl(redirectUri);
      window.location.href = authResponse.authUrl;
    } catch (error) {
      console.error('Login error:', error);
      setAuthError('Failed to initiate authentication. Please check your configuration.');
      setIsAuthenticating(false);
    }
  };

  const handleLogout = () => {
    authService.logout();
    setIsAuthenticated(false);
  };

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    console.log('Uploading file:', file.name);

    Papa.parse<CsvRow>(file, {
      header: true,
      skipEmptyLines: true,
      complete: (results) => {
        console.log('CSV parsing results:', results);
        console.log('Parsed data:', results.data);
        
        if (results.errors.length > 0) {
          console.error('CSV parsing errors:', results.errors);
        }

        const newEntries: ValidationEntry[] = results.data.map((row, index) => {
          console.log(`Row ${index}:`, row);
          
          // Handle different column name formats
          const prompt = row.prompt || row.Prompt || '';
          const expectedOutput = row.expectedOutput || row['Expected Output'] || '';
          
          return {
            id: Date.now() + index,
            prompt: prompt,
            expectedOutput: expectedOutput,
            actualOutput: '',
            score: null,
            status: 'pending' as const
          };
        });
        
        console.log('Created entries:', newEntries);
        setEntries(prev => [...prev, ...newEntries]);
      },
      error: (error) => {
        console.error('CSV parsing error:', error);
        alert('Error parsing CSV file. Please check the format.');
      }
    });

    // Reset file input
    event.target.value = '';
  };

  const addNewEntry = () => {
    const newEntry: ValidationEntry = {
      id: Date.now(),
      prompt: '',
      expectedOutput: '',
      actualOutput: '',
      score: null,
      status: 'pending'
    };
    setEntries(prev => [...prev, newEntry]);
  };

  const removeEntry = (id: number) => {
    setEntries(prev => prev.filter(entry => entry.id !== id));
  };

  const updateEntry = (id: number, updates: Partial<ValidationEntry>) => {
    setEntries(prev => prev.map(entry =>
      entry.id === id ? { ...entry, ...updates } : entry
    ));
  };

  const clearAll = () => {
    setEntries([]);
  };

  const processEntries = async () => {
    if (!isAuthenticated) {
      alert('Please log in to Microsoft 365 first.');
      return;
    }

    console.log('🚀 Starting validation process...');
    console.log('🔐 Authentication status:', isAuthenticated);
    console.log('🎫 Access token available:', !!authService.getAccessToken());
    
    setIsProcessing(true);
    
    const pendingEntries = entries.filter(entry => entry.status === 'pending' && entry.prompt.trim());
    console.log('📝 Processing', pendingEntries.length, 'pending entries');
    
    for (const entry of pendingEntries) {
      console.log('🔄 Processing entry:', entry.id, 'Prompt:', entry.prompt.substring(0, 50) + '...');
      updateEntry(entry.id, { status: 'processing' });
      
      try {
        const chatRequest: Omit<CopilotChatRequest, 'accessToken'> = {
          prompt: entry.prompt,
          conversationId: undefined,
          selectedAgentId: entry.selectedAgentId,
          additionalInstructions: entry.additionalInstructions,
          selectedKnowledgeSource: entry.selectedKnowledgeSource
        };

        console.log('📤 Sending chat request:', chatRequest);
        const chatResponse = await authService.chatWithCopilot(chatRequest);
        console.log('📥 Received chat response:', chatResponse);
        
        if (chatResponse.success) {
          updateEntry(entry.id, { 
            actualOutput: chatResponse.response,
            status: 'completed'
          });

          // Calculate similarity score
          if (entry.expectedOutput.trim()) {
            try {
              console.log('📊 Calculating similarity score...');
              const similarityResponse = await authService.calculateSimilarity({
                expected: entry.expectedOutput,
                actual: chatResponse.response,
                additionalInstructions: entry.additionalInstructions,
                selectedKnowledgeSource: entry.selectedKnowledgeSource
              });
              
              if (similarityResponse.success) {
                updateEntry(entry.id, { 
                  score: similarityResponse.score,
                  reasoning: similarityResponse.reasoning,
                  differences: similarityResponse.differences
                });
                console.log('✅ Similarity score calculated:', {
                  score: similarityResponse.score,
                  reasoning: similarityResponse.reasoning,
                  differences: similarityResponse.differences
                });
              } else {
                console.error('❌ Similarity calculation failed:', similarityResponse.error);
              }
            } catch (error) {
              console.error('❌ Similarity calculation error:', error);
            }
          }
        } else {
          console.error('❌ Chat request failed:', chatResponse.error);
          updateEntry(entry.id, { 
            actualOutput: chatResponse.error || 'Unknown error',
            status: 'error'
          });
        }
      } catch (error) {
        console.error('❌ Chat error:', error);
        updateEntry(entry.id, { 
          actualOutput: 'Failed to get response from Copilot',
          status: 'error'
        });
      }

      // Add delay between requests to avoid rate limiting
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    
    setIsProcessing(false);
    console.log('🏁 Validation process completed');
  };

  const loadKnowledgeSources = async () => {
    console.log('🗃️ Loading external knowledge sources...');
    setIsLoadingKnowledgeSources(true);
    
    try {
      const sourcesResponse = await authService.getKnowledgeSources();
      setKnowledgeSources(sourcesResponse.value || []);
      console.log('✅ Loaded', sourcesResponse.value?.length || 0, 'knowledge sources');
    } catch (error) {
      console.error('❌ Failed to load knowledge sources:', error);
      
      let errorMessage = 'Failed to load knowledge sources';
      
      // Check for permission errors
      if ((error as any)?.response?.status === 403) {
        errorMessage = 'Permission denied: Your application needs Microsoft Graph permissions to access external connections. ' +
                     'Required permissions: ExternalConnection.Read.All or ExternalConnection.ReadWrite.All. ' +
                     'Please contact your administrator to grant these permissions and provide admin consent.';
      } else if ((error as any)?.response?.data?.detail) {
        errorMessage = (error as any).response.data.detail;
      } else {
        errorMessage = 'Failed to load knowledge sources: ' + (error as Error).message;
      }
      
      alert(errorMessage);
    } finally {
      setIsLoadingKnowledgeSources(false);
    }
  };

  const completedEntries = entries.filter(e => e.status === 'completed').length;
  const pendingEntries = entries.filter(e => e.status === 'pending').length;
  const avgScore = entries.length > 0 ? 
    entries.reduce((sum, e) => sum + (e.score || 0), 0) / entries.length : 0;

  // Job management handlers
  const handleJobSubmitted = (jobId: string, statusUrl: string) => {
    console.log('🎉 Job submitted successfully:', jobId, statusUrl);
    setStatusJobId(jobId);
    setShowJobStatus(true);
    setActiveTab('jobs');
    setJobListRefreshTrigger(prev => prev + 1);
    
    // Show success notification
    alert(`Job submitted successfully!\nJob ID: ${jobId}\nStatus URL: ${statusUrl}`);
  };

  const handleJobSelect = (job: Job) => {
    setSelectedJob(job);
    setStatusJobId(job.id);
    setShowJobStatus(true);
  };

  const handleCloseJobStatus = () => {
    setShowJobStatus(false);
    setStatusJobId(null);
    setSelectedJob(null);
  };

  if (isAuthenticating) {
    return (
      <div className="min-vh-100 d-flex align-items-center justify-content-center bg-light">
        <div className="text-center">
          <div className="spinner-border text-primary mb-3" role="status">
            <span className="visually-hidden">Loading...</span>
          </div>
          <h5>Authenticating...</h5>
        </div>
      </div>
    );
  }

  return (
    <div className="min-vh-100 bg-light">
      {/* Navigation Header */}
      <nav className="navbar navbar-expand-lg navbar-dark bg-primary shadow-sm">
        <div className="container-fluid">
          <span className="navbar-brand mb-0 h1">
            <i className="bi bi-cpu me-2"></i>
            Copilot Evaluation Tool
          </span>
          <div className="navbar-nav ms-auto d-flex flex-row align-items-center">
            {isAuthenticated ? (
              <>
                <span className="nav-item text-light me-3">
                  <small>
                    <i className="bi bi-shield-check me-1"></i>
                    Authenticated
                  </small>
                </span>
                <button 
                  className="btn btn-outline-light btn-sm"
                  onClick={handleLogout}
                >
                  <i className="bi bi-box-arrow-right me-1"></i>
                  Sign Out
                </button>
              </>
            ) : (
              <button 
                className="btn btn-light btn-sm"
                onClick={handleLogin}
                disabled={isAuthenticating}
              >
                <i className="bi bi-microsoft me-1"></i>
                Sign in to M365
              </button>
            )}
          </div>
        </div>
      </nav>

      {authError && (
        <div className="alert alert-danger alert-dismissible fade show m-3" role="alert">
          <i className="bi bi-exclamation-triangle me-2"></i>
          {authError}
          <button 
            type="button" 
            className="btn-close" 
            onClick={() => setAuthError(null)}
          ></button>
        </div>
      )}

      {/* Main Content */}
      <div className="container-fluid p-4">
        {/* Tab Navigation */}
        <ul className="nav nav-tabs mb-0" role="tablist">
          <li className="nav-item" role="presentation">
            <button
              className={`nav-link ${activeTab === 'validation' ? 'active' : ''}`}
              onClick={() => setActiveTab('validation')}
              type="button"
              role="tab"
            >
              <i className="bi bi-check2-square me-2"></i>
              Validation Testing
            </button>
          </li>
          <li className="nav-item" role="presentation">
            <button
              className={`nav-link ${activeTab === 'jobs' ? 'active' : ''}`}
              onClick={() => setActiveTab('jobs')}
              type="button"
              role="tab"
            >
              <i className="bi bi-gear-wide-connected me-2"></i>
              Job Management
            </button>
          </li>
        </ul>

        {/* Tab Content */}
        <div className="tab-content">
          {/* Validation Tab */}
          {activeTab === 'validation' && (
            <div className="row g-4">
              {/* Left Panel - File Upload and Controls */}
              <div className="col-lg-4">
            <div className="card shadow-sm h-100">
              <div className="card-header bg-white">
                <h5 className="card-title mb-0">
                  <i className="bi bi-upload me-2"></i>
                  Data Upload & Controls
                </h5>
              </div>
              <div className="card-body">
                {/* File Upload */}
                <div className="mb-4">
                  <label className="form-label fw-semibold">
                    <i className="bi bi-file-earmark-csv me-1"></i>
                    Upload CSV File
                  </label>
                  <input
                    type="file"
                    className="form-control"
                    accept=".csv"
                    onChange={handleFileUpload}
                  />
                  <div className="form-text">
                    CSV should have columns: <code>Prompt</code>, <code>Expected Output</code> or <code>prompt</code>, <code>expectedOutput</code>
                  </div>
                </div>

                {/* Action Buttons */}
                <div className="d-grid gap-2">
                  <button 
                    className="btn btn-primary"
                    onClick={addNewEntry}
                  >
                    <i className="bi bi-plus-circle me-1"></i>
                    Add New Entry
                  </button>
                  
                  <button 
                    className="btn btn-success"
                    onClick={processEntries}
                    disabled={!isAuthenticated || isProcessing || pendingEntries === 0}
                  >
                    {isProcessing ? (
                      <>
                        <span className="spinner-border spinner-border-sm me-2"></span>
                        Processing...
                      </>
                    ) : (
                      <>
                        <i className="bi bi-play-circle me-1"></i>
                        Run Validation ({pendingEntries})
                      </>
                    )}
                  </button>
                  
                  <button 
                    className="btn btn-outline-danger"
                    onClick={clearAll}
                    disabled={entries.length === 0}
                  >
                    <i className="bi bi-trash me-1"></i>
                    Clear All
                  </button>

                  {isAuthenticated && (
                    <button 
                      className="btn btn-outline-info"
                      onClick={loadKnowledgeSources}
                      disabled={isLoadingKnowledgeSources}
                    >
                      {isLoadingKnowledgeSources ? (
                        <>
                          <span className="spinner-border spinner-border-sm me-2"></span>
                          Loading...
                        </>
                      ) : (
                        <>
                          <i className="bi bi-database me-1"></i>
                          Load Knowledge Sources ({knowledgeSources.length})
                        </>
                      )}
                    </button>
                  )}
                </div>

                {!isAuthenticated && (
                  <div className="alert alert-warning mt-3">
                    <i className="bi bi-info-circle me-2"></i>
                    Please sign in to Microsoft 365 to use Copilot validation.
                  </div>
                )}
              </div>

              {/* Statistics */}
              <div className="card-footer bg-light">
                <div className="row text-center">
                  <div className="col-6">
                    <div className="border-end">
                      <h6 className="mb-1 text-primary">{completedEntries}</h6>
                      <small className="text-muted">Completed</small>
                    </div>
                  </div>
                  <div className="col-6">
                    <h6 className="mb-1 text-success">{avgScore.toFixed(2)}</h6>
                    <small className="text-muted">Avg Score</small>
                  </div>
                </div>
              </div>
            </div>

            {/* Knowledge Sources Section */}
            {knowledgeSources.length > 0 && (
              <div className="card shadow-sm mt-3">
                <div className="card-header bg-white">
                  <h6 className="card-title mb-0">
                    <i className="bi bi-database me-2"></i>
                    Available Knowledge Sources ({knowledgeSources.length})
                  </h6>
                </div>
                <div className="card-body">
                  <div className="row g-2">
                    {knowledgeSources.slice(0, 6).map((source) => (
                      <div key={source.id} className="col-12">
                        <div className="d-flex align-items-start p-2 border rounded">
                          <div className="flex-grow-1">
                            <div className="fw-semibold small">{source.name}</div>
                            {source.description && (
                              <div className="text-muted small">{source.description.substring(0, 80)}...</div>
                            )}
                            <div className="d-flex gap-2 mt-1">
                              {source.state && (
                                <span className="badge bg-secondary">{source.state}</span>
                              )}
                              <span className="badge bg-info text-truncate" style={{ maxWidth: '150px' }}>
                                ID: {source.id}
                              </span>
                            </div>
                          </div>
                        </div>
                      </div>
                    ))}
                    {knowledgeSources.length > 6 && (
                      <div className="col-12">
                        <div className="text-center text-muted small">
                          ... and {knowledgeSources.length - 6} more knowledge sources
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}
          </div>

              {/* Right Panel - Validation Table */}
              <div className="col-lg-8">
            <div className="card shadow-sm h-100">
              <div className="card-header bg-white">
                <h5 className="card-title mb-0">
                  <i className="bi bi-table me-2"></i>
                  Validation Results
                </h5>
              </div>
              <div className="card-body p-0">
                {entries.length > 0 ? (
                  <ValidationTable
                    entries={entries}
                    onAddEntry={addNewEntry}
                    onRemoveEntry={removeEntry}
                    onUpdateEntry={updateEntry}
                    isAuthenticated={isAuthenticated}
                    knowledgeSources={knowledgeSources}
                  />
                ) : (
                  <div className="text-center py-5">
                    <i className="bi bi-inbox display-1 text-muted"></i>
                    <h4 className="text-muted mt-3">No validation data</h4>
                    <p className="text-muted">Upload a CSV file or add entries manually to get started.</p>
                  </div>
                )}
              </div>
            </div>
          </div>
            </div>
          )}

          {/* Jobs Tab */}
          {activeTab === 'jobs' && (
            <div className="row g-4">
              {/* Job Status Modal/Panel */}
              {showJobStatus && statusJobId && (
                <div className="col-12">
                  <JobStatus
                    jobId={statusJobId}
                    onClose={handleCloseJobStatus}
                    autoRefresh={true}
                    refreshInterval={5000}
                  />
                </div>
              )}

              {/* Job Submission */}
              <div className="col-lg-4">
                <JobSubmission onJobSubmitted={handleJobSubmitted} />
              </div>

              {/* Job List */}
              <div className="col-lg-8">
                <JobList
                  onJobSelect={handleJobSelect}
                  refreshTrigger={jobListRefreshTrigger}
                />
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default App;
