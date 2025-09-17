# CopilotEval Architecture Documentation

## System Overview

CopilotEval is a distributed evaluation platform for testing and validating Large Language Model (LLM) responses against expected outputs. The system processes evaluation jobs asynchronously using a microservices architecture built on Azure cloud services.

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           CopilotEval System                             │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Frontend      │    │   Backend API   │    │  Function App   │
│   (React)       │────│   (.NET 9)      │────│  (Worker)       │
│                 │    │                 │    │                 │
│ • File Upload   │    │ • Job Mgmt      │    │ • Job Processor │
│ • Validation UI │    │ • Auth/AuthZ    │    │ • Copilot API   │
│ • Results View  │    │ • Rate Limiting │    │ • Evaluation    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │   Azure         │
                    │   Infrastructure│
                    └─────────────────┘
                             │
    ┌────────────────────────┼────────────────────────┐
    │                        │                        │
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  Service Bus    │ │   Key Vault     │ │  SQL Database   │
│                 │ │                 │ │                 │
│ • job-messages  │ │ • API Keys      │ │ • Job Metadata  │
│ • job-results   │ │ • Secrets       │ │ • User Data     │
│ • Dead Letter   │ │ • Certificates  │ │ • Results       │
└─────────────────┘ └─────────────────┘ └─────────────────┘
         │                       │                       │
         │                       │                       │
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │  Blob Storage   │ │   Monitoring    │ │   External APIs │
    │                 │ │                 │ │                 │
    │ • CSV Files     │ │ • App Insights  │ │ • Copilot Chat  │
    │ • Results       │ │ • Log Analytics │ │ • Graph Search  │
    │ • Large Payloads│ │ • Alerts        │ │ • Azure AD      │
    └─────────────────┘ └─────────────────┘ └─────────────────┘
```

## Component Architecture

### 1. Frontend (React Application)

**Technology Stack:**
- React 18 with TypeScript
- Vite for build tooling
- Axios for HTTP client
- PapaParse for CSV processing

**Key Components:**
- **File Upload Component**: Handles CSV validation data uploads
- **Validation Table**: Dynamic table for managing test cases
- **Results Dashboard**: Displays evaluation results and metrics
- **Authentication**: Azure AD integration for user authentication

**Responsibilities:**
- User interface for job creation and management
- File upload and client-side validation
- Real-time status updates and results visualization
- Error handling and user feedback

### 2. Backend API (.NET 9 Minimal API)

**Technology Stack:**
- .NET 9 Minimal API
- Entity Framework Core (In-Memory for dev, SQL for production)
- Azure AD JWT Bearer Authentication
- OpenTelemetry for observability

**Key Controllers:**
- **JobsController**: Job lifecycle management (CRUD operations)
- **AuthController**: Authentication and authorization
- **HealthController**: System health checks

**Key Services:**
- **JobQueueService**: Service Bus integration for async messaging
- **CopilotService**: Integration with Copilot Chat API
- **GraphSearchService**: Azure Cognitive Search integration
- **ExecutionService**: Job execution orchestration
- **JobProcessor**: Core evaluation logic

**Responsibilities:**
- API endpoints for job management
- Authentication and authorization
- Request validation and rate limiting
- Message queuing for async processing
- Database operations and data persistence

### 3. Worker (Azure Function App)

**Technology Stack:**
- Azure Functions (.NET 8)
- Service Bus Triggers
- Application Insights integration
- Blob Storage SDK

**Key Functions:**
- **JobProcessor**: Processes job messages from Service Bus
- **CopilotEvaluator**: Executes LLM evaluations
- **ResultsProcessor**: Aggregates and stores results

**Responsibilities:**
- Asynchronous job processing
- Copilot API integration and evaluation
- Similarity scoring and validation
- Progress tracking and status updates
- Error handling and retry logic

### 4. Azure Infrastructure

**Core Services:**

**Service Bus:**
- **job-messages queue**: Incoming job processing requests
- **job-results queue**: Completed evaluation results
- **Dead Letter Queue**: Failed message handling

**SQL Database:**
- **Jobs table**: Job metadata and configuration
- **JobResults table**: Evaluation results and metrics
- **Users table**: User authentication data

**Blob Storage:**
- **job-data container**: Input CSV files and datasets
- **results container**: Output files and detailed results
- **uploads container**: User-uploaded content
- **large-payloads container**: Oversized message payloads

**Key Vault:**
- API keys and secrets management
- Certificate storage
- Connection strings and sensitive configuration

**Monitoring:**
- **Application Insights**: Telemetry and performance monitoring
- **Log Analytics**: Centralized logging and querying
- **Alerts**: Automated incident detection and notification

## Data Flow Architecture

### Job Submission Flow

```
1. User uploads CSV → Frontend validation
2. Frontend → POST /api/jobs → Backend API
3. Backend validates → Saves to DB → Returns job ID
4. Backend → Enqueues JobCreated message → Service Bus
5. Worker receives message → Begins processing
6. Worker → Updates progress → Service Bus (job-results)
7. Backend listens → Updates DB → Notifies Frontend
```

### Message Processing Flow

```
┌─────────────────┐
│ JobCreated      │
│ Message         │
└─────────┬───────┘
          │
          ▼
┌─────────────────┐
│ Worker Function │
│ • Parse config  │
│ • Load data     │
│ • Start eval    │
└─────────┬───────┘
          │
          ▼
┌─────────────────┐
│ Evaluation Loop │
│ • Call Copilot  │
│ • Score results │
│ • Track progress│
└─────────┬───────┘
          │
          ▼
┌─────────────────┐
│ Results Storage │
│ • Save to DB    │
│ • Store in Blob │
│ • Send completion│
└─────────────────┘
```

## Security Architecture

### Authentication & Authorization
- **Azure AD Integration**: Single sign-on with organizational accounts
- **JWT Bearer Tokens**: Stateless authentication for API access
- **Role-Based Access Control (RBAC)**: Different permissions for users/admins

### Data Security
- **Key Vault**: Centralized secret management
- **Managed Identity**: Secure service-to-service authentication
- **Encryption**: Data at rest and in transit encryption
- **Network Security**: Virtual network integration and private endpoints

### API Security
- **CORS Configuration**: Controlled cross-origin access
- **Rate Limiting**: Request throttling and abuse prevention
- **Input Validation**: Comprehensive request validation
- **Error Handling**: Secure error responses without information leakage

## Scalability & Performance

### Horizontal Scaling
- **Function App**: Auto-scaling based on queue depth
- **API Instances**: Load balancer distribution
- **Database**: Connection pooling and read replicas

### Performance Optimization
- **Async Processing**: Non-blocking job execution
- **Blob Storage**: Large payload handling
- **Caching**: In-memory caching for frequent data
- **Connection Pooling**: Efficient database connections

### Monitoring & Alerting
- **Custom Metrics**: Job processing rates and success rates
- **Performance Counters**: CPU, memory, and throughput monitoring
- **Health Checks**: Automated endpoint monitoring
- **Alert Rules**: Proactive incident detection

## Deployment Architecture

### Environment Strategy
- **Development**: Local development with in-memory databases
- **Staging**: Full Azure environment for integration testing
- **Production**: High-availability configuration with redundancy

### Infrastructure as Code
- **Bicep Templates**: Declarative Azure resource management
- **Environment Parameters**: Environment-specific configuration
- **Automated Deployment**: CI/CD pipeline integration

### Configuration Management
- **Key Vault**: Sensitive configuration and secrets
- **App Settings**: Environment-specific application configuration
- **Feature Flags**: Runtime feature toggling

## Technology Decisions & Rationale

### Frontend: React + TypeScript
- **Pros**: Strong ecosystem, TypeScript safety, component reusability
- **Cons**: Bundle size, complexity for simple UIs
- **Decision**: Chosen for developer productivity and maintainability

### Backend: .NET 9 Minimal API
- **Pros**: High performance, native cloud integration, strong typing
- **Cons**: Microsoft ecosystem lock-in
- **Decision**: Optimal for Azure integration and team expertise

### Worker: Azure Functions
- **Pros**: Serverless scaling, event-driven, cost-effective
- **Cons**: Cold start latency, vendor lock-in
- **Decision**: Perfect fit for asynchronous job processing

### Database: Azure SQL Database
- **Pros**: Managed service, ACID compliance, rich querying
- **Cons**: Cost for large datasets, vendor lock-in
- **Decision**: Reliability and consistency requirements

### Messaging: Azure Service Bus
- **Pros**: Enterprise messaging, dead letter handling, guaranteed delivery
- **Cons**: Cost, complexity for simple scenarios
- **Decision**: Required for reliable asynchronous processing

## Future Architecture Considerations

### Potential Enhancements
- **Microservices Decomposition**: Split monolithic API into domain services
- **Event Sourcing**: Immutable event log for audit and replay
- **CQRS**: Separate read/write models for performance
- **Container Orchestration**: Kubernetes for better resource management

### Integration Opportunities
- **Multi-Cloud**: Support for AWS/GCP deployments
- **Edge Computing**: Distribute processing geographically
- **AI/ML Pipeline**: Enhanced evaluation algorithms
- **Real-time Analytics**: Stream processing for live metrics

## References

- [Backend API Documentation](../backend/docs/api-endpoints.md)
- [Job Processing Implementation](../backend/docs/job-submission-implementation.md)
- [Infrastructure Documentation](../infra/README.md)
- [Deployment Guide](../infra/DEPLOYMENT_SUMMARY.md)