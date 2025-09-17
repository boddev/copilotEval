# Deployment Troubleshooting Guide

This guide helps diagnose and resolve common deployment issues in the CopilotEval CI/CD pipeline.

## 🔍 Common Issues and Solutions

### 1. Build Failures

#### Frontend Build Issues

**Problem**: npm install fails
```
npm ERR! code ENOTFOUND
npm ERR! errno ENOTFOUND
npm ERR! network request failed
```

**Solutions**:
```bash
# Clear npm cache
npm cache clean --force

# Use different registry
npm install --registry https://registry.npmjs.org/

# Check package-lock.json integrity
npm ci --ignore-scripts
```

**Problem**: TypeScript compilation errors
```
error TS2307: Cannot find module 'X' or its type declarations
```

**Solutions**:
```bash
# Install missing type definitions
npm install --save-dev @types/[package-name]

# Clear TypeScript cache
npx tsc --build --clean

# Verify tsconfig.json paths
```

#### Backend Build Issues

**Problem**: .NET restore fails
```
error NU1101: Unable to find package 'X'. No packages exist with this id
```

**Solutions**:
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore with verbose logging
dotnet restore --verbosity detailed

# Check NuGet.config sources
```

**Problem**: Compilation errors
```
error CS0234: The type or namespace name 'X' does not exist
```

**Solutions**:
```bash
# Clean and rebuild
dotnet clean && dotnet build

# Check project references
dotnet list reference

# Verify target framework
```

### 2. Test Failures

#### Contract Test Failures

**Problem**: OpenAPI validation fails
```
Error: OpenAPI specification is invalid
```

**Solutions**:
```bash
# Validate specific file
swagger-cli validate backend/openapi/openapi.yaml

# Check schema compliance
redocly lint backend/openapi/openapi.yaml --config .redocly.yaml

# Validate against actual API
npm run test:contract
```

#### Integration Test Failures

**Problem**: Database connection failures
```
Cannot connect to SQL Server
```

**Solutions**:
```bash
# Check connection string
echo $CONNECTION_STRING

# Test database connectivity
sqlcmd -S server -d database -U user -P password -Q "SELECT 1"

# Verify firewall rules
az sql server firewall-rule list --server-name copiloteval-sql
```

### 3. Deployment Issues

#### Azure Authentication Problems

**Problem**: Authentication failed
```
Error: Authentication failed. Please check your credentials
```

**Solutions**:
```bash
# Check service principal
az ad sp show --id $AZURE_CLIENT_ID

# Verify role assignments
az role assignment list --assignee $AZURE_CLIENT_ID

# Test authentication manually
az login --service-principal -u $AZURE_CLIENT_ID -p $AZURE_CLIENT_SECRET --tenant $AZURE_TENANT_ID
```

#### Infrastructure Deployment Failures

**Problem**: Bicep template validation fails
```
Template validation failed: The template is not valid
```

**Solutions**:
```bash
# Validate template syntax
az bicep build --file infra/main.bicep

# Check parameter file
cat infra/parameters/prod.parameters.json | jq .

# Validate deployment
az deployment group validate \
  --resource-group rg-copiloteval-prod \
  --template-file infra/main.bicep \
  --parameters @infra/parameters/prod.parameters.json
```

**Problem**: Resource creation fails
```
Resource 'X' already exists in location 'Y'
```

**Solutions**:
```bash
# Check existing resources
az resource list --resource-group rg-copiloteval-prod

# Use incremental deployment mode
az deployment group create --mode Incremental

# Clean up conflicting resources
az resource delete --ids /subscriptions/.../resourceGroups/.../providers/.../
```

#### Application Deployment Issues

**Problem**: App Service deployment timeout
```
Error: Deployment timed out after 600 seconds
```

**Solutions**:
```bash
# Check deployment logs
az webapp log tail --name copiloteval --resource-group rg-copiloteval-prod

# Verify app service plan capacity
az appservice plan show --name copiloteval-plan --resource-group rg-copiloteval-prod

# Manual deployment
az webapp deployment source config-zip \
  --name copiloteval \
  --resource-group rg-copiloteval-prod \
  --src deployment.zip
```

### 4. Health Check Failures

#### Application Not Responding

**Problem**: Health endpoint returns 500 errors
```
HTTP 500 Internal Server Error
```

**Diagnosis**:
```bash
# Check application logs
az webapp log download --name copiloteval --resource-group rg-copiloteval-prod

# Check app insights
az monitor app-insights query \
  --app copiloteval-insights \
  --analytics-query "exceptions | where timestamp > ago(1h)"

# Check environment variables
az webapp config appsettings list --name copiloteval --resource-group rg-copiloteval-prod
```

**Solutions**:
1. Verify database connectivity
2. Check Key Vault access permissions
3. Validate configuration settings
4. Review application startup logs

#### Database Connectivity Issues

**Problem**: Cannot connect to database
```
A network-related or instance-specific error occurred
```

**Diagnosis**:
```bash
# Test database connectivity
az sql db show-connection-string \
  --server copiloteval-sql \
  --name copiloteval-db \
  --client ado.net

# Check firewall rules
az sql server firewall-rule list --server copiloteval-sql --resource-group rg-copiloteval-prod

# Test from App Service
az webapp ssh --name copiloteval --resource-group rg-copiloteval-prod
```

### 5. Performance Issues

#### Slow Deployment

**Problem**: Deployment takes too long
```
Deployment duration: 15+ minutes
```

**Optimizations**:
```bash
# Use deployment slots for zero-downtime
az webapp deployment slot create --name copiloteval --slot staging

# Optimize build artifacts
# - Exclude unnecessary files
# - Use build caching
# - Parallel deployment steps

# Monitor deployment progress
az deployment group list --resource-group rg-copiloteval-prod --query "[0].properties.provisioningState"
```

## 🔧 Debugging Tools and Commands

### GitHub Actions Debugging

```bash
# Enable debug logging
# Add this to workflow environment variables:
ACTIONS_STEP_DEBUG: true
ACTIONS_RUNNER_DEBUG: true

# Check workflow status
gh run list --workflow=ci.yml

# View specific run logs
gh run view RUN_ID --log

# Re-run failed jobs
gh run rerun RUN_ID --failed
```

### Azure Debugging

```bash
# Check resource health
az resource health show --resource-id /subscriptions/.../resourceGroups/.../providers/...

# Monitor metrics
az monitor metrics list \
  --resource /subscriptions/.../resourceGroups/.../providers/... \
  --metric-names "CpuPercentage,MemoryPercentage"

# Check activity logs
az monitor activity-log list \
  --resource-group rg-copiloteval-prod \
  --start-time 2024-01-01T00:00:00Z
```

### Application Debugging

```bash
# Stream application logs
az webapp log tail --name copiloteval --resource-group rg-copiloteval-prod

# Download logs
az webapp log download --name copiloteval --resource-group rg-copiloteval-prod

# Check app insights telemetry
az monitor app-insights query \
  --app copiloteval-insights \
  --analytics-query "requests | where timestamp > ago(1h) | summarize count() by resultCode"
```

## 📞 Escalation Matrix

### Severity Levels

#### P0 - Critical (Production Down)
- **Response Time**: 15 minutes
- **Escalation**: Immediate to on-call engineer
- **Communication**: Customer notification within 30 minutes

#### P1 - High (Major Feature Impacted)
- **Response Time**: 1 hour
- **Escalation**: Development team lead
- **Communication**: Internal stakeholders

#### P2 - Medium (Minor Issues)
- **Response Time**: 4 hours
- **Escalation**: Regular development team
- **Communication**: Team slack channel

#### P3 - Low (Cosmetic Issues)
- **Response Time**: Next business day
- **Escalation**: Regular development cycle
- **Communication**: GitHub issue tracking

### Contact Information

```yaml
On-Call Engineer: +1-XXX-XXX-XXXX
Platform Team: platform@company.com
Development Team: dev-team@company.com
Management: engineering-mgmt@company.com

Slack Channels:
  - #devops-alerts
  - #engineering-incidents
  - #platform-team
```

## 📊 Monitoring and Alerting

### Key Metrics to Monitor

1. **Deployment Success Rate**: > 95%
2. **Deployment Duration**: < 10 minutes
3. **Health Check Success Rate**: > 99%
4. **Error Rate**: < 1%
5. **Response Time**: < 2 seconds

### Alert Rules

```yaml
Deployment Failure:
  condition: deployment_success_rate < 95%
  duration: 1 occurrence
  action: notify on-call engineer

Health Check Failure:
  condition: health_check_failure_count > 3
  duration: 5 minutes
  action: trigger auto-rollback

High Error Rate:
  condition: error_rate > 5%
  duration: 3 minutes
  action: notify development team
```

## 📚 Additional Resources

- [Azure App Service Troubleshooting](https://docs.microsoft.com/en-us/azure/app-service/troubleshoot-diagnostic-logs)
- [GitHub Actions Debugging](https://docs.github.com/en/actions/monitoring-and-troubleshooting-workflows/enabling-debug-logging)
- [Bicep Troubleshooting](https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/troubleshoot)
- [.NET Deployment Troubleshooting](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/azure-apps/troubleshoot)

---

**Last Updated**: [Date]
**Document Owner**: Platform Team
**Review Cycle**: Monthly