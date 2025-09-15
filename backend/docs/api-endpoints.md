# API Endpoints Documentation

## Overview

This document provides detailed technical specifications for all REST API endpoints in the Copilot Evaluation platform, including the new job lifecycle endpoints and existing functionality.

## Base Configuration

**Base URL:** `/api`  
**Content-Type:** `application/json`  
**Authentication:** Bearer token via `Authorization` header  
**Tracing:** W3C Trace Context via `traceparent` header

## Authentication Endpoints

### GET /api/auth/url

Generate OAuth authentication URL for M365 Copilot access.

**Parameters:**
- `redirectUri` (query, optional): OAuth redirect URI (default: `http://localhost:5173`)

**Request Example:**
```http
GET /api/auth/url?redirectUri=http://localhost:3000
```

**Response (200 OK):**
```json
{
  "authUrl": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?...",
  "state": "unique-state-identifier"
}
```

**Error Response:**
- `400 Bad Request`: Invalid redirect URI format

### POST /api/auth/token

Exchange OAuth authorization code for access token.

**Request Body:**
```json
{
  "code": "authorization-code-from-oauth-flow",
  "state": "state-from-auth-url-response",
  "redirectUri": "http://localhost:3000"
}
```

**Response (200 OK):**
```json
{
  "accessToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIs...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "refreshToken": "MCRlYzU4Y2YxLWZkNzYtNGRhNS1iNzA1..."
}
```

**Error Response:**
- `400 Bad Request`: Invalid authorization code or state

## Health Check Endpoints

### GET /api/health

Simple health check endpoint.

**Response (200 OK):**
```json
{
  "status": "Healthy",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

## Copilot Integration Endpoints

### GET /api/copilot/agents

Retrieve installed Copilot agents/plugins.

**Headers:**
- `Authorization: Bearer <access_token>` (required)

**Query Parameters:**
- `accessToken` (query): Access token (alternative to header)

**Response (200 OK):**
```json
{
  "value": [
    {
      "id": "agent-id-123",
      "externalId": "com.example.agent",
      "displayName": "Custom Business Agent",
      "distributionMethod": "store",
      "shortDescription": "Helps with business queries",
      "description": "A comprehensive agent for business intelligence",
      "version": "1.0.0",
      "isBlocked": false,
      "publishingState": "published"
    }
  ]
}
```

**Error Responses:**
- `400 Bad Request`: Missing access token
- `401 Unauthorized`: Invalid access token

### GET /api/copilot/knowledge-sources

Retrieve external knowledge sources.

**Headers:**
- `Authorization: Bearer <access_token>` (required)

**Query Parameters:**
- `accessToken` (query): Access token (alternative to header)

**Response (200 OK):**
```json
{
  "value": [
    {
      "id": "connection-id-456",
      "name": "SharePoint Knowledge Base",
      "description": "Company knowledge base from SharePoint",
      "state": "ready",
      "configuration": {
        "authorizedAppIds": ["app-id-789"]
      }
    }
  ]
}
```

**Error Responses:**
- `400 Bad Request`: Missing access token
- `403 Forbidden`: Insufficient permissions
- `401 Unauthorized`: Invalid access token

### POST /api/copilot/chat

Send chat request to M365 Copilot.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Request Body:**
```json
{
  "prompt": "What is the company's revenue for Q4 2023?",
  "conversationId": "conv-abc123",
  "accessToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIs...",
  "timeZone": "UTC",
  "selectedAgentId": null,
  "additionalInstructions": "Focus on financial accuracy",
  "selectedKnowledgeSource": "connection-id-456"
}
```

**Response (200 OK):**
```json
{
  "response": "Based on the financial data, the company's Q4 2023 revenue was $2.5 billion...",
  "success": true,
  "error": null,
  "conversationId": "conv-abc123",
  "attributions": [
    {
      "attributionType": "file",
      "providerDisplayName": "SharePoint",
      "attributionSource": "Q4-2023-Financial-Report.pdf",
      "seeMoreWebUrl": "https://company.sharepoint.com/documents/...",
      "imageWebUrl": null,
      "imageFavIcon": "https://sharepoint.com/favicon.ico",
      "imageWidth": 32,
      "imageHeight": 32
    }
  ]
}
```

**Error Responses:**
- `400 Bad Request`: Missing required fields or invalid access token
- `500 Internal Server Error`: M365 Copilot API error

## Similarity Scoring Endpoints

### POST /api/similarity/score

Calculate semantic similarity between expected and actual responses using Copilot.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Request Body:**
```json
{
  "expected": "The revenue for Q4 2023 was $2.5B",
  "actual": "Q4 2023 revenue totaled $2.5 billion",
  "accessToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIs...",
  "additionalInstructions": "Focus on semantic meaning over exact wording",
  "selectedKnowledgeSource": null
}
```

**Response (200 OK):**
```json
{
  "score": 0.95,
  "success": true,
  "error": null,
  "reasoning": "Both responses convey the same information with minor wording differences",
  "differences": "Expected uses '$2.5B' while actual uses '$2.5 billion'"
}
```

**Error Responses:**
- `400 Bad Request`: Missing required fields or invalid access token
- `500 Internal Server Error`: Copilot API error during evaluation

## Job Lifecycle Endpoints

### POST /api/jobs

Submit a new evaluation job for processing.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `Idempotency-Key: <unique-key>` (optional but recommended)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Request Body:**
```json
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

**Response Headers:**
```
X-RateLimit-Limit: 10
X-RateLimit-Remaining: 9
X-RateLimit-Reset: 1705321860
Location: /api/jobs/job_abc123def456
```

**Error Responses:**
- `400 Bad Request`: Invalid request body or configuration
- `401 Unauthorized`: Missing or invalid access token
- `429 Too Many Requests`: Rate limit exceeded (returns `Retry-After` header)
- `500 Internal Server Error`: Server error

### GET /api/jobs

Retrieve list of jobs with filtering and pagination.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Query Parameters:**
- `status` (optional): Filter by status (`pending`, `running`, `completed`, `failed`, `cancelled`)
- `type` (optional): Filter by type (`bulk_evaluation`, `single_evaluation`)
- `page` (optional): Page number (default: 1, min: 1)
- `limit` (optional): Items per page (default: 20, min: 1, max: 100)
- `sort` (optional): Sort field (`created_at`, `updated_at`, `name`)
- `order` (optional): Sort order (`asc`, `desc`, default: `desc`)

**Request Example:**
```http
GET /api/jobs?status=running&type=bulk_evaluation&page=1&limit=20&sort=created_at&order=desc
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIs...
```

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

**Response Headers:**
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1705321860
```

**Error Responses:**
- `400 Bad Request`: Invalid query parameters
- `401 Unauthorized`: Missing or invalid access token
- `429 Too Many Requests`: Rate limit exceeded

### GET /api/jobs/{id}

Retrieve detailed information about a specific job.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Path Parameters:**
- `id` (required): Job identifier (format: `job_[a-zA-Z0-9]{12}`)

**Response (200 OK) - Completed Job:**
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

**Response (200 OK) - Failed Job:**
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

### POST /api/jobs/{id}/cancel

Cancel a running or pending job.

**Headers:**
- `Authorization: Bearer <access_token>` (required)
- `Idempotency-Key: <unique-key>` (optional but recommended)
- `traceparent: 00-{trace-id}-{parent-id}-{trace-flags}` (optional)

**Path Parameters:**
- `id` (required): Job identifier

**Request Body:**
```json
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

## Debug Endpoints

### POST /api/debug/json

Debug endpoint for testing JSON serialization.

**Request Body:**
```json
{
  "prompt": "Test prompt",
  "additionalInstructions": "Test instructions",
  "timeZone": "UTC"
}
```

**Response (200 OK):**
```json
{
  "generatedJson": "{\n  \"request\": {\n    \"message\": {\n      \"text\": \"Test instructions\\n\\nTest prompt\"\n    },\n    \"locationHint\": {\n      \"timeZone\": \"UTC\"\n    }\n  }\n}"
}
```

## Common Response Headers

All responses include the following headers:

```
Content-Type: application/json
X-RateLimit-Limit: <limit>
X-RateLimit-Remaining: <remaining>
X-RateLimit-Reset: <reset-timestamp>
X-Request-ID: <unique-request-id>
```

For job endpoints, additional headers may include:

```
Location: /api/jobs/<job-id>  (for POST /api/jobs)
Retry-After: <seconds>        (for 429 responses)
```

## Error Response Format

All error responses follow this consistent format:

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

## Rate Limiting Details

### Limits by Endpoint Category

- **Authentication**: 30 requests per minute
- **General API**: 100 requests per minute
- **Job submission**: 10 requests per minute
- **Job queries**: 60 requests per minute
- **Copilot chat**: 20 requests per minute

### Rate Limit Headers

```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1705321860
X-RateLimit-Window: 60
```

When rate limited (429 response):

```
Retry-After: 60
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1705321860
```

## Idempotency Behavior

For endpoints supporting idempotency (job creation and cancellation):

1. Include `Idempotency-Key` header with unique identifier
2. Duplicate requests return same response (same status code and body)
3. Idempotency keys are valid for 24 hours
4. Failed requests can be retried with the same key
5. Keys should be UUIDs or similar unique identifiers

**Example:**
```http
POST /api/jobs
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

Subsequent requests with the same key return the original response without creating duplicate resources.

## Tracing Support

Requests support W3C Trace Context for distributed tracing:

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```

Format: `00-{trace-id}-{parent-id}-{trace-flags}`

- `trace-id`: 32-character lowercase hex string
- `parent-id`: 16-character lowercase hex string  
- `trace-flags`: 2-character hex string (typically "01" for sampled)

The trace ID is included in error responses and can be used for support requests.