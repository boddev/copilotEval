# CopilotEval Operational Runbook

## Overview

This runbook provides operational procedures, troubleshooting guides, and recovery steps for the CopilotEval platform. It covers common failure scenarios, monitoring procedures, and emergency response protocols.

## Table of Contents

1. [System Health Monitoring](#system-health-monitoring)
2. [Common Failure Modes](#common-failure-modes)
3. [Recovery Procedures](#recovery-procedures)
4. [Dead Letter Queue Handling](#dead-letter-queue-handling)
5. [Worker Restart Procedures](#worker-restart-procedures)
6. [Database Recovery](#database-recovery)
7. [Performance Troubleshooting](#performance-troubleshooting)
8. [Emergency Contacts](#emergency-contacts)

## System Health Monitoring

### Key Metrics to Monitor

**Application Performance:**
- API response times (< 500ms for 95th percentile)
- Job processing rate (jobs/minute)
- Worker function execution duration
- Error rates (< 1% for API endpoints)

**Infrastructure Health:**
- Service Bus queue depth
- SQL Database DTU utilization (< 80%)
- Function App CPU/Memory usage
- Blob Storage throughput

**Business Metrics:**
- Job completion rate (> 95%)
- Average similarity scores
- User activity and engagement
- System availability (> 99.9%)

### Monitoring Tools

**Azure Portal Dashboards:**
- Application Insights Overview
- Service Bus Metrics
- SQL Database Performance
- Function App Monitoring

**Log Analytics Queries:**
```kql
// Failed jobs in last 24 hours
Jobs
| where TimeGenerated > ago(24h)
| where Status == "failed"
| summarize count() by bin(TimeGenerated, 1h)

// Service Bus dead letter queue depth
ServiceBusLogs
| where Category == "OperationalLogs"
| where MessageType == "DeadLetter"
| summarize count() by bin(TimeGenerated, 5m)

// Worker function errors
FunctionAppLogs
| where Level == "Error"
| where TimeGenerated > ago(1h)
| project TimeGenerated, Message, Exception
```

### Health Check Endpoints

**API Health Checks:**
- `GET /api/health` - Basic API availability
- `GET /api/health/detailed` - Dependency health status
- `GET /api/health/ready` - Readiness probe

**Expected Responses:**
```json
{
  "status": "healthy",
  "timestamp": "2024-01-15T12:00:00Z",
  "dependencies": {
    "database": "healthy",
    "service_bus": "healthy",
    "blob_storage": "healthy",
    "key_vault": "healthy"
  },
  "metrics": {
    "active_jobs": 15,
    "queue_depth": 3,
    "worker_instances": 2
  }
}
```

## Common Failure Modes

### 1. API Service Unavailable

**Symptoms:**
- HTTP 503 responses from API endpoints
- High error rates in Application Insights
- Frontend unable to load data

**Immediate Actions:**
1. Check Application Insights for exception details
2. Verify Azure App Service status in portal
3. Check resource utilization (CPU/Memory)
4. Review recent deployments

**Diagnostic Commands:**
```bash
# Check API health
curl -X GET https://copiloteval-api.azurewebsites.net/api/health

# Check App Service logs
az webapp log tail --name copiloteval-api --resource-group copiloteval-prod

# Check scaling metrics
az monitor metrics list --resource /subscriptions/.../resourceGroups/copiloteval-prod/providers/Microsoft.Web/sites/copiloteval-api
```

### 2. Worker Functions Not Processing Jobs

**Symptoms:**
- Service Bus queue depth increasing
- Jobs stuck in "pending" status
- No worker telemetry in Application Insights

**Immediate Actions:**
1. Check Function App status and scaling
2. Verify Service Bus connection health
3. Review worker function logs
4. Check for worker function errors

**Diagnostic Commands:**
```bash
# Check Function App status
az functionapp show --name copiloteval-worker --resource-group copiloteval-prod

# Check Service Bus queue metrics
az servicebus queue show --name job-messages --namespace-name copiloteval-sb --resource-group copiloteval-prod

# View function logs
az functionapp log tail --name copiloteval-worker --resource-group copiloteval-prod
```

### 3. Database Connection Issues

**Symptoms:**
- API returning 500 errors
- Database timeout exceptions
- Connection pool exhaustion

**Immediate Actions:**
1. Check SQL Database health and DTU usage
2. Verify connection string configuration
3. Review connection pool settings
4. Check for blocking queries

**Diagnostic Commands:**
```sql
-- Check active connections
SELECT 
    DB_NAME(database_id) as DatabaseName,
    COUNT(*) as ConnectionCount
FROM sys.dm_exec_sessions 
WHERE database_id > 0
GROUP BY database_id;

-- Check blocking processes
SELECT 
    blocking_session_id,
    session_id,
    wait_type,
    wait_resource,
    wait_time
FROM sys.dm_exec_requests 
WHERE blocking_session_id > 0;
```

### 4. Storage Account Access Issues

**Symptoms:**
- File upload failures
- Blob access denied errors
- Large payload processing failures

**Immediate Actions:**
1. Verify storage account accessibility
2. Check managed identity permissions
3. Review storage account configuration
4. Validate SAS token generation

## Recovery Procedures

### API Service Recovery

**Automated Recovery:**
```bash
# Restart App Service
az webapp restart --name copiloteval-api --resource-group copiloteval-prod

# Scale out if needed
az webapp update --name copiloteval-api --resource-group copiloteval-prod --set properties.reserved=true
az appservice plan update --name copiloteval-plan --resource-group copiloteval-prod --sku P2V2
```

**Manual Recovery Steps:**
1. Identify root cause from logs
2. Apply hotfix if code issue
3. Scale up resources if capacity issue
4. Failover to secondary region if needed

### Worker Function Recovery

**Automated Recovery:**
```bash
# Restart Function App
az functionapp restart --name copiloteval-worker --resource-group copiloteval-prod

# Sync function triggers
az functionapp function sync --name copiloteval-worker --resource-group copiloteval-prod
```

**Manual Recovery Steps:**
1. Check function configuration
2. Verify Service Bus triggers
3. Redeploy function code if needed
4. Clear dead letter queue if required

## Dead Letter Queue Handling

### Monitoring Dead Letter Messages

**Check DLQ Depth:**
```bash
az servicebus queue show \
  --name job-messages/\$DeadLetterQueue \
  --namespace-name copiloteval-sb \
  --resource-group copiloteval-prod \
  --query messageCount
```

### Common DLQ Scenarios

**1. Message Format Errors:**
- Invalid JSON payload
- Missing required fields
- Schema validation failures

**Recovery Action:**
```bash
# View DLQ messages
az servicebus queue message peek \
  --queue-name job-messages/\$DeadLetterQueue \
  --namespace-name copiloteval-sb \
  --resource-group copiloteval-prod
```

**2. Processing Timeouts:**
- Long-running job evaluations
- Copilot API rate limiting
- External service unavailability

**Recovery Action:**
```bash
# Requeue messages after fixing issue
az servicebus queue message receive \
  --queue-name job-messages/\$DeadLetterQueue \
  --namespace-name copiloteval-sb \
  --resource-group copiloteval-prod | \
azure-cli-tools requeue --queue job-messages
```

### DLQ Processing Procedures

**Daily DLQ Review:**
1. Check DLQ message count
2. Sample recent messages for error patterns
3. Identify systemic issues
4. Create incident if pattern detected

**Weekly DLQ Cleanup:**
1. Export aged messages for analysis
2. Purge resolved error categories
3. Update monitoring alerts based on trends
4. Document lessons learned

## Worker Restart Procedures

### Graceful Restart

**Prerequisites:**
- Verify no critical jobs in progress
- Check current queue depth
- Ensure backup worker capacity

**Steps:**
```bash
# 1. Drain current work (wait for completion)
az functionapp config set \
  --name copiloteval-worker \
  --resource-group copiloteval-prod \
  --settings FUNCTIONS_WORKER_PROCESS_COUNT=0

# 2. Wait for active executions to complete
az monitor metrics list \
  --resource /subscriptions/.../providers/Microsoft.Web/sites/copiloteval-worker \
  --metric FunctionExecutionUnits

# 3. Restart function app
az functionapp restart \
  --name copiloteval-worker \
  --resource-group copiloteval-prod

# 4. Restore worker capacity
az functionapp config set \
  --name copiloteval-worker \
  --resource-group copiloteval-prod \
  --settings FUNCTIONS_WORKER_PROCESS_COUNT=10
```

### Emergency Restart

**When Immediate Action Required:**
```bash
# Force restart without graceful shutdown
az functionapp restart \
  --name copiloteval-worker \
  --resource-group copiloteval-prod

# Check restart success
az functionapp show \
  --name copiloteval-worker \
  --resource-group copiloteval-prod \
  --query state
```

### Post-Restart Verification

**Health Checks:**
1. Verify function app is running
2. Check Service Bus connection
3. Monitor first job processing
4. Validate telemetry data flow

**Monitoring Commands:**
```bash
# Check function health
curl -X GET "https://copiloteval-worker.azurewebsites.net/api/health"

# Monitor job processing
az monitor metrics list \
  --resource /subscriptions/.../Microsoft.ServiceBus/namespaces/copiloteval-sb \
  --metric ActiveMessages
```

## Database Recovery

### Connection Pool Exhaustion

**Immediate Actions:**
```sql
-- Kill long-running connections
DECLARE @kill varchar(8000) = '';  
SELECT @kill = @kill + 'kill ' + CONVERT(varchar(5), session_id) + ';'  
FROM sys.dm_exec_sessions
WHERE program_name LIKE 'copiloteval%'
  AND last_request_end_time < DATEADD(minute, -30, GETDATE());

EXEC(@kill);
```

**Configuration Updates:**
```bash
# Update connection string with appropriate pool size
az webapp config connection-string set \
  --name copiloteval-api \
  --resource-group copiloteval-prod \
  --connection-string-type SQLAzure \
  --settings DefaultConnection="Server=tcp:..;Connection Timeout=30;Max Pool Size=50;"
```

### Database Deadlock Resolution

**Monitor Deadlocks:**
```sql
-- Enable deadlock monitoring
DBCC TRACEON (1222, -1);

-- Query deadlock information
SELECT 
    deadlock_xml = CAST(target_data AS XML)
FROM sys.dm_xe_session_targets st
JOIN sys.dm_xe_sessions s ON s.address = st.event_session_address
WHERE s.name = 'system_health';
```

### Backup and Recovery

**Point-in-Time Recovery:**
```bash
# Create point-in-time restore
az sql db restore \
  --dest-name copiloteval-db-restored \
  --resource-group copiloteval-prod \
  --server copiloteval-sql \
  --source-database copiloteval-db \
  --time "2024-01-15T12:00:00Z"
```

## Performance Troubleshooting

### High API Latency

**Investigation Steps:**
1. Check Application Insights performance metrics
2. Identify slow database queries
3. Review external API call times
4. Analyze resource utilization

**Optimization Actions:**
```sql
-- Identify slow queries
SELECT TOP 10 
    total_elapsed_time/execution_count AS avg_elapsed_time,
    total_worker_time/execution_count AS avg_worker_time,
    execution_count,
    SUBSTRING(st.text, (qs.statement_start_offset/2)+1, 
        ((CASE qs.statement_end_offset
          WHEN -1 THEN DATALENGTH(st.text)
         ELSE qs.statement_end_offset
         END - qs.statement_start_offset)/2) + 1) AS statement_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
ORDER BY total_elapsed_time/execution_count DESC;
```

### Worker Function Timeouts

**Configuration Adjustments:**
```json
{
  "functionTimeout": "00:10:00",
  "extensions": {
    "serviceBus": {
      "maxConcurrentCalls": 5,
      "prefetchCount": 100,
      "maxAutoRenewDuration": "00:05:00"
    }
  }
}
```

### Memory Leaks

**Monitoring Commands:**
```bash
# Check memory usage
az monitor metrics list \
  --resource /subscriptions/.../Microsoft.Web/sites/copiloteval-worker \
  --metric MemoryWorkingSet \
  --start-time 2024-01-15T00:00:00Z \
  --end-time 2024-01-15T23:59:59Z
```

## Incident Response Procedures

### Severity Classifications

**P0 - Critical (< 30 min response):**
- Complete system outage
- Data corruption or loss
- Security breach

**P1 - High (< 2 hours response):**
- Major feature unavailable
- Performance degradation > 50%
- Multiple user complaints

**P2 - Medium (< 8 hours response):**
- Minor feature issues
- Single component degradation
- Workaround available

**P3 - Low (< 24 hours response):**
- Cosmetic issues
- Documentation gaps
- Enhancement requests

### Escalation Procedures

**Initial Response (0-15 minutes):**
1. Acknowledge incident
2. Assess severity and impact
3. Begin initial investigation
4. Notify stakeholders

**Investigation Phase (15-60 minutes):**
1. Gather system metrics and logs
2. Identify root cause
3. Implement temporary mitigation
4. Update stakeholders

**Resolution Phase (Variable):**
1. Apply permanent fix
2. Verify system recovery
3. Conduct post-incident review
4. Update documentation

## Emergency Contacts

### Technical Team
- **Primary On-Call**: [Phone] [Email]
- **Secondary On-Call**: [Phone] [Email]
- **Platform Team Lead**: [Phone] [Email]
- **DevOps Engineer**: [Phone] [Email]

### Business Stakeholders
- **Product Owner**: [Phone] [Email]
- **Engineering Manager**: [Phone] [Email]
- **Customer Success**: [Phone] [Email]

### Vendor Support
- **Microsoft Azure Support**: +1-800-642-7676
- **Azure Premium Support Portal**: portal.azure.com
- **Severity A Incidents**: Immediate escalation required

## Appendix

### Useful Log Queries

**API Error Analysis:**
```kql
requests
| where timestamp > ago(1h)
| where success == false
| summarize count() by resultCode, bin(timestamp, 5m)
| render timechart
```

**Worker Function Performance:**
```kql
traces
| where cloud_RoleName contains "copiloteval-worker"
| where message contains "JobProcessed"
| extend duration = toreal(customDimensions.Duration)
| summarize avg(duration), count() by bin(timestamp, 1h)
```

### Common Azure CLI Commands

```bash
# Get resource group resources
az resource list --resource-group copiloteval-prod --output table

# Check App Service status
az webapp show --name copiloteval-api --resource-group copiloteval-prod --query state

# Scale Function App
az functionapp plan update --name copiloteval-plan --resource-group copiloteval-prod --sku EP2

# Check Service Bus namespace
az servicebus namespace show --name copiloteval-sb --resource-group copiloteval-prod
```

### Monitoring Alert Rules

**Configure Critical Alerts:**
```bash
# API availability alert
az monitor metrics alert create \
  --name "API-Availability-Alert" \
  --resource-group copiloteval-prod \
  --scopes /subscriptions/.../Microsoft.Web/sites/copiloteval-api \
  --condition "avg Http5xx > 5" \
  --window-size 5m \
  --evaluation-frequency 1m

# Queue depth alert  
az monitor metrics alert create \
  --name "Queue-Depth-Alert" \
  --resource-group copiloteval-prod \
  --scopes /subscriptions/.../Microsoft.ServiceBus/namespaces/copiloteval-sb \
  --condition "avg ActiveMessages > 100" \
  --window-size 5m \
  --evaluation-frequency 1m
```

---

**Document Version**: 1.0  
**Last Updated**: January 2024  
**Review Schedule**: Monthly  
**Owner**: Platform Engineering Team