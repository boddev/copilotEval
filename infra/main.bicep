@description('The environment name (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('The primary location for resources')
param location string = resourceGroup().location

@description('The name prefix for all resources')
param namePrefix string = 'copiloteval'

@description('Tags to apply to all resources')
param tags object = {
  Environment: environment
  Application: 'CopilotEval'
  Owner: 'DevOps'
}

@description('Enable monitoring resources (Application Insights, Log Analytics)')
param enableMonitoring bool = true

@description('SQL Database configuration')
param sqlConfig object = {
  administratorLogin: 'copilotadmin'
  administratorLoginPassword: '' // Will be set from Key Vault
  databaseName: 'copiloteval'
  skuName: environment == 'prod' ? 'S2' : 'S1'
  maxSizeBytes: environment == 'prod' ? 107374182400 : 21474836480 // 100GB prod, 20GB dev/staging
}

@description('Storage account configuration')
param storageConfig object = {
  skuName: environment == 'prod' ? 'Standard_GRS' : 'Standard_LRS'
  containers: [
    'job-data'
    'results'
    'uploads'
    'large-payloads'
  ]
}

@description('Service Bus configuration')
param serviceBusConfig object = {
  skuName: environment == 'prod' ? 'Standard' : 'Basic'
  queues: [
    {
      name: 'job-messages'
      maxDeliveryCount: 10
      lockDurationInSeconds: 300
      duplicateDetectionTimeWindow: 'PT10M'
      deadLetteringOnMessageExpiration: true
      enableBatchedOperations: true
      enablePartitioning: environment == 'prod'
      maxSizeInMegabytes: environment == 'prod' ? 5120 : 1024
    }
    {
      name: 'job-results'
      maxDeliveryCount: 5
      lockDurationInSeconds: 60
      duplicateDetectionTimeWindow: 'PT5M'
      deadLetteringOnMessageExpiration: true
      enableBatchedOperations: true
      enablePartitioning: environment == 'prod'
      maxSizeInMegabytes: environment == 'prod' ? 2048 : 512
    }
  ]
}

@description('Function App configuration')
param functionAppConfig object = {
  skuName: environment == 'prod' ? 'EP1' : 'Y1'
  skuTier: environment == 'prod' ? 'ElasticPremium' : 'Dynamic'
  runtime: 'dotnet'
  runtimeVersion: '8'
}

// Generate unique names with environment suffix
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 4)
var keyVaultName = '${namePrefix}-kv-${environment}-${uniqueSuffix}'
var storageAccountName = '${namePrefix}st${environment}${uniqueSuffix}'
var serviceBusNamespaceName = '${namePrefix}-sb-${environment}-${uniqueSuffix}'
var sqlServerName = '${namePrefix}-sql-${environment}-${uniqueSuffix}'
var functionAppName = '${namePrefix}-func-${environment}-${uniqueSuffix}'
var appServicePlanName = '${namePrefix}-plan-${environment}-${uniqueSuffix}'
var appInsightsName = '${namePrefix}-ai-${environment}-${uniqueSuffix}'
var logAnalyticsName = '${namePrefix}-law-${environment}-${uniqueSuffix}'

// Key Vault module
module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-deployment'
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    environment: environment
  }
}

// Storage Account module
module storage 'modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    storageAccountName: storageAccountName
    location: location
    tags: tags
    environment: environment
    config: storageConfig
  }
}

// Service Bus module
module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-deployment'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    environment: environment
    config: serviceBusConfig
  }
}

// SQL Database module
module database 'modules/database.bicep' = {
  name: 'database-deployment'
  params: {
    sqlServerName: sqlServerName
    location: location
    tags: tags
    environment: environment
    config: sqlConfig
    keyVaultName: keyVaultName
  }
  dependsOn: [
    keyVault
  ]
}

// Monitoring resources (optional)
module monitoring 'modules/monitoring.bicep' = if (enableMonitoring) {
  name: 'monitoring-deployment'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    location: location
    tags: tags
    environment: environment
  }
}

// Function App and App Service Plan
module functionApp 'modules/function-app.bicep' = {
  name: 'function-app-deployment'
  params: {
    functionAppName: functionAppName
    appServicePlanName: appServicePlanName
    location: location
    tags: tags
    environment: environment
    config: functionAppConfig
    storageAccountName: storageAccountName
    serviceBusConnectionString: serviceBus.outputs.connectionString
    sqlConnectionString: database.outputs.connectionString
    appInsightsInstrumentationKey: enableMonitoring ? monitoring.outputs.instrumentationKey : ''
    keyVaultName: keyVaultName
  }
  dependsOn: [
    keyVault
  ]
}

// Store connection strings in Key Vault
resource storageConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: '${keyVaultName}/storage-connection-string'
  properties: {
    value: storage.outputs.connectionString
  }
  dependsOn: [
    keyVault
  ]
}

resource serviceBusConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: '${keyVaultName}/servicebus-connection-string'
  properties: {
    value: serviceBus.outputs.connectionString
  }
  dependsOn: [
    keyVault
  ]
}

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: '${keyVaultName}/sql-connection-string'
  properties: {
    value: database.outputs.connectionString
  }
  dependsOn: [
    keyVault
  ]
}

// Outputs
output resourceGroupName string = resourceGroup().name
output keyVaultName string = keyVaultName
output storageAccountName string = storageAccountName
output serviceBusNamespace string = serviceBusNamespaceName
output sqlServerName string = sqlServerName
output functionAppName string = functionAppName
output appInsightsName string = enableMonitoring ? appInsightsName : ''

output endpoints object = {
  serviceBusNamespace: serviceBus.outputs.namespaceName
  storageAccount: storage.outputs.accountName
  sqlServer: database.outputs.serverName
  functionApp: functionApp.outputs.functionAppUrl
  keyVault: keyVault.outputs.vaultUri
}