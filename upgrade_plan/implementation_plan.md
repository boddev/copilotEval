# CopilotEval Upgrade - Implementation Plan

## Summary

Transform CopilotEval into a scalable job processing platform. The frontend will submit jobs; the backend will enqueue and persist them. Scalable workers will process jobs and update status; users will view job progress via a status URL and real-time updates.

## Recommended Stack

- Messaging: Azure Service Bus (recommended) or Azure Storage Queue (simpler)
- Compute: .NET 9 backend API (existing) + .NET Worker Service or Azure Functions for consumers
- Storage: Azure SQL or Cosmos DB for job metadata; Azure Blob Storage for large job artifacts
- Auth: Azure AD + Managed Identities
- Observability: OpenTelemetry + Azure Application Insights
- IaC: Bicep templates
- CI/CD: GitHub Actions + environment-specific pipelines

## Detailed Tasks for Coding Agents

1) Design & API Contracts (Backend) - 2-3 days
   - Deliverables: OpenAPI spec, sample payloads, JobMessage JSON schema
   - Files to add/update: backend/Models/JobMessage.cs, backend/Controllers/JobsController.cs, backend/Properties/launchSettings.json
   - Acceptance criteria: OpenAPI validates; sample payload test passes

2) Infrastructure Templates (Infra) - 2-4 days
   - Deliverables: Bicep templates for Service Bus, DB, Storage, Function App, Key Vault
   - Files: infra/main.bicep, infra/servicebus.bicep, infra/database.bicep, infra/keyvault.bicep
   - Acceptance criteria: Templates deployable to a new resource group and pass smoke tests

3) Backend Producer (Backend) - 3-5 days
   - Tasks: Implement POST /api/jobs to validate request, persist job (status=Queued), enqueue JobMessage to Service Bus, return {jobId, statusUrl}
   - Files: backend/Controllers/JobsController.cs, backend/Services/JobQueueService.cs, backend/Repositories/JobRepository.cs
   - Acceptance criteria: Postman test enqueues message and job persists

4) Worker Consumers (Worker) - 4-7 days
   - Tasks: Implement Service Bus triggered Azure Function or .NET Worker Service that processes messages, updates job status, writes artifacts, and handles retries/DLQ
   - Files: worker/JobProcessor.cs (or functions/JobProcessor.cs), worker/Services/ExecutionService.cs
   - Acceptance criteria: Worker picks up message, processes and updates status from Queued -> Running -> Succeeded/Failed

5) Job Status Store & APIs (Backend) - 2-4 days
   - Tasks: Implement JobRepository, GET /api/jobs/{id}, GET /api/jobs (filters, pagination), provide status URL
   - Files: backend/Repositories/JobRepository.cs, backend/Controllers/JobsController.cs
   - Acceptance criteria: API returns correct job lifecycle state and results

6) Frontend Job Engine & UI (Frontend) - 3-5 days
   - Tasks: Job submission form, job list, job status page, SignalR client (optional) for real-time updates, snackbar notifications
   - Files: frontend/src/components/JobSubmission.tsx, frontend/src/components/JobList.tsx, frontend/src/components/JobStatus.tsx, frontend/src/services/jobService.ts
   - Acceptance criteria: User can submit job and click status URL to see progress and results

7) Observability & Tracing (Ops) - 2-3 days
   - Tasks: Instrument backend, worker, and frontend with OpenTelemetry, export to App Insights; add logs, metrics, and alerts
   - Files: backend/Observability/Telemetry.cs, worker/Observability, infra/monitoring.bicep
   - Acceptance criteria: Trace shows end-to-end flow for a job and alerts trigger on failures

8) Security & Authorization (Security) - 2-3 days
   - Tasks: Protect APIs with Azure AD, use managed identities for queue access, store secrets in Key Vault
   - Files: backend/Program.cs changes for authentication, infra/keyvault.bicep
   - Acceptance criteria: Backend and worker authenticate via managed identities; no secrets in code

9) Testing & Load Validation (QA) - 3-5 days
   - Tasks: Unit tests, integration tests, e2e tests (Playwright), load tests (k6)
   - Files: tests/backend/*, tests/frontend/*, load-tests/k6/*.js
   - Acceptance criteria: Tests pass on CI and system sustains target load

10) CI/CD & Deploy (DevOps) - 3-5 days
    - Tasks: GitHub Actions for CI (build/test) and CD (deploy infra and apps), blue/green or canary rollout
    - Files: .github/workflows/ci.yml, .github/workflows/cd.yml, infra/ci-templates
    - Acceptance criteria: Automated deployments to staging and production with rollback

11) Documentation & Runbooks (Docs) - 1-2 days
    - Tasks: Update README, architecture docs, runbooks for operators, Postman collections
    - Files: docs/architecture.md, docs/runbook.md, docs/postman-collection.json
    - Acceptance criteria: On-call engineer can follow runbook to recover from failures

## Message Schema (JobMessage)

```json
{
  "jobId": "guid",
  "userId": "string",
  "createdAt": "2025-09-15T00:00:00Z",
  "type": "string",
  "payload": { /* arbitrary json for job */ },
  "priority": "normal",
  "selectedKnowledgeSource": "string|null",
  "additionalInstructions": "string|null",
  "callbackUrl": "string|null"
}
```

## Job Lifecycle

- Queued: Job recorded and enqueued
- Running: Worker picked up job and started processing
- Succeeded: Job completed and result stored
- Failed: Job failed and error recorded
- Dead-lettered: Multiple failures, moved to DLQ for manual inspection

## Operational Considerations

- Idempotency: Workers must handle repeated messages
- Retries: Exponential backoff; move to DLQ after N attempts
- Message Size: Keep JobMessage small; store large payloads in Blob Storage and pass blobs references

## Next Steps

- Validate architecture decisions with stakeholders
- Prioritize tasks and assign to teams/agents
- Begin implementation with Design & API Contracts

