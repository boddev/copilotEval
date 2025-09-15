# API Design - Job Lifecycle Endpoints

## Overview

This document defines the REST API design for managing job lifecycle in the Copilot Evaluation platform. The job system allows users to submit evaluation tasks that run asynchronously and track their progress through completion.

## Base URL

```
https://api.copiloteval.com/api
```

For local development:
```
http://localhost:5000/api
```

## Authentication

All job endpoints require authentication via Bearer token in the Authorization header:

```
Authorization: Bearer <access_token>
```

The access token should be obtained through the existing OAuth flow (`/api/auth/url` and `/api/auth/token` endpoints).

## Tracing

All requests should include a `traceparent` header for distributed tracing following the W3C Trace Context specification:

```
traceparent: 00-{trace-id}-{parent-id}-{trace-flags}
```

Example:
```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```

## Idempotency

POST operations support idempotency through the `Idempotency-Key` header:

```
Idempotency-Key: <unique-key>
```

The key should be a unique string (UUID recommended) that identifies the request. Duplicate requests with the same idempotency key will return the same response without creating duplicate resources.

## Rate Limiting

- 100 requests per minute per authenticated user
- 10 job submissions per minute per authenticated user
- Rate limit headers are included in responses:
  - `X-RateLimit-Limit`: Request limit per window
  - `X-RateLimit-Remaining`: Requests remaining in current window
  - `X-RateLimit-Reset`: Unix timestamp when limit resets

## Job Lifecycle States

Jobs progress through the following states:

1. **pending** - Job submitted and queued for processing
2. **running** - Job is actively being processed
3. **completed** - Job finished successfully
4. **failed** - Job encountered an error and failed
5. **cancelled** - Job was cancelled by user request

## API Endpoints

### 1. Submit Job (POST /api/jobs)

Submit a new evaluation job for processing.

**Request:**
```http
POST /api/jobs
Content-Type: application/json
Authorization: Bearer <access_token>
Idempotency-Key: <unique-key>
traceparent: 00-{trace-id}-{parent-id}-{trace-flags}

{
  "name": "Edgar Filing Evaluation",
  "description": "Evaluate Copilot responses against Edgar filing data",
  "type": "bulk_evaluation",
  "configuration": {
    "data_source": "edgar-validation-data.csv",
    "prompt_template": "Based on the following financial data: {context}, provide an analysis.",
    "evaluation_criteria": {
      "similarity_threshold": 0.8,
      "use_semantic_scoring": true
    },
    "agent_configuration": {
      "selected_agent_id": null,
      "additional_instructions": "Focus on financial accuracy",
      "knowledge_source": "edgar_filings"
    }
  }
}
```

**Response (201 Created):**
```json
{
  "id": "job_abc123def456",
  "name": "Edgar Filing Evaluation",
  "description": "Evaluate Copilot responses against Edgar filing data",
  "type": "bulk_evaluation",
  "status": "pending",
  "created_at": "2024-01-15T10:30:00Z",
  "updated_at": "2024-01-15T10:30:00Z",
  "estimated_completion": "2024-01-15T11:00:00Z",
  "progress": {
    "total_items": 0,
    "completed_items": 0,
    "percentage": 0
  },
  "configuration": {
    "data_source": "edgar-validation-data.csv",
    "prompt_template": "Based on the following financial data: {context}, provide an analysis.",
    "evaluation_criteria": {
      "similarity_threshold": 0.8,
      "use_semantic_scoring": true
    },
    "agent_configuration": {
      "selected_agent_id": null,
      "additional_instructions": "Focus on financial accuracy",
      "knowledge_source": "edgar_filings"
    }
  }
}
```

**Error Responses:**
- `400 Bad Request`: Invalid request body or configuration
- `401 Unauthorized`: Missing or invalid access token
- `429 Too Many Requests`: Rate limit exceeded
- `500 Internal Server Error`: Server error

### 2. List Jobs (GET /api/jobs)

Retrieve a list of jobs with optional filtering and pagination.

**Request:**
```http
GET /api/jobs?status=running&type=bulk_evaluation&page=1&limit=20&sort=created_at&order=desc
Authorization: Bearer <access_token>
traceparent: 00-{trace-id}-{parent-id}-{trace-flags}
```

**Query Parameters:**
- `status` (optional): Filter by job status (pending, running, completed, failed, cancelled)
- `type` (optional): Filter by job type (bulk_evaluation, single_evaluation)
- `page` (optional): Page number for pagination (default: 1)
- `limit` (optional): Number of items per page (default: 20, max: 100)
- `sort` (optional): Sort field (created_at, updated_at, name)
- `order` (optional): Sort order (asc, desc, default: desc)

**Response (200 OK):**
```json
{
  "jobs": [
    {
      "id": "job_abc123def456",
      "name": "Edgar Filing Evaluation",
      "description": "Evaluate Copilot responses against Edgar filing data",
      "type": "bulk_evaluation",
      "status": "running",
      "created_at": "2024-01-15T10:30:00Z",
      "updated_at": "2024-01-15T10:45:00Z",
      "estimated_completion": "2024-01-15T11:00:00Z",
      "progress": {
        "total_items": 100,
        "completed_items": 45,
        "percentage": 45
      }
    }
  ],
  "pagination": {
    "current_page": 1,
    "total_pages": 5,
    "total_items": 92,
    "items_per_page": 20,
    "has_next": true,
    "has_previous": false
  }
}
```

**Error Responses:**
- `400 Bad Request`: Invalid query parameters
- `401 Unauthorized`: Missing or invalid access token
- `429 Too Many Requests`: Rate limit exceeded

### 3. Get Job Details (GET /api/jobs/{id})

Retrieve detailed information about a specific job.

**Request:**
```http
GET /api/jobs/job_abc123def456
Authorization: Bearer <access_token>
traceparent: 00-{trace-id}-{parent-id}-{trace-flags}
```

**Response (200 OK):**
```json
{
  "id": "job_abc123def456",
  "name": "Edgar Filing Evaluation",
  "description": "Evaluate Copilot responses against Edgar filing data",
  "type": "bulk_evaluation",
  "status": "completed",
  "created_at": "2024-01-15T10:30:00Z",
  "updated_at": "2024-01-15T11:15:00Z",
  "completed_at": "2024-01-15T11:15:00Z",
  "estimated_completion": "2024-01-15T11:00:00Z",
  "progress": {
    "total_items": 100,
    "completed_items": 100,
    "percentage": 100
  },
  "configuration": {
    "data_source": "edgar-validation-data.csv",
    "prompt_template": "Based on the following financial data: {context}, provide an analysis.",
    "evaluation_criteria": {
      "similarity_threshold": 0.8,
      "use_semantic_scoring": true
    },
    "agent_configuration": {
      "selected_agent_id": null,
      "additional_instructions": "Focus on financial accuracy",
      "knowledge_source": "edgar_filings"
    }
  },
  "results": {
    "summary": {
      "total_evaluations": 100,
      "passed_evaluations": 87,
      "failed_evaluations": 13,
      "average_similarity_score": 0.82,
      "pass_rate": 0.87
    },
    "download_url": "/api/jobs/job_abc123def456/results",
    "detailed_results": [
      {
        "item_id": "item_001",
        "prompt": "What is the revenue for Q4 2023?",
        "expected_response": "The revenue for Q4 2023 was $2.5B",
        "actual_response": "Q4 2023 revenue totaled $2.5 billion",
        "similarity_score": 0.95,
        "passed": true,
        "evaluation_details": {
          "reasoning": "Both responses convey the same information with minor wording differences",
          "differences": "Expected uses '$2.5B' while actual uses '$2.5 billion'"
        }
      }
    ]
  },
  "error_details": null
}
```

**Response for Failed Job (200 OK):**
```json
{
  "id": "job_xyz789ghi012",
  "name": "Failed Evaluation Job",
  "status": "failed",
  "created_at": "2024-01-15T12:00:00Z",
  "updated_at": "2024-01-15T12:05:00Z",
  "error_details": {
    "error_code": "DATA_SOURCE_UNAVAILABLE",
    "error_message": "Unable to access the specified data source file",
    "error_timestamp": "2024-01-15T12:05:00Z",
    "retry_possible": true
  }
}
```

**Error Responses:**
- `404 Not Found`: Job not found or user doesn't have access
- `401 Unauthorized`: Missing or invalid access token
- `429 Too Many Requests`: Rate limit exceeded

### 4. Cancel Job (POST /api/jobs/{id}/cancel)

Cancel a running or pending job.

**Request:**
```http
POST /api/jobs/job_abc123def456/cancel
Authorization: Bearer <access_token>
Idempotency-Key: <unique-key>
traceparent: 00-{trace-id}-{parent-id}-{trace-flags}

{
  "reason": "User requested cancellation"
}
```

**Response (200 OK):**
```json
{
  "id": "job_abc123def456",
  "status": "cancelled",
  "cancelled_at": "2024-01-15T10:45:00Z",
  "cancellation_reason": "User requested cancellation",
  "progress": {
    "total_items": 100,
    "completed_items": 23,
    "percentage": 23
  }
}
```

**Error Responses:**
- `400 Bad Request`: Job cannot be cancelled (already completed or failed)
- `404 Not Found`: Job not found or user doesn't have access
- `401 Unauthorized`: Missing or invalid access token
- `409 Conflict`: Job is already cancelled
- `429 Too Many Requests`: Rate limit exceeded

## Error Handling

All error responses follow a consistent format:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human-readable error description",
    "details": {
      "field": "Additional error details",
      "trace_id": "4bf92f3577b34da6a3ce929d0e0e4736"
    }
  }
}
```

Common error codes:
- `INVALID_REQUEST`: Request body or parameters are invalid
- `UNAUTHORIZED`: Authentication required or invalid
- `FORBIDDEN`: User doesn't have permission for this resource
- `NOT_FOUND`: Resource not found
- `RATE_LIMITED`: Request rate limit exceeded
- `SERVER_ERROR`: Internal server error

## Webhook Support (Future Enhancement)

The API will support webhook notifications for job status changes:

```json
{
  "webhook_url": "https://client.example.com/webhooks/job-status",
  "events": ["job.completed", "job.failed", "job.cancelled"]
}
```

## Data Retention

- Job metadata is retained for 90 days
- Job results are retained for 30 days
- Cancelled jobs are retained for 7 days
- Users can download results via the API during the retention period