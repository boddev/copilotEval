# Deployment Rollback Procedures

This document outlines the rollback procedures for the CopilotEval application in case of deployment failures or critical issues.

## 🚨 Emergency Rollback Scenarios

### When to Rollback
- Critical bugs in production affecting user experience
- Performance degradation > 50% compared to previous version
- Database migration failures causing data corruption
- Security vulnerabilities introduced in the new deployment
- Service unavailability > 5 minutes

## 🔄 Automatic Rollback

### Blue-Green Deployment Rollback
The CD pipeline implements automatic blue-green deployment with built-in rollback capabilities.

#### Automatic Triggers
- Health check failures after deployment
- Performance regression detection
- Critical error rate threshold exceeded (>5% error rate)

#### Process
1. Health checks fail on new deployment
2. Pipeline automatically swaps back to previous version
3. Monitoring alerts are triggered
4. Incident response team is notified

## 🛠️ Manual Rollback Procedures

### 1. Azure Portal Rollback (Quick)

#### Steps:
1. Navigate to Azure Portal → App Services
2. Select the affected app service
3. Go to **Deployment slots**
4. Click **Swap** to swap staging with production
5. Verify the rollback was successful

#### Timeline: ~2-3 minutes

### 2. Azure CLI Rollback (Preferred)

#### Production Rollback
```bash
# Login to Azure
az login

# Set subscription
az account set --subscription "YOUR_SUBSCRIPTION_ID"

# Swap slots to rollback
az webapp deployment slot swap \
  --resource-group "rg-copiloteval-prod" \
  --name "copiloteval" \
  --slot "production" \
  --target-slot "staging"

# Verify rollback
curl -f https://copiloteval.azurewebsites.net/api/health
```

#### Staging Rollback
```bash
az webapp deployment slot swap \
  --resource-group "rg-copiloteval-staging" \
  --name "copiloteval-staging" \
  --slot "production" \
  --target-slot "staging"
```

#### Timeline: ~1-2 minutes

### 3. GitHub Actions Rollback

#### Trigger Manual Rollback Workflow
1. Go to GitHub Actions tab
2. Select **CD Pipeline** workflow
3. Click **Run workflow**
4. Select **production** environment
5. Check **force_deploy** if needed
6. Provide rollback commit SHA or tag

#### Via GitHub CLI
```bash
gh workflow run cd.yml \
  --field environment=production \
  --field rollback_to_commit=PREVIOUS_COMMIT_SHA
```

#### Timeline: ~5-10 minutes

## 🗄️ Database Rollback

### Database Migration Rollback

#### Check Migration Status
```bash
# Connect to production database
sqlcmd -S copiloteval-prod-sql.database.windows.net -d copiloteval-prod

# Check applied migrations
SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
```

#### Rollback Migration
```bash
# Rollback to specific migration
dotnet ef database update PreviousMigrationName --connection "YOUR_CONNECTION_STRING"

# Or rollback one migration
dotnet ef database update --connection "YOUR_CONNECTION_STRING"
```

⚠️ **Warning**: Database rollbacks should only be performed by database administrators and may result in data loss.

## 📊 Infrastructure Rollback

### Bicep Template Rollback

#### Revert to Previous Infrastructure Version
```bash
cd infra

# Deploy previous version of infrastructure
az deployment group create \
  --resource-group "rg-copiloteval-prod" \
  --template-file "main.bicep" \
  --parameters "@parameters/prod.parameters.json" \
  --name "rollback-$(date +%Y%m%d-%H%M%S)"
```

#### Using Git Tags
```bash
# Checkout previous infrastructure version
git checkout PREVIOUS_INFRASTRUCTURE_TAG

# Deploy previous infrastructure
./deploy.sh -e prod -g rg-copiloteval-prod -s YOUR_SUBSCRIPTION_ID
```

## 🔍 Verification Steps

### Post-Rollback Verification Checklist

#### Application Health
- [ ] Application responds to health checks
- [ ] Main user flows are functional
- [ ] Database connectivity is restored
- [ ] API endpoints are responsive
- [ ] Frontend loads correctly

#### Monitoring
- [ ] Error rates have returned to normal
- [ ] Response times are acceptable
- [ ] No critical alerts are firing
- [ ] User sessions are not disrupted

#### Tests
```bash
# Run smoke tests
cd infra
./smoke-tests.sh -e prod -g rg-copiloteval-prod -s YOUR_SUBSCRIPTION_ID

# Run API integration tests
cd backend
./scripts/jobs-api-integration-tests.sh
```

## 📞 Escalation Procedures

### Rollback Failure Escalation

#### Level 1: Development Team
- **Contact**: Development team lead
- **Response Time**: 15 minutes
- **Responsibilities**: Execute standard rollback procedures

#### Level 2: Platform Team
- **Contact**: Platform/DevOps team
- **Response Time**: 30 minutes
- **Responsibilities**: Infrastructure-level rollbacks, database issues

#### Level 3: Emergency Response
- **Contact**: Engineering manager + on-call engineer
- **Response Time**: 1 hour
- **Responsibilities**: Critical system recovery, data recovery

### Contact Information
```
Development Team: dev-team@company.com
Platform Team: platform@company.com
On-Call Engineer: +1-XXX-XXX-XXXX
Emergency Hotline: +1-XXX-XXX-XXXX
```

## 📋 Rollback Communication

### Internal Communication Template
```
🚨 PRODUCTION ROLLBACK INITIATED

Time: [TIMESTAMP]
Environment: [production/staging]
Reason: [Brief description]
Rollback Method: [manual/automatic]
ETA: [Expected completion time]
Status: [in-progress/completed/failed]

Next Update: [Time for next update]
```

### Customer Communication Template
```
We are currently experiencing technical difficulties and are working to resolve the issue. 
Expected resolution time: [X] minutes.
We apologize for any inconvenience.

Status page: status.copiloteval.com
```

## 🔧 Prevention Measures

### Rollback Prevention Strategy
1. **Comprehensive Testing**: Ensure all tests pass before deployment
2. **Gradual Rollouts**: Use feature flags for incremental releases
3. **Monitoring**: Implement real-time monitoring and alerting
4. **Canary Deployments**: Test with small user groups first
5. **Backup Strategy**: Automated backups before each deployment

### Monitoring and Alerts
- Error rate > 5% for 5 minutes
- Response time > 2 seconds for 3 minutes
- Health check failures
- Database connection failures
- Memory/CPU usage > 90%

## 📚 Related Documentation

- [Deployment Guide](DEPLOYMENT_SUMMARY.md)
- [Infrastructure Setup](README.md)
- [Monitoring and Alerting](../docs/monitoring.md)
- [Incident Response Playbook](../docs/incident-response.md)

---

**Last Updated**: [Date]
**Document Owner**: Platform Team
**Review Cycle**: Monthly