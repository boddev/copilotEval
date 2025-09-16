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