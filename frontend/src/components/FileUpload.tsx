import { useState, useRef } from 'react';
import Papa from 'papaparse';
import { ValidationEntry } from '../types/ValidationEntry';

interface FileUploadProps {
  onFileUploaded: (entries: ValidationEntry[]) => void;
}

const FileUpload: React.FC<FileUploadProps> = ({ onFileUploaded }) => {
  const [isProcessing, setIsProcessing] = useState(false);
  const [message, setMessage] = useState('');
  const [debugInfo, setDebugInfo] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    setIsProcessing(true);
    setMessage('Processing file...');
    setDebugInfo(`File: ${file.name}, Size: ${file.size} bytes`);

    Papa.parse(file, {
      header: true,
      skipEmptyLines: true,
      complete: (results) => {
        try {
          const entries: ValidationEntry[] = results.data.map((row: any, index: number) => {
            return {
              id: index + 1,
              prompt: row.Prompt || row.prompt || '',
              expectedOutput: row['Expected Output'] || row.expectedOutput || row.expected || '',
              actualOutput: '',
              score: null,
              status: 'pending' as const
            };
          }).filter(entry => entry.prompt && entry.expectedOutput);

          setDebugInfo(`
File: ${file.name}
Total rows: ${results.data.length}
Valid entries: ${entries.length}
Columns found: ${Object.keys(results.data[0] || {}).join(', ')}
          `.trim());

          onFileUploaded(entries);
          setMessage(`Successfully loaded ${entries.length} entries from ${file.name}`);
        } catch (error) {
          console.error('Error processing file:', error);
          setMessage(`Error processing file: ${error instanceof Error ? error.message : 'Unknown error'}`);
        } finally {
          setIsProcessing(false);
        }
      },
      error: (error) => {
        console.error('Error parsing CSV:', error);
        setMessage(`Error parsing file: ${error.message}`);
        setIsProcessing(false);
      }
    });
  };

  return (
    <div>
      <div className="mb-3">
        <label htmlFor="file-upload" className="form-label fw-bold">
          <i className="bi bi-file-earmark-spreadsheet me-2"></i>
          Select CSV File
        </label>
        <input
          id="file-upload"
          ref={fileInputRef}
          type="file"
          accept=".csv,.tsv,.txt"
          onChange={handleFileChange}
          className="form-control form-control-lg"
          disabled={isProcessing}
        />
        <div className="form-text">
          <i className="bi bi-info-circle me-1"></i>
          Expected columns: <strong>Prompt</strong>, <strong>Expected Output</strong>
        </div>
      </div>

      {isProcessing && (
        <div className="text-center mb-3">
          <div className="spinner-border text-primary" role="status">
            <span className="visually-hidden">Processing...</span>
          </div>
          <div className="mt-2 text-muted">Processing file...</div>
        </div>
      )}
      
      {message && (
        <div className={`alert ${message.includes('Error') ? 'alert-danger' : 'alert-success'} d-flex align-items-center`}>
          <i className={`bi ${message.includes('Error') ? 'bi-exclamation-triangle' : 'bi-check-circle'} me-2`}></i>
          {message}
        </div>
      )}
      
      {debugInfo && (
        <div className="mt-3">
          <div className="card bg-light">
            <div className="card-header py-2">
              <small className="text-muted fw-bold">
                <i className="bi bi-bug me-1"></i>
                File Analysis
              </small>
            </div>
            <div className="card-body py-2">
              <pre className="mb-0" style={{ fontSize: '0.75rem' }}>{debugInfo}</pre>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default FileUpload;
