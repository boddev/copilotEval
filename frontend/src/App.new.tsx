import React, { useState, useEffect } from 'react';
import Papa from 'papaparse';
import 'bootstrap/dist/css/bootstrap.min.css';
import 'bootstrap-icons/font/bootstrap-icons.css';
import ValidationTable from './components/ValidationTable';
import { ValidationEntry } from './types/ValidationEntry';
import { authService, CopilotChatRequest } from './services/authService';
import telemetryService from './services/telemetryService';
import './App.css';

interface CsvRow {
  prompt: string;
  expectedOutput: string;
}

function App() {
  const [entries, setEntries] = useState<ValidationEntry[]>([]);
  const [isProcessing, setIsProcessing] = useState(false);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isAuthenticating, setIsAuthenticating] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);

  useEffect(() => {
    // Track page view
    telemetryService.trackPageView('CopilotEval App');
    
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
    setIsAuthenticating(true);
    setAuthError(null);

    try {
      await authService.exchangeCodeForToken(code, state);
      setIsAuthenticated(true);
      
      // Clear URL parameters
      const url = new URL(window.location.href);
      url.searchParams.delete('code');
      url.searchParams.delete('state');
      url.searchParams.delete('session_state');
      window.history.replaceState({}, document.title, url.toString());
    } catch (error) {
      console.error('OAuth callback error:', error);
      setAuthError('Failed to complete authentication. Please try again.');
    } finally {
      setIsAuthenticating(false);
    }
  };

  const handleLogin = async () => {
    setIsAuthenticating(true);
    setAuthError(null);

    telemetryService.trackUserAction('login_attempt', 'auth_button');

    try {
      const redirectUri = `${window.location.origin}${window.location.pathname}`;
      const authResponse = await authService.getAuthUrl(redirectUri);
      
      telemetryService.trackEvent('auth_url_generated', {
        redirectUri,
        success: true
      });
      
      window.location.href = authResponse.authUrl;
    } catch (error) {
      console.error('Login error:', error);
      setAuthError('Failed to initiate authentication. Please check your configuration.');
      setIsAuthenticating(false);
      
      telemetryService.trackEvent('login_failed', {
        errorMessage: error instanceof Error ? error.message : 'Unknown error',
        step: 'auth_url_generation'
      });
      
      telemetryService.trackException(error instanceof Error ? error : new Error('Unknown login error'), {
        step: 'auth_url_generation'
      });
    }
  };

  const handleLogout = () => {
    telemetryService.trackUserAction('logout', 'auth_button');
    telemetryService.clearUser();
    
    authService.logout();
    setIsAuthenticated(false);
    
    telemetryService.trackEvent('user_logged_out', {
      success: true
    });
  };

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    const startTime = performance.now();
    telemetryService.trackUserAction('csv_upload_started', file.name, {
      fileSize: file.size,
      fileType: file.type
    });

    Papa.parse<CsvRow>(file, {
      header: true,
      skipEmptyLines: true,
      complete: (results) => {
        const duration = performance.now() - startTime;
        const newEntries: ValidationEntry[] = results.data.map((row, index) => ({
          id: Date.now() + index,
          prompt: row.prompt || '',
          expectedOutput: row.expectedOutput || '',
          actualOutput: '',
          score: null,
          status: 'pending' as const
        }));
        setEntries(prev => [...prev, ...newEntries]);

        telemetryService.trackValidationEvent('upload', {
          fileName: file.name,
          entriesCount: newEntries.length,
          success: true
        }, {
          processingTime: duration,
          fileSize: file.size
        });
      },
      error: (error) => {
        const duration = performance.now() - startTime;
        console.error('CSV parsing error:', error);
        alert('Error parsing CSV file. Please check the format.');
        
        telemetryService.trackValidationEvent('error', {
          fileName: file.name,
          errorType: 'csv_parse_error',
          errorMessage: error.message,
          success: false
        }, {
          processingTime: duration,
          fileSize: file.size
        });
        
        telemetryService.trackException(new Error(`CSV parsing error: ${error.message}`), {
          fileName: file.name,
          fileSize: file.size
        });
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
      telemetryService.trackEvent('process_entries_unauthorized');
      return;
    }

    const startTime = performance.now();
    setIsProcessing(true);
    
    const pendingEntries = entries.filter(entry => entry.status === 'pending' && entry.prompt.trim());
    
    telemetryService.trackValidationEvent('process', {
      totalEntries: entries.length,
      pendingEntries: pendingEntries.length,
      batchSize: pendingEntries.length
    });
    
    let successCount = 0;
    let errorCount = 0;
    
    for (const entry of pendingEntries) {
      updateEntry(entry.id, { status: 'processing' });
      
      const entryStartTime = performance.now();
      
      try {
        const chatRequest: Omit<CopilotChatRequest, 'accessToken'> = {
          prompt: entry.prompt,
          conversationId: undefined
        };

        const chatResponse = await authService.chatWithCopilot(chatRequest);
        const entryDuration = performance.now() - entryStartTime;
        
        if (chatResponse.success) {
          updateEntry(entry.id, { 
            actualOutput: chatResponse.response,
            status: 'completed'
          });

          successCount++;
          
          telemetryService.trackEvent('chat_request_success', {
            promptLength: entry.prompt.length,
            responseLength: chatResponse.response.length,
            entryId: entry.id
          }, {
            requestDuration: entryDuration
          });

          // Calculate similarity score
          if (entry.expectedOutput.trim()) {
            try {
              const similarityStartTime = performance.now();
              const similarityResponse = await authService.calculateSimilarity({
                expected: entry.expectedOutput,
                actual: chatResponse.response
              });
              const similarityDuration = performance.now() - similarityStartTime;
              
              if (similarityResponse.success) {
                updateEntry(entry.id, { score: similarityResponse.score });
                
                telemetryService.trackEvent('similarity_calculation_success', {
                  score: similarityResponse.score,
                  entryId: entry.id,
                  expectedLength: entry.expectedOutput.length,
                  actualLength: chatResponse.response.length
                }, {
                  calculationDuration: similarityDuration
                });
              }
            } catch (error) {
              console.error('Similarity calculation error:', error);
              telemetryService.trackException(error instanceof Error ? error : new Error('Similarity calculation failed'), {
                entryId: entry.id,
                step: 'similarity_calculation'
              });
            }
          }
        } else {
          updateEntry(entry.id, { 
            actualOutput: chatResponse.error || 'Unknown error',
            status: 'error'
          });
          
          errorCount++;
          
          telemetryService.trackEvent('chat_request_failed', {
            errorMessage: chatResponse.error || 'Unknown error',
            promptLength: entry.prompt.length,
            entryId: entry.id
          }, {
            requestDuration: entryDuration
          });
        }
      } catch (error) {
        const entryDuration = performance.now() - entryStartTime;
        console.error('Chat error:', error);
        updateEntry(entry.id, { 
          actualOutput: 'Failed to get response from Copilot',
          status: 'error'
        });
        
        errorCount++;
        
        telemetryService.trackEvent('chat_request_exception', {
          errorMessage: error instanceof Error ? error.message : 'Unknown error',
          promptLength: entry.prompt.length,
          entryId: entry.id
        }, {
          requestDuration: entryDuration
        });
        
        telemetryService.trackException(error instanceof Error ? error : new Error('Chat request failed'), {
          entryId: entry.id,
          promptLength: entry.prompt.length
        });
      }

      // Add delay between requests to avoid rate limiting
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    
    const totalDuration = performance.now() - startTime;
    
    telemetryService.trackValidationEvent('complete', {
      totalProcessed: pendingEntries.length,
      successCount,
      errorCount,
      successRate: pendingEntries.length > 0 ? (successCount / pendingEntries.length) * 100 : 0
    }, {
      totalProcessingTime: totalDuration,
      averageTimePerEntry: pendingEntries.length > 0 ? totalDuration / pendingEntries.length : 0
    });
    
    setIsProcessing(false);
  };

  const completedEntries = entries.filter(e => e.status === 'completed').length;
  const pendingEntries = entries.filter(e => e.status === 'pending').length;
  const avgScore = entries.length > 0 ? 
    entries.reduce((sum, e) => sum + (e.score || 0), 0) / entries.length : 0;

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
                    CSV should have columns: <code>prompt</code>, <code>expectedOutput</code>
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
      </div>
    </div>
  );
}

export default App;
