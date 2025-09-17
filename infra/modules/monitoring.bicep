@description('Name of the Application Insights component')
param appInsightsName string

@description('Name of the Log Analytics workspace')
param logAnalyticsName string

@description('Location for the monitoring resources')
param location string = resourceGroup().location

@description('Tags to apply to the monitoring resources')
param tags object = {}

@description('Environment name')
param environment string

@description('Log Analytics workspace retention in days')
param logRetentionInDays int = environment == 'prod' ? 365 : 30

@description('Application Insights sampling percentage')
param samplingPercentage int = environment == 'prod' ? 100 : 50

// Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: environment == 'prod' ? 'PerGB2018' : 'Free'
    }
    retentionInDays: logRetentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
      disableLocalAuth: false
    }
    workspaceCapping: environment != 'prod' ? {
      dailyQuotaGb: json('0.5')
    } : null
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    SamplingPercentage: samplingPercentage
    RetentionInDays: logRetentionInDays
    DisableIpMasking: environment == 'dev'
    ImmediatePurgeDataOn30Days: environment == 'dev'
  }
}

// Action Groups for alerting
resource alertActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (environment != 'dev') {
  name: '${appInsightsName}-alerts'
  location: 'Global'
  tags: tags
  properties: {
    groupShortName: 'CopilotEval'
    enabled: true
    emailReceivers: [
      {
        name: 'DevOps Team'
        emailAddress: 'devops@example.com'
        useCommonAlertSchema: true
      }
    ]
    smsReceivers: []
    webhookReceivers: []
    eventHubReceivers: []
    itsmReceivers: []
    azureAppPushReceivers: []
    automationRunbookReceivers: []
    voiceReceivers: []
    logicAppReceivers: []
    azureFunctionReceivers: []
    armRoleReceivers: []
  }
}

// Metric Alerts
resource highErrorRateAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (environment != 'dev') {
  name: '${appInsightsName}-high-error-rate'
  location: 'Global'
  tags: tags
  properties: {
    description: 'Alert when error rate is high'
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ErrorRate'
          metricName: 'exceptions/count'
          operator: 'GreaterThan'
          threshold: environment == 'prod' ? 10 : 5
          timeAggregation: 'Count'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: alertActionGroup.id
      }
    ]
  }
}

resource highResponseTimeAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (environment == 'prod') {
  name: '${appInsightsName}-high-response-time'
  location: 'Global'
  tags: tags
  properties: {
    description: 'Alert when response time is high'
    severity: 3
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ResponseTime'
          metricName: 'requests/duration'
          operator: 'GreaterThan'
          threshold: 5000 // 5 seconds
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: alertActionGroup.id
      }
    ]
  }
}

resource lowAvailabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (environment == 'prod') {
  name: '${appInsightsName}-low-availability'
  location: 'Global'
  tags: tags
  properties: {
    description: 'Alert when availability is low'
    severity: 1
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Availability'
          metricName: 'availabilityResults/availabilityPercentage'
          operator: 'LessThan'
          threshold: 95
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: alertActionGroup.id
      }
    ]
  }
}

// Enhanced alerts for OpenTelemetry metrics
resource queueLengthAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = if (environment != 'dev') {
  name: '${appInsightsName}-high-queue-length'
  location: location
  tags: tags
  properties: {
    displayName: 'High Queue Length Alert'
    description: 'Alert when queue length is consistently high'
    severity: 2
    enabled: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
customMetrics
| where cloud_RoleName contains "copiloteval"
| where name == "copiloteval_worker_active_message_processors"
| where timestamp > ago(15m)
| summarize AvgActiveProcessors = avg(value)
'''
          threshold: 10
          operator: 'GreaterThan'
          resourceIdColumn: ''
          metricMeasureColumn: 'AvgActiveProcessors'
          dimensions: []
          failingPeriods: {
            numberOfEvaluationPeriods: 3
            minFailingPeriodsToAlert: 2
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        alertActionGroup.id
      ]
    }
  }
}

resource highErrorRateScheduledAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = if (environment != 'dev') {
  name: '${appInsightsName}-high-error-rate-scheduled'
  location: location
  tags: tags
  properties: {
    displayName: 'High Error Rate Alert (Enhanced)'
    description: 'Alert when error rate exceeds 5% over 10 minutes'
    severity: 1
    enabled: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    criteria: {
      allOf: [
        {
          query: '''
union traces, customEvents
| where cloud_RoleName contains "copiloteval"
| where timestamp > ago(10m)
| extend IsError = case(
    severityLevel >= 3, 1,
    name contains "Failed" or name contains "Error", 1,
    customDimensions.["status"] == "failed", 1,
    customDimensions.["status"] == "error", 1,
    0)
| summarize 
    Total = count(),
    Errors = sum(IsError),
    ErrorRate = (sum(IsError) * 100.0) / count()
| where Total > 10
'''
          threshold: 5.0
          operator: 'GreaterThan'
          resourceIdColumn: ''
          metricMeasureColumn: 'ErrorRate'
          dimensions: []
          failingPeriods: {
            numberOfEvaluationPeriods: 2
            minFailingPeriodsToAlert: 2
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        alertActionGroup.id
      ]
    }
  }
}

resource jobProcessingDurationAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = if (environment == 'prod') {
  name: '${appInsightsName}-slow-job-processing'
  location: location
  tags: tags
  properties: {
    displayName: 'Slow Job Processing Alert'
    description: 'Alert when job processing takes longer than 5 minutes on average'
    severity: 2
    enabled: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    evaluationFrequency: 'PT10M'
    windowSize: 'PT20M'
    criteria: {
      allOf: [
        {
          query: '''
customMetrics
| where cloud_RoleName contains "copiloteval"
| where name == "copiloteval_job_processing_duration_seconds"
| where timestamp > ago(20m)
| summarize AvgDuration = avg(value)
'''
          threshold: 300 // 5 minutes in seconds
          operator: 'GreaterThan'
          resourceIdColumn: ''
          metricMeasureColumn: 'AvgDuration'
          dimensions: []
          failingPeriods: {
            numberOfEvaluationPeriods: 2
            minFailingPeriodsToAlert: 2
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        alertActionGroup.id
      ]
    }
  }
}

resource workerUnhealthyAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = if (environment != 'dev') {
  name: '${appInsightsName}-worker-unhealthy'
  location: location
  tags: tags
  properties: {
    displayName: 'Worker Unhealthy Alert'
    description: 'Alert when worker stops processing messages for 10 minutes'
    severity: 1
    enabled: true
    scopes: [
      logAnalyticsWorkspace.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    criteria: {
      allOf: [
        {
          query: '''
customMetrics
| where cloud_RoleName contains "copiloteval"
| where name == "copiloteval_worker_messages_processed_total"
| where timestamp > ago(10m)
| summarize MessagesProcessed = sum(value)
'''
          threshold: 1
          operator: 'LessThan'
          resourceIdColumn: ''
          metricMeasureColumn: 'MessagesProcessed'
          dimensions: []
          failingPeriods: {
            numberOfEvaluationPeriods: 2
            minFailingPeriodsToAlert: 2
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        alertActionGroup.id
      ]
    }
  }
}

// Log queries for common scenarios
resource jobFailureLogQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'job-failures'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Job Failures'
    query: '''
traces
| where cloud_RoleName contains "copiloteval-func"
| where message contains "job_failed" or message contains "ERROR"
| where timestamp > ago(1h)
| project timestamp, message, severityLevel, operation_Name, cloud_RoleInstance
| order by timestamp desc
'''
    functionAlias: 'JobFailures'
    functionParameters: 'timeRange:timespan=1h'
  }
}

resource performanceLogQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'performance-metrics'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Performance Metrics'
    query: '''
requests
| where cloud_RoleName contains "copiloteval"
| where timestamp > ago(1h)
| summarize 
    AvgDuration = avg(duration),
    P95Duration = percentile(duration, 95),
    RequestCount = count(),
    SuccessRate = avg(case(success == true, 1.0, 0.0)) * 100
    by bin(timestamp, 5m), name
| order by timestamp desc
'''
    functionAlias: 'PerformanceMetrics'
    functionParameters: 'timeRange:timespan=1h'
  }
}

resource usageLogQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'usage-statistics'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Usage Statistics'
    query: '''
customEvents
| where cloud_RoleName contains "copiloteval"
| where name in ("JobCreated", "JobCompleted", "JobFailed")
| where timestamp > ago(24h)
| summarize Count = count() by name, bin(timestamp, 1h)
| order by timestamp desc
'''
    functionAlias: 'UsageStatistics'
    functionParameters: 'timeRange:timespan=24h'
  }
}

// Enhanced queries for OpenTelemetry tracing
resource tracingLogQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'job-tracing'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Job Processing Traces'
    query: '''
traces
| where cloud_RoleName contains "copiloteval"
| where operation_Name in ("job.enqueue", "job.process", "job.execute", "worker.message.process")
| where timestamp > ago(1h)
| project 
    timestamp,
    operation_Name,
    operation_Id,
    message,
    severityLevel,
    customDimensions.["job.id"],
    customDimensions.["correlation.id"],
    customDimensions.["job.type"],
    customDimensions.["status"]
| order by timestamp desc
'''
    functionAlias: 'JobTracing'
    functionParameters: 'timeRange:timespan=1h'
  }
}

// Queue depth and worker metrics
resource queueMetricsQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'queue-metrics'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Queue and Worker Metrics'
    query: '''
customMetrics
| where cloud_RoleName contains "copiloteval"
| where name in (
    "copiloteval_jobs_enqueued_total",
    "copiloteval_worker_messages_received_total",
    "copiloteval_worker_messages_processed_total",
    "copiloteval_worker_active_message_processors"
)
| where timestamp > ago(1h)
| summarize 
    Value = avg(value) 
    by name, bin(timestamp, 5m)
| order by timestamp desc
'''
    functionAlias: 'QueueMetrics'
    functionParameters: 'timeRange:timespan=1h'
  }
}

// Performance metrics for job processing
resource jobPerformanceQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'job-performance'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Job Performance Metrics'
    query: '''
customMetrics
| where cloud_RoleName contains "copiloteval"
| where name in (
    "copiloteval_job_processing_duration_seconds",
    "copiloteval_copilot_api_duration_seconds",
    "copiloteval_worker_message_processing_duration_seconds"
)
| where timestamp > ago(1h)
| summarize 
    AvgDuration = avg(value),
    P95Duration = percentile(value, 95),
    P99Duration = percentile(value, 99),
    MaxDuration = max(value)
    by name, bin(timestamp, 5m)
| order by timestamp desc
'''
    functionAlias: 'JobPerformance'
    functionParameters: 'timeRange:timespan=1h'
  }
}

// Error rate tracking
resource errorRateQuery 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = {
  name: 'error-rates'
  parent: logAnalyticsWorkspace
  properties: {
    category: 'CopilotEval'
    displayName: 'Error Rate Analysis'
    query: '''
union traces, customEvents
| where cloud_RoleName contains "copiloteval"
| where timestamp > ago(1h)
| extend IsError = case(
    severityLevel >= 3, 1,
    name contains "Failed" or name contains "Error", 1,
    customDimensions.["status"] == "failed", 1,
    customDimensions.["status"] == "error", 1,
    0)
| summarize 
    Total = count(),
    Errors = sum(IsError),
    ErrorRate = (sum(IsError) * 100.0) / count()
    by bin(timestamp, 5m), cloud_RoleName
| order by timestamp desc
'''
    functionAlias: 'ErrorRates'
    functionParameters: 'timeRange:timespan=1h'
  }
}
    functionAlias: 'UsageStatistics'
    functionParameters: 'timeRange:timespan=24h'
  }
}

// Workbooks for dashboards
resource overviewWorkbook 'Microsoft.Insights/workbooks@2022-04-01' = if (environment != 'dev') {
  name: guid('${appInsightsName}-overview')
  location: location
  tags: tags
  kind: 'shared'
  properties: {
    displayName: 'CopilotEval Overview Dashboard'
    serializedData: '''
{
  "version": "Notebook/1.0",
  "items": [
    {
      "type": 1,
      "content": {
        "json": "# CopilotEval Application Overview\n\nThis dashboard provides an overview of the CopilotEval application performance and usage."
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "requests\n| where timestamp > ago(24h)\n| summarize RequestCount = count(), AvgDuration = avg(duration) by bin(timestamp, 1h)\n| render timechart",
        "size": 0,
        "title": "Request Rate and Response Time"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "customEvents\n| where name in ('JobCreated', 'JobCompleted', 'JobFailed')\n| where timestamp > ago(24h)\n| summarize Count = count() by name\n| render piechart",
        "size": 0,
        "title": "Job Status Distribution"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "traces\n| where cloud_RoleName contains \"copiloteval\"\n| where operation_Name in (\"job.enqueue\", \"job.process\", \"worker.message.process\")\n| where timestamp > ago(1h)\n| summarize Count = count() by operation_Name, bin(timestamp, 5m)\n| render timechart",
        "size": 0,
        "title": "Job Processing Pipeline - Last Hour"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "customMetrics\n| where cloud_RoleName contains \"copiloteval\"\n| where name in (\"copiloteval_worker_active_message_processors\", \"copiloteval_jobs_enqueued_total\", \"copiloteval_worker_messages_processed_total\")\n| where timestamp > ago(1h)\n| summarize Value = avg(value) by name, bin(timestamp, 5m)\n| render timechart",
        "size": 0,
        "title": "Queue Metrics - Active Processors vs Enqueued vs Processed"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "customMetrics\n| where cloud_RoleName contains \"copiloteval\"\n| where name in (\"copiloteval_job_processing_duration_seconds\", \"copiloteval_copilot_api_duration_seconds\")\n| where timestamp > ago(1h)\n| summarize AvgDuration = avg(value), P95Duration = percentile(value, 95) by name, bin(timestamp, 5m)\n| render timechart",
        "size": 0,
        "title": "Performance Metrics - Processing Duration (Avg & P95)"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "union traces, customEvents\n| where cloud_RoleName contains \"copiloteval\"\n| where timestamp > ago(1h)\n| extend IsError = case(severityLevel >= 3, 1, name contains \"Failed\" or name contains \"Error\", 1, customDimensions.[\"status\"] == \"failed\", 1, 0)\n| summarize Total = count(), Errors = sum(IsError), ErrorRate = (sum(IsError) * 100.0) / count() by bin(timestamp, 5m)\n| render timechart",
        "size": 0,
        "title": "Error Rate Trends - Last Hour"
      }
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "traces\n| where cloud_RoleName contains \"copiloteval\"\n| where operation_Name == \"job.process\"\n| where timestamp > ago(1h)\n| project timestamp, customDimensions.[\"job.id\"], customDimensions.[\"correlation.id\"], customDimensions.[\"status\"], operation_Id\n| where isnotempty(customDimensions_job_id)\n| order by timestamp desc\n| take 50",
        "size": 0,
        "title": "Recent Job Processing Activity"
      }
    }
  ]
}
'''
    category: 'workbook'
    sourceId: appInsights.id
  }
}

// Outputs
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output appInsightsId string = appInsights.id
output appInsightsName string = appInsights.name
output instrumentationKey string = appInsights.properties.InstrumentationKey
output connectionString string = appInsights.properties.ConnectionString
output actionGroupId string = environment != 'dev' ? alertActionGroup.id : ''
output workspaceId string = logAnalyticsWorkspace.properties.customerId