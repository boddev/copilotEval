# API Contracts & JobMessage Schema

This directory contains the complete API contracts and JobMessage schema for the Copilot Evaluation platform.

## 📁 Files Structure

```
backend/
├── openapi/
│   ├── openapi.yaml                  # Complete OpenAPI 3.0.3 specification
│   └── job-message-schema.json       # JSON Schema for JobMessage queue
├── docs/sample-payloads/
│   ├── job-create-request-normal.json     # Normal job creation payload
│   ├── job-create-request-large-payload.json  # Large payload with blob ref
│   ├── job-response.json                   # Job creation response
│   ├── job-message-created.json           # JobMessage: job created
│   ├── job-message-progress.json          # JobMessage: job progress
│   ├── job-message-completed-large-payload.json  # JobMessage: job completed (large)
│   ├── job-message-failed.json            # JobMessage: job failed
│   ├── error-response-invalid-request.json     # Error response example
│   └── error-response-rate-limited.json        # Rate limit error example
├── scripts/
│   └── contract-tests.sh                  # Contract validation test script
└── Models/
    └── JobMessage.cs                      # C# model classes
```

## 🚀 API Endpoints Overview

### Job Lifecycle Management
- `POST /api/jobs` - Submit new evaluation job
- `GET /api/jobs` - List jobs with filtering & pagination
- `GET /api/jobs/{id}` - Get detailed job information
- `DELETE /api/jobs/{id}` - Cancel a running job
- `GET /api/jobs/{id}/results` - Download job results

### Health & Monitoring  
- `GET /api/health` - API health check

## 📋 JobMessage Queue Schema

The `JobMessage` schema supports asynchronous job processing with the following message types:

- **`job_created`** - Job has been created and queued
- **`job_started`** - Job processing has begun
- **`job_progress`** - Job progress update
- **`job_completed`** - Job has completed successfully
- **`job_failed`** - Job has failed with error details
- **`job_cancelled`** - Job has been cancelled

### Large Payload Support

For large datasets, the system supports blob storage references:

```json
{
  "blob_references": [
    {
      "blob_id": "550e8400-e29b-41d4-a716-446655440000",
      "storage_account": "copilotevaldata",
      "container": "job-data",
      "blob_name": "jobs/2024/01/job_abc123def456/results.json",
      "content_type": "application/json",
      "size_bytes": 2048576,
      "access_url": "https://..."
    }
  ]
}
```

## 🧪 Running Contract Tests

The contract tests validate all sample payloads against their schemas:

```bash
# Run all contract tests
cd backend
./scripts/contract-tests.sh

# Test individual schemas
ajv -s openapi/job-message-schema.json -d docs/sample-payloads/job-message-created.json --strict=false

# Validate OpenAPI specification
swagger-cli validate openapi/openapi.yaml
redocly lint openapi/openapi.yaml
```

## 🔧 CI/CD Integration

Contract tests run automatically in GitHub Actions on:
- Changes to OpenAPI specifications
- Changes to sample payloads
- Changes to model classes
- Changes to test scripts

See `.github/workflows/contract-tests.yml` for the complete CI configuration.

## 📊 Error Handling

All error responses follow a consistent format:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human-readable error description",
    "details": {
      "field": "Additional context",
      "trace_id": "4bf92f3577b34da6a3ce929d0e0e4736"
    }
  }
}
```

Common error codes:
- `INVALID_REQUEST` - Request validation failed
- `UNAUTHORIZED` - Authentication required
- `NOT_FOUND` - Resource not found
- `RATE_LIMITED` - Rate limit exceeded
- `SERVER_ERROR` - Internal server error

## 🔐 Authentication

All job endpoints require Bearer token authentication:

```http
Authorization: Bearer <access_token>
```

Tokens are obtained through the OAuth 2.0 flow via Microsoft identity platform.

## 📈 Rate Limiting

- **General API**: 100 requests per minute per user
- **Job Submission**: 10 jobs per minute per user

Rate limit headers are included in responses:
- `X-RateLimit-Limit` - Request limit per window
- `X-RateLimit-Remaining` - Requests remaining
- `X-RateLimit-Reset` - Reset timestamp

## 🔍 Tracing Support

All endpoints support W3C Trace Context for distributed tracing:

```http
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```

## 📝 Job Configuration Options

### Data Sources
- **Normal payloads**: `data_source` field with file path
- **Large payloads**: `data_source_blob_ref` with blob storage URL

### Evaluation Criteria
- `similarity_threshold` (0.0-1.0) - Minimum score for passing
- `use_semantic_scoring` (boolean) - Enable semantic similarity
- `custom_evaluators` (array) - Custom evaluator names

### Agent Configuration
- `selected_agent_id` - Agent ID (null for default Copilot)
- `additional_instructions` - Extra instructions for agent
- `knowledge_source` - Knowledge source connection ID

## 📚 Example Usage

### Submit a Job
```http
POST /api/jobs
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "Financial Analysis Evaluation",
  "description": "Evaluate responses against financial data",
  "type": "bulk_evaluation",
  "configuration": {
    "data_source": "financial-validation-data.csv",
    "prompt_template": "Analyze this financial data: {context}",
    "evaluation_criteria": {
      "similarity_threshold": 0.8,
      "use_semantic_scoring": true
    }
  }
}
```

### Check Job Status
```http
GET /api/jobs/job_abc123def456
Authorization: Bearer <token>
```

### Download Results
```http
GET /api/jobs/job_abc123def456/results?format=json
Authorization: Bearer <token>
```

## 🛠️ Development Notes

### C# Model Classes
The `JobMessage.cs` file contains all model classes with proper JSON serialization attributes for ASP.NET Core integration.

### OpenAPI Compliance
The specification validates successfully with:
- ✅ swagger-cli validator
- ✅ Redocly OpenAPI linter (with acceptable warnings)
- ✅ JSON Schema validation for all samples

### Large Payload Design
The blob reference system allows handling datasets larger than typical message queue limits while maintaining efficient queue processing.

---

For detailed API documentation, see the generated docs from `openapi.yaml` or visit the Swagger UI when running the development server.