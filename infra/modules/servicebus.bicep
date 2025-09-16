@description('Name of the Service Bus namespace')
param namespaceName string

@description('Location for the Service Bus namespace')
param location string = resourceGroup().location

@description('Tags to apply to the Service Bus namespace')
param tags object = {}

@description('Environment name')
param environment string

@description('Service Bus configuration')
param config object

// Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: config.skuName
    tier: config.skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
    zoneRedundant: environment == 'prod'
  }
}

// Service Bus Queues based on JobMessage schema requirements
resource queues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for queue in config.queues: {
  name: queue.name
  parent: serviceBusNamespace
  properties: {
    maxDeliveryCount: queue.maxDeliveryCount
    lockDuration: 'PT${queue.lockDurationInSeconds}S'
    duplicateDetectionHistoryTimeWindow: queue.duplicateDetectionTimeWindow
    deadLetteringOnMessageExpiration: queue.deadLetteringOnMessageExpiration
    enableBatchedOperations: queue.enableBatchedOperations
    enablePartitioning: queue.enablePartitioning
    maxSizeInMegabytes: queue.maxSizeInMegabytes
    requiresDuplicateDetection: true
    requiresSession: false
    status: 'Active'
    autoDeleteOnIdle: 'P365D' // 1 year
    defaultMessageTimeToLive: 'P14D' // 14 days
  }
}]

// Authorization rules
resource sendListenRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  name: 'SendListenRule'
  parent: serviceBusNamespace
  properties: {
    rights: [
      'Send'
      'Listen'
    ]
  }
}

resource manageRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  name: 'ManageRule'
  parent: serviceBusNamespace
  properties: {
    rights: [
      'Send'
      'Listen'
      'Manage'
    ]
  }
}

// Topic for job events (pub/sub pattern for notifications)
resource jobEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  name: 'job-events'
  parent: serviceBusNamespace
  properties: {
    maxSizeInMegabytes: config.skuName == 'Basic' ? 1024 : 5120
    enableBatchedOperations: true
    enablePartitioning: environment == 'prod' && config.skuName != 'Basic'
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    defaultMessageTimeToLive: 'P7D' // 7 days
    autoDeleteOnIdle: 'P365D' // 1 year
    status: 'Active'
  }
}

// Subscription for monitoring/logging job events
resource monitoringSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = if (environment != 'dev') {
  name: 'monitoring'
  parent: jobEventsTopic
  properties: {
    lockDuration: 'PT1M'
    maxDeliveryCount: 10
    deadLetteringOnFilterEvaluationExceptions: true
    deadLetteringOnMessageExpiration: true
    enableBatchedOperations: true
    requiresSession: false
    status: 'Active'
    autoDeleteOnIdle: 'P365D'
    defaultMessageTimeToLive: 'P7D'
  }
}

// Subscription for webhook notifications
resource webhookSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  name: 'webhook-notifications'
  parent: jobEventsTopic
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    deadLetteringOnFilterEvaluationExceptions: true
    deadLetteringOnMessageExpiration: true
    enableBatchedOperations: true
    requiresSession: false
    status: 'Active'
    autoDeleteOnIdle: 'P365D'
    defaultMessageTimeToLive: 'P7D'
  }
}

// Filter for webhook subscription (only completed/failed events)
resource webhookFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = {
  name: 'webhook-filter'
  parent: webhookSubscription
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'message_type IN (\'job_completed\', \'job_failed\', \'job_cancelled\')'
    }
  }
}

// Diagnostic settings for monitoring
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev') {
  scope: serviceBusNamespace
  name: '${namespaceName}-diagnostics'
  properties: {
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
    ]
  }
}

// Outputs
output namespaceId string = serviceBusNamespace.id
output namespaceName string = serviceBusNamespace.name
output connectionString string = sendListenRule.listKeys().primaryConnectionString
output managementConnectionString string = manageRule.listKeys().primaryConnectionString
output queues array = [for (queue, i) in config.queues: {
  name: queue.name
  id: queues[i].id
}]
output topicName string = jobEventsTopic.name
output subscriptions array = [
  {
    name: 'webhook-notifications'
    id: webhookSubscription.id
  }
]