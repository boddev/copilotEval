CopilotEval Upgrade Analysis Report

Overview

This document analyzes the current CopilotEval project and presents a detailed implementation plan to expand its scope to a scalable job submission and processing platform. The goal is to enable the frontend to submit jobs via a queue (Azure Service Bus or Storage Queue), have a scalable backend consume those jobs, and provide users with real-time status and result links.

Current State

- Monolith backend: .NET 9 Minimal API running as a single process (CopilotEvalApi)
- Frontend: React + TypeScript (Vite), running locally on port 3000, communicates with backend via REST
- Graph and Copilot integrations: existing services that call Microsoft Graph and M365 Copilot APIs
- Local development: dotnet watch for backend, npm dev server for frontend

Requirements & Goals

1. Job submission from frontend via queue
2. Scalable background consumers to process jobs
3. Persistent job status tracking with query APIs
4. Real-time status updates per job via status URL and optional SignalR
5. Observability: distributed tracing, metrics, structured logs
6. Secure credentials: Azure AD, managed identities, Key Vault
7. IaC & CI/CD for reproducible deployments
8. Testing & load validation

Architecture Options

1. Azure Service Bus (recommended):
   - Pros: Advanced messaging features, topics/subscriptions, dead-letter, sessions
   - Cons: Slightly higher cost than Storage Queue

2. Azure Storage Queue:
   - Pros: Simpler and cheaper
   - Cons: Fewer features (no topics, sessions)

3. Other options: Kafka/Event Hubs for high throughput

High-level Architecture

- Frontend: Job Submission UI -> POST /api/jobs -> Backend enqueues JobMessage
- Backend API: Validates job request, persists Job entity (status: queued), enqueues JobMessage
- Workers: Scalable .NET Worker Services or Azure Functions pick up JobMessage, set status to running, execute job, persist results, emit events
- JobStatus Store: SQL Server or Cosmos DB to store job metadata and results
- Notification: SignalR or WebSockets for real-time UI updates; fallback to polling via GET /api/jobs/{id}
- Monitoring: Application Insights/OpenTelemetry for distributed tracing

Non-functional concerns

- Security: OAuth 2.0 / Azure AD
- Scalability: Autoscale rules for worker roles based on queue length & CPU
- Reliability: DLQ handling, retries with exponential backoff
- Observability: Correlate requests using traceparent headers

Implementation Phases

Phase 1: Design & API Contracts
- Define JobMessage schema
- Define REST endpoints and payloads
- Create OpenAPI spec

Phase 2: Infrastructure (IaC)
- Create Bicep templates: Service Bus, SQL DB/Cosmos, App Service/Functions, Key Vault

Phase 3: Backend Producer
- Implement /api/jobs endpoint, queue producer, job persistence

Phase 4: Worker Consumers
- Implement scalable worker using Azure Functions or .NET Worker Service with Service Bus trigger

Phase 5: Frontend
- Add Job submission components, Job list, Job status page, and SignalR client

Phase 6: Observability & Security
- Add OpenTelemetry/Azure Application Insights, managed identities, Key Vault

Phase 7: Testing & Deployment
- CI/CD GitHub Actions pipelines, unit/integration tests, load tests

Acceptance Criteria

- Jobs can be submitted from frontend and enqueued
- Workers process jobs and update job status
- Users can see status via status URL and receive updates
- System exhibits expected scaling behavior under load


