# CI/CD Pipeline Environment Configuration

This document outlines the environment-specific configuration for the CopilotEval CI/CD pipeline.

## 🌍 Environment Overview

The CI/CD pipeline supports three environments:
- **Development** (`dev`) - Local development and testing
- **Staging** (`staging`) - Pre-production testing and validation  
- **Production** (`prod`) - Live production environment

## 🔧 Environment Configuration

### GitHub Repository Secrets

The following secrets must be configured in GitHub repository settings:

#### Required Secrets
```yaml
# Azure Authentication
AZURE_CLIENT_ID: "azure-service-principal-client-id"
AZURE_TENANT_ID: "azure-tenant-id" 
AZURE_SUBSCRIPTION_ID: "azure-subscription-id"
AZURE_CLIENT_SECRET: "azure-service-principal-secret"  # Only for legacy auth if needed

# Optional: Environment-specific secrets
STAGING_DATABASE_CONNECTION: "staging-database-connection-string"
PROD_DATABASE_CONNECTION: "production-database-connection-string"
COPILOT_API_KEY: "copilot-api-key"
```

#### Service Principal Setup
```bash
# Create service principal for CI/CD
az ad sp create-for-rbac \
  --name "copiloteval-cicd" \
  --role "Contributor" \
  --scopes "/subscriptions/YOUR_SUBSCRIPTION_ID" \
  --sdk-auth

# Assign additional roles
az role assignment create \
  --assignee "SERVICE_PRINCIPAL_ID" \
  --role "Key Vault Secrets Officer" \
  --scope "/subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/rg-copiloteval-*"
```

### Environment Variables

#### CI Pipeline (.github/workflows/ci.yml)
```yaml
env:
  DOTNET_VERSION: '8.x'          # .NET version for backend
  NODE_VERSION: '20.x'           # Node.js version for frontend
```

#### CD Pipeline (.github/workflows/cd.yml)
```yaml
env:
  DOTNET_VERSION: '8.x'
  NODE_VERSION: '20.x'
  AZURE_RESOURCE_GROUP_STAGING: 'rg-copiloteval-staging'
  AZURE_RESOURCE_GROUP_PROD: 'rg-copiloteval-prod'
```

## 🏗️ Infrastructure Configuration

### Resource Groups
```yaml
Development:
  resource_group: "rg-copiloteval-dev"
  location: "East US 2"
  
Staging:
  resource_group: "rg-copiloteval-staging" 
  location: "East US 2"
  
Production:
  resource_group: "rg-copiloteval-prod"
  location: "East US 2"
```

### App Service Configuration

#### Staging Environment
```yaml
app_service_name: "copiloteval-staging"
app_service_plan: "copiloteval-staging-plan"
sku: "S1"
deployment_slots:
  - staging  # For blue-green deployments
  - production
```

#### Production Environment  
```yaml
app_service_name: "copiloteval"
app_service_plan: "copiloteval-prod-plan"
sku: "P1V2"
deployment_slots:
  - staging  # For blue-green deployments
  - production
```

## 🔐 Security Configuration

### GitHub Environment Protection Rules

#### Staging Environment
```yaml
name: staging
protection_rules:
  required_reviewers: 0
  wait_timer: 0  # No wait time
  deployment_branches:
    - main
```

#### Production Environment
```yaml
name: production
protection_rules:
  required_reviewers: 2        # Require 2 approvals
  wait_timer: 5               # 5 minute wait time
  deployment_branches:
    - main                    # Only deploy from main branch
  environment_secrets:
    - PROD_DATABASE_CONNECTION
    - PROD_API_KEYS
```

### Azure RBAC Configuration

#### CI/CD Service Principal Permissions
```bash
# Minimum required permissions
az role assignment create \
  --assignee $AZURE_CLIENT_ID \
  --role "Contributor" \
  --resource-group "rg-copiloteval-staging"

az role assignment create \
  --assignee $AZURE_CLIENT_ID \
  --role "Contributor" \
  --resource-group "rg-copiloteval-prod"

# Key Vault access
az role assignment create \
  --assignee $AZURE_CLIENT_ID \
  --role "Key Vault Secrets Officer" \
  --scope "/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/rg-copiloteval-*/providers/Microsoft.KeyVault/vaults/*"
```

## 🚀 Deployment Strategy

### Staging Deployment (Automatic)
- **Trigger**: Push to `main` branch
- **Strategy**: Blue-green deployment
- **Approval**: None required
- **Rollback**: Automatic on health check failure
- **Validation**: Smoke tests + integration tests

### Production Deployment (Manual)
- **Trigger**: Manual workflow dispatch
- **Strategy**: Blue-green deployment  
- **Approval**: 2 reviewers required
- **Rollback**: Manual or automatic on critical failure
- **Validation**: Full test suite + smoke tests

## 📊 Monitoring Configuration

### Application Insights
```yaml
Staging:
  name: "copiloteval-staging-insights"
  retention: 90  # days
  
Production:
  name: "copiloteval-prod-insights"
  retention: 365  # days
```

### Alert Rules
```yaml
Deployment Alerts:
  - Deployment failure
  - Health check failure
  - High error rate (>5%)
  - Response time degradation (>2s)

Infrastructure Alerts:
  - High CPU usage (>80%)
  - High memory usage (>85%)
  - Disk space low (<10%)
  - Database connection failures
```

## 🔄 Workflow Configuration

### Branch Protection Rules
```yaml
main_branch:
  protection_rules:
    - require_status_checks: true
    - required_status_checks:
        - "CI Status Check"
        - "frontend-build"
        - "backend-build"
        - "contract-tests"
        - "security-scan"
    - require_pull_request_reviews: true
    - required_approving_review_count: 1
    - dismiss_stale_reviews: true
    - require_code_owner_reviews: true
    - restrict_pushes: true
    - allow_force_pushes: false
    - allow_deletions: false
```

### Workflow Permissions
```yaml
ci_workflow:
  permissions:
    contents: read
    pull-requests: write
    security-events: write
    
cd_workflow:
  permissions:
    contents: read
    id-token: write
    deployments: write
```

## 🛠️ Local Development Setup

### Environment Variables for Local Development
```bash
# .env file for local development
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection="Server=localhost;Database=CopilotEval;Trusted_Connection=true;"
export CopilotApiKey="your-local-api-key"
export Azure__KeyVault__VaultUri="https://copiloteval-kv-dev.vault.azure.net/"
```

### Local Database Setup
```bash
# Setup local SQL Server for development
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourPassword123!" \
  -p 1433:1433 --name sqlserver \
  -d mcr.microsoft.com/mssql/server:2022-latest

# Run database migrations
dotnet ef database update --project backend/CopilotEvalApi.csproj
```

## 📋 Configuration Checklist

### Initial Setup
- [ ] Create Azure service principal
- [ ] Configure GitHub repository secrets
- [ ] Set up environment protection rules
- [ ] Configure branch protection rules
- [ ] Deploy infrastructure to all environments
- [ ] Verify health endpoints in all environments
- [ ] Set up monitoring and alerting
- [ ] Document rollback procedures
- [ ] Train team on deployment processes

### Regular Maintenance
- [ ] Rotate service principal credentials (quarterly)
- [ ] Review and update protection rules (monthly)
- [ ] Update dependency versions (monthly)
- [ ] Review monitoring and alert rules (monthly)
- [ ] Test rollback procedures (quarterly)

---

**Last Updated**: [Date]
**Document Owner**: Platform Team
**Review Cycle**: Monthly