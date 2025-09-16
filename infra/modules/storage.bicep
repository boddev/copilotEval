@description('Name of the Storage Account')
param storageAccountName string

@description('Location for the Storage Account')
param location string = resourceGroup().location

@description('Tags to apply to the Storage Account')
param tags object = {}

@description('Environment name')
param environment string

@description('Storage account configuration')
param config object

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: config.skuName
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: true
    accessTier: 'Hot'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
      ipRules: []
      virtualNetworkRules: []
    }
    encryption: {
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
        file: {
          enabled: true
          keyType: 'Account'
        }
        queue: {
          enabled: true
          keyType: 'Account'
        }
        table: {
          enabled: true
          keyType: 'Account'
        }
      }
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: environment == 'prod'
    }
    largeFileSharesState: 'Disabled'
    isHnsEnabled: false
    isSftpEnabled: false
    isLocalUserEnabled: false
  }
}

// Blob Service
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  name: 'default'
  parent: storageAccount
  properties: {
    changeFeed: {
      enabled: environment != 'dev'
      retentionInDays: environment == 'prod' ? 365 : 30
    }
    restorePolicy: {
      enabled: environment == 'prod'
      days: environment == 'prod' ? 6 : 1
    }
    deleteRetentionPolicy: {
      enabled: true
      days: environment == 'prod' ? 365 : 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: environment == 'prod' ? 365 : 7
    }
    isVersioningEnabled: environment == 'prod'
    cors: {
      corsRules: [
        {
          allowedOrigins: [
            'http://localhost:3000'
            'http://localhost:5173'
            'https://*.azurewebsites.net'
          ]
          allowedMethods: [
            'GET'
            'POST'
            'PUT'
            'DELETE'
            'OPTIONS'
          ]
          allowedHeaders: [
            '*'
          ]
          exposedHeaders: [
            '*'
          ]
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

// Queue Service
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  name: 'default'
  parent: storageAccount
  properties: {
    cors: {
      corsRules: []
    }
  }
}

// Table Service
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  name: 'default'
  parent: storageAccount
  properties: {
    cors: {
      corsRules: []
    }
  }
}

// File Service
resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  name: 'default'
  parent: storageAccount
  properties: {
    cors: {
      corsRules: []
    }
    shareDeleteRetentionPolicy: {
      enabled: true
      days: environment == 'prod' ? 365 : 7
    }
  }
}

// Blob Containers
resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for containerName in config.containers: {
  name: containerName
  parent: blobService
  properties: {
    publicAccess: 'None'
    metadata: {
      environment: environment
      purpose: containerName == 'job-data' ? 'Job input data and CSV files' : (containerName == 'results' ? 'Job results and evaluation data' : (containerName == 'uploads' ? 'User uploaded files' : (containerName == 'large-payloads' ? 'Large payload data referenced by BlobReference' : 'General storage')))
    }
  }
}]

// Tables for job tracking
resource jobsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: 'jobs'
  parent: tableService
}

resource jobResultsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: 'jobresults'
  parent: tableService
}

resource jobProgressTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: 'jobprogress'
  parent: tableService
}

// Queues for async processing (as backup to Service Bus)
resource deadLetterQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: 'deadletter'
  parent: queueService
  properties: {
    metadata: {
      purpose: 'Dead letter messages from Service Bus'
    }
  }
}

resource retryQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: 'retry'
  parent: queueService
  properties: {
    metadata: {
      purpose: 'Messages to be retried'
    }
  }
}

// File Shares for shared data
resource configShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  name: 'config'
  parent: fileService
  properties: {
    shareQuota: 100
    enabledProtocols: 'SMB'
    metadata: {
      purpose: 'Configuration files and templates'
    }
  }
}

resource logsShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  name: 'logs'
  parent: fileService
  properties: {
    shareQuota: environment == 'prod' ? 1024 : 100
    enabledProtocols: 'SMB'
    metadata: {
      purpose: 'Application logs and diagnostic data'
    }
  }
}

// Diagnostic settings for monitoring
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev') {
  scope: storageAccount
  name: '${storageAccountName}-diagnostics'
  properties: {
    metrics: [
      {
        category: 'Transaction'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
    ]
  }
}

// Blob diagnostic settings
resource blobDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev') {
  scope: blobService
  name: '${storageAccountName}-blob-diagnostics'
  properties: {
    logs: [
      {
        category: 'StorageRead'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
      {
        category: 'StorageWrite'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
      {
        category: 'StorageDelete'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 365 : 30
        }
      }
    ]
    metrics: [
      {
        category: 'Transaction'
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
output storageAccountId string = storageAccount.id
output accountName string = storageAccount.name
output primaryEndpoints object = storageAccount.properties.primaryEndpoints
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
output blobConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
output containers array = [for (containerName, i) in config.containers: {
  name: containerName
  id: containers[i].id
}]
output tables array = [
  {
    name: 'jobs'
    id: jobsTable.id
  }
  {
    name: 'jobresults'
    id: jobResultsTable.id
  }
  {
    name: 'jobprogress'
    id: jobProgressTable.id
  }
]
output queues array = [
  {
    name: 'deadletter'
    id: deadLetterQueue.id
  }
  {
    name: 'retry'
    id: retryQueue.id
  }
]
output fileShares array = [
  {
    name: 'config'
    id: configShare.id
  }
  {
    name: 'logs'
    id: logsShare.id
  }
]