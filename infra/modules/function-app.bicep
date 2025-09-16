@description('Name of the Function App')
param functionAppName string

@description('Name of the App Service Plan')
param appServicePlanName string

@description('Location for the Function App')
param location string = resourceGroup().location

@description('Tags to apply to the Function App')
param tags object = {}

@description('Environment name')
param environment string

@description('Function App configuration')
param config object

@description('Storage Account name for the Function App')
param storageAccountName string

@description('Service Bus connection string')
@secure()
param serviceBusConnectionString string

@description('SQL connection string')
@secure()
param sqlConnectionString string

@description('Application Insights instrumentation key')
@secure()
param appInsightsInstrumentationKey string = ''

@description('Key Vault name')
param keyVaultName string

// Get storage account for Function App storage
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Get Key Vault for configuration
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: config.skuName
    tier: config.skuTier
  }
  kind: config.skuTier == 'Dynamic' ? 'functionapp' : 'elastic'
  properties: {
    reserved: false
    maximumElasticWorkerCount: environment == 'prod' ? 20 : 5
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(functionAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~18'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: config.runtime
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        // Service Bus Configuration
        {
          name: 'ServiceBusConnectionString'
          value: serviceBusConnectionString
        }
        {
          name: 'JobMessagesQueue'
          value: 'job-messages'
        }
        {
          name: 'JobResultsQueue'
          value: 'job-results'
        }
        {
          name: 'JobEventsTopic'
          value: 'job-events'
        }
        // SQL Database Configuration
        {
          name: 'SqlConnectionString'
          value: sqlConnectionString
        }
        // Storage Configuration
        {
          name: 'BlobStorageConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'JobDataContainer'
          value: 'job-data'
        }
        {
          name: 'ResultsContainer'
          value: 'results'
        }
        {
          name: 'UploadsContainer'
          value: 'uploads'
        }
        {
          name: 'LargePayloadsContainer'
          value: 'large-payloads'
        }
        // Key Vault Configuration
        {
          name: 'KeyVaultName'
          value: keyVaultName
        }
        {
          name: 'KeyVaultUri'
          value: keyVault.properties.vaultUri
        }
        // Application Configuration
        {
          name: 'Environment'
          value: environment
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'prod' ? 'Production' : environment == 'staging' ? 'Staging' : 'Development'
        }
        // Logging Configuration
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsInstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: !empty(appInsightsInstrumentationKey) ? 'InstrumentationKey=${appInsightsInstrumentationKey}' : ''
        }
        // Job Processing Configuration
        {
          name: 'MaxConcurrentJobs'
          value: environment == 'prod' ? '10' : '3'
        }
        {
          name: 'JobTimeoutMinutes'
          value: environment == 'prod' ? '60' : '30'
        }
        {
          name: 'MaxRetryAttempts'
          value: '3'
        }
        {
          name: 'RetryDelaySeconds'
          value: '30'
        }
        // Copilot API Configuration (will be set from Key Vault)
        {
          name: 'CopilotApiEndpoint'
          value: 'https://api.copilot.microsoft.com'
        }
        // Performance and Scaling
        {
          name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
          value: 'true'
        }
        {
          name: 'WEBSITE_LOAD_USER_PROFILE'
          value: '1'
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      netFrameworkVersion: 'v8.0'
      phpVersion: 'OFF'
      pythonVersion: 'OFF'
      nodeVersion: 'OFF'
      javaVersion: 'OFF'
      powerShellVersion: 'OFF'
      linuxFxVersion: ''
      requestTracingEnabled: environment != 'prod'
      httpLoggingEnabled: environment != 'prod'
      logsDirectorySizeLimit: environment == 'prod' ? 100 : 35
      detailedErrorLoggingEnabled: environment != 'prod'
      alwaysOn: config.skuTier != 'Dynamic'
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
          'https://ms.portal.azure.com'
        ]
        supportCredentials: false
      }
      ipSecurityRestrictions: []
      scmIpSecurityRestrictions: []
      scmIpSecurityRestrictionsUseMain: false
      http20Enabled: true
      websiteTimeZone: 'UTC'
      functionAppScaleLimit: environment == 'prod' ? 200 : 20
      minimumElasticInstanceCount: environment == 'prod' ? 2 : 0
      healthCheckPath: '/api/health'
    }
  }
}

// Grant Function App access to Key Vault
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  name: 'add'
  parent: keyVault
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
}

// Diagnostic settings for monitoring
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev' && !empty(appInsightsInstrumentationKey)) {
  scope: functionApp
  name: '${functionAppName}-diagnostics'
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

// Function App deployment slots for staging
resource stagingSlot 'Microsoft.Web/sites/slots@2023-01-01' = if (environment == 'prod') {
  name: 'staging'
  parent: functionApp
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: config.runtime
        }
        {
          name: 'Environment'
          value: 'staging'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Staging'
        }
      ]
      alwaysOn: true
    }
  }
}

// Outputs
output functionAppId string = functionApp.id
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output principalId string = functionApp.identity.principalId
output appServicePlanId string = appServicePlan.id
output stagingSlotUrl string = environment == 'prod' ? 'https://${stagingSlot.name}-${functionApp.name}.azurewebsites.net' : ''