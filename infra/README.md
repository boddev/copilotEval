# CopilotEval Infrastructure

This directory contains Azure Bicep templates and deployment scripts for the CopilotEval application infrastructure.

## Overview

The infrastructure provisions the following Azure resources:

- **Service Bus**: Message queues for asynchronous job processing based on JobMessage schema
- **Storage Account**: Blob storage for file uploads, job data, and large payloads
- **SQL Database**: Relational database for job tracking and results storage
- **Key Vault**: Secure storage for connection strings, API keys, and secrets
- **Function App**: Serverless compute for job processing workers
- **App Service Plan**: Hosting plan for Function App
- **Application Insights & Log Analytics**: Monitoring and observability (staging/prod only)

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Frontend      │    │   Backend API   │    │  Function App   │
│   (React)       │────│   (.NET Core)   │────│  (Job Worker)   │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                │                        │
                                │                        │
                       ┌─────────────────┐    ┌─────────────────┐
                       │   Key Vault     │    │  Service Bus    │
                       │   (Secrets)     │    │  (Queues)       │
                       └─────────────────┘    └─────────────────┘
                                │                        │
                                │                        │
                       ┌─────────────────┐    ┌─────────────────┐
                       │  SQL Database   │    │  Blob Storage   │
                       │  (Job Data)     │    │  (Files/Data)   │
                       └─────────────────┘    └─────────────────┘
```

## File Structure

```
infra/
├── main.bicep                    # Main template orchestrator
├── modules/                      # Modular Bicep templates
│   ├── keyvault.bicep           # Key Vault configuration
│   ├── servicebus.bicep         # Service Bus namespace and queues
│   ├── storage.bicep            # Storage account and containers
│   ├── database.bicep           # SQL Server and database
│   ├── function-app.bicep       # Function App and App Service Plan
│   └── monitoring.bicep         # Application Insights and Log Analytics
├── parameters/                   # Environment-specific parameters
│   ├── dev.parameters.json      # Development environment
│   ├── staging.parameters.json  # Staging environment
│   └── prod.parameters.json     # Production environment
├── deploy.sh                    # Deployment script
├── smoke-tests.sh               # Infrastructure validation tests
└── README.md                    # This file
```

## Prerequisites

1. **Azure CLI**: Install and configure Azure CLI
   ```bash
   # Install Azure CLI (if not already installed)
   curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
   
   # Login to Azure
   az login
   ```

2. **Azure Subscription**: Ensure you have appropriate permissions to create resources

3. **Resource Group**: Create or identify the target resource group

## Quick Start

### 1. Deploy to Development Environment

```bash
# Navigate to the infra directory
cd infra

# Deploy to development environment
./deploy.sh \
  --environment dev \
  --resource-group rg-copiloteval-dev \
  --subscription 12345678-1234-1234-1234-123456789012
```

### 2. Validate Infrastructure

```bash
# Run smoke tests to verify deployment
./smoke-tests.sh \
  --environment dev \
  --resource-group rg-copiloteval-dev \
  --subscription 12345678-1234-1234-1234-123456789012
```

### 3. Update Secrets

After deployment, update Key Vault secrets with actual values:

```bash
# Set SQL admin password
az keyvault secret set \
  --vault-name copiloteval-kv-dev-xxxx \
  --name sql-admin-password \
  --value "YourSecurePassword123!"

# Set Copilot API key
az keyvault secret set \
  --vault-name copiloteval-kv-dev-xxxx \
  --name copilot-api-key \
  --value "your-copilot-api-key"
```

## Environment-Specific Deployments

### Development
- **Purpose**: Development and testing
- **Features**: Basic monitoring, lower costs
- **Service Bus**: Basic tier, smaller queues
- **SQL**: S1 tier, 20GB storage
- **Storage**: LRS redundancy
- **Monitoring**: Disabled

```bash
./deploy.sh -e dev -g rg-copiloteval-dev -s <subscription-id>
```

### Staging
- **Purpose**: Pre-production testing
- **Features**: Full monitoring, production-like configuration
- **Service Bus**: Standard tier, larger queues
- **SQL**: S1 tier, 20GB storage
- **Storage**: LRS redundancy
- **Monitoring**: Enabled

```bash
./deploy.sh -e staging -g rg-copiloteval-staging -s <subscription-id>
```

### Production
- **Purpose**: Live workload
- **Features**: High availability, security, monitoring
- **Service Bus**: Standard tier, partitioned queues
- **SQL**: S2 tier, 100GB storage, geo-redundant backup
- **Storage**: GRS redundancy
- **Monitoring**: Enabled with alerting

```bash
./deploy.sh -e prod -g rg-copiloteval-prod -s <subscription-id>
```

## Service Bus Configuration

The Service Bus is configured to handle JobMessage schema requirements:

### Queues

1. **job-messages**: Main queue for job lifecycle messages
   - Messages: JobCreated, JobStarted, JobProgress, JobCompleted, JobFailed, JobCancelled
   - Lock Duration: 5 minutes
   - Max Delivery Count: 10
   - Dead Letter on Expiration: Yes

2. **job-results**: Queue for job result processing
   - Lock Duration: 1 minute
   - Max Delivery Count: 5
   - Dead Letter on Expiration: Yes

### Topic

1. **job-events**: Topic for pub/sub notifications
   - Subscriptions: webhook-notifications, monitoring
   - Filters: Only completed/failed/cancelled events for webhooks

## Storage Configuration

### Blob Containers

- **job-data**: CSV files and input data
- **results**: Job results and evaluation output
- **uploads**: User-uploaded files
- **large-payloads**: BlobReference data for oversized messages

### Tables

- **jobs**: Job metadata and status tracking
- **jobresults**: Detailed evaluation results
- **jobprogress**: Progress tracking information

### Queues (Backup)

- **deadletter**: Dead letter messages from Service Bus
- **retry**: Messages scheduled for retry

## SQL Database Schema

The SQL database stores:

- Job metadata and configuration
- User information and authentication
- Job results and analytics
- System audit logs

Key tables align with the JobMessage and Job models defined in `backend/Models/JobMessage.cs`.

## Security Configuration

### Key Vault Secrets

- `sql-admin-password`: SQL Server administrator password
- `copilot-api-key`: Microsoft Copilot API key
- `function-app-key`: Function App authentication key
- `storage-connection-string`: Storage account connection string
- `servicebus-connection-string`: Service Bus connection string
- `sql-connection-string`: SQL database connection string

### Access Control

- Function App has Managed Identity access to Key Vault (read secrets)
- Service Bus uses connection strings for authentication
- SQL Database uses SQL authentication (can be upgraded to Azure AD)
- Storage Account uses account keys (can be upgraded to RBAC)

## Monitoring and Alerting

### Application Insights

Tracks:
- Request performance and availability
- Custom events for job lifecycle
- Exception tracking and debugging
- User analytics and usage patterns

### Log Analytics

Centralized logging for:
- Application logs from Function App
- Azure resource diagnostic logs
- Custom queries and dashboards

### Alerts

- High error rate (> 10 errors in 15 minutes)
- High response time (> 5 seconds average)
- Low availability (< 95%)

## Troubleshooting

### Common Issues

1. **Template Validation Errors**
   ```bash
   # Validate template without deploying
   ./deploy.sh -e dev -g rg-test -s <sub-id> --validate-only
   ```

2. **Permission Errors**
   - Ensure you have Contributor role on the subscription
   - Check Azure AD permissions for Key Vault access

3. **Resource Name Conflicts**
   - Resources use unique suffixes to avoid conflicts
   - If issues persist, try a different resource group

4. **Smoke Test Failures**
   - Wait a few minutes after deployment for resources to stabilize
   - Check Azure portal for any failed resource deployments

### Debugging Commands

```bash
# Check deployment status
az deployment group list --resource-group <rg-name> --output table

# View specific deployment details
az deployment group show --resource-group <rg-name> --name <deployment-name>

# Check resource status
az resource list --resource-group <rg-name> --output table

# View Function App logs
az functionapp log tail --name <function-app-name> --resource-group <rg-name>
```

## Cost Optimization

### Development
- Use Basic tier Service Bus
- Use S1 SQL Database
- Disable monitoring features
- Use LRS storage redundancy

### Production
- Consider Reserved Instances for SQL Database
- Use autoscaling for Function App
- Monitor and optimize storage costs
- Review Application Insights data retention

## Compliance and Governance

### Data Protection
- Encryption at rest enabled for all services
- TLS 1.2 minimum for all communications
- Soft delete enabled for Key Vault and Storage

### Backup and Recovery
- SQL Database automated backups (7-35 days)
- Blob storage soft delete (7-365 days)
- Key Vault soft delete enabled

### Auditing
- SQL Database auditing enabled
- Azure Activity Log integration
- Application Insights telemetry

## Next Steps

After successful deployment:

1. **Deploy Application Code**
   - Deploy Function App code for job processing
   - Update API configuration to use new resources

2. **Configure Monitoring**
   - Set up custom dashboards in Application Insights
   - Configure alert notification channels
   - Test alert rules

3. **Security Hardening**
   - Implement network restrictions if needed
   - Configure Azure AD authentication
   - Review and update access policies

4. **Performance Testing**
   - Load test the infrastructure
   - Optimize queue settings based on usage
   - Tune SQL Database performance settings

## Support

For issues with the infrastructure templates:

1. Check the troubleshooting section above
2. Review Azure deployment logs in the portal
3. Run validation tests: `./deploy.sh --validate-only`
4. Run smoke tests: `./smoke-tests.sh`

For application-specific issues, refer to the main README.md in the repository root.