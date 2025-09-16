@description('Name of the SQL Server')
param sqlServerName string

@description('Location for the SQL Server')
param location string = resourceGroup().location

@description('Tags to apply to the SQL Server')
param tags object = {}

@description('Environment name')
param environment string

@description('SQL Database configuration')
param config object

@description('Key Vault name for storing secrets')
param keyVaultName string

// Get SQL admin password from Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource sqlAdminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing = {
  name: 'sql-admin-password'
  parent: keyVault
}

// SQL Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: config.administratorLogin
    administratorLoginPassword: sqlAdminPasswordSecret.properties.value
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// SQL Database
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  name: config.databaseName
  parent: sqlServer
  location: location
  tags: tags
  sku: {
    name: config.skuName
    tier: config.skuName == 'S1' ? 'Standard' : 'Standard'
    capacity: config.skuName == 'S1' ? 20 : 50
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: config.maxSizeBytes
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: environment == 'prod'
    readScale: environment == 'prod' ? 'Enabled' : 'Disabled'
    requestedBackupStorageRedundancy: environment == 'prod' ? 'Geo' : 'Local'
    isLedgerOn: false
  }
}

// Firewall rules
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  name: 'AllowAllWindowsAzureIps'
  parent: sqlServer
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Allow development IP ranges (only for dev/staging)
resource allowDevelopmentIps 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = if (environment != 'prod') {
  name: 'AllowDevelopmentIPs'
  parent: sqlServer
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '255.255.255.255'
  }
}

// SQL Database backup policies
resource backupLongTermRetentionPolicy 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-05-01-preview' = if (environment == 'prod') {
  name: 'default'
  parent: sqlDatabase
  properties: {
    weeklyRetention: 'P12W'
    monthlyRetention: 'P12M'
    yearlyRetention: 'P7Y'
    weekOfYear: 1
  }
}

resource backupShortTermRetentionPolicy 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-05-01-preview' = {
  name: 'default'
  parent: sqlDatabase
  properties: {
    retentionDays: environment == 'prod' ? 35 : 7
  }
}

// Transparent Data Encryption
resource transparentDataEncryption 'Microsoft.Sql/servers/databases/transparentDataEncryption@2023-05-01-preview' = {
  name: 'current'
  parent: sqlDatabase
  properties: {
    state: 'Enabled'
  }
}

// Auditing settings
resource auditingSettings 'Microsoft.Sql/servers/auditingSettings@2023-05-01-preview' = if (environment != 'dev') {
  name: 'default'
  parent: sqlServer
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    retentionDays: environment == 'prod' ? 365 : 30
    auditActionsAndGroups: [
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
      'BATCH_COMPLETED_GROUP'
      'DATABASE_LOGOUT_GROUP'
      'DATABASE_OBJECT_CHANGE_GROUP'
      'DATABASE_PERMISSION_CHANGE_GROUP'
      'DATABASE_PRINCIPAL_CHANGE_GROUP'
      'DATABASE_ROLE_MEMBER_CHANGE_GROUP'
      'SCHEMA_OBJECT_CHANGE_GROUP'
      'SCHEMA_OBJECT_ACCESS_GROUP'
    ]
  }
}

// Database auditing settings
resource databaseAuditingSettings 'Microsoft.Sql/servers/databases/auditingSettings@2023-05-01-preview' = if (environment != 'dev') {
  name: 'default'
  parent: sqlDatabase
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    retentionDays: environment == 'prod' ? 365 : 30
    auditActionsAndGroups: [
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
      'BATCH_COMPLETED_GROUP'
    ]
  }
}

// Threat detection policy
resource threatDetectionPolicy 'Microsoft.Sql/servers/securityAlertPolicies@2023-05-01-preview' = if (environment != 'dev') {
  name: 'default'
  parent: sqlServer
  properties: {
    state: 'Enabled'
    emailAccountAdmins: true
    emailAddresses: []
    retentionDays: environment == 'prod' ? 365 : 30
    disabledAlerts: []
  }
}

// Vulnerability assessment
resource vulnerabilityAssessment 'Microsoft.Sql/servers/vulnerabilityAssessments@2023-05-01-preview' = if (environment == 'prod') {
  name: 'default'
  parent: sqlServer
  properties: {
    storageContainerPath: 'https://${sqlServerName}vulnassess.blob.${az.environment().suffixes.storage}/vulnerability-assessment/'
    recurringScans: {
      isEnabled: true
      emailSubscriptionAdmins: true
      emails: []
    }
  }
  dependsOn: [
    threatDetectionPolicy
  ]
}

// Database vulnerability assessment
resource databaseVulnerabilityAssessment 'Microsoft.Sql/servers/databases/vulnerabilityAssessments@2023-05-01-preview' = if (environment == 'prod') {
  name: 'default'
  parent: sqlDatabase
  properties: {
    recurringScans: {
      isEnabled: true
      emailSubscriptionAdmins: true
      emails: []
    }
  }
  dependsOn: [
    vulnerabilityAssessment
  ]
}

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev') {
  scope: sqlDatabase
  name: '${config.databaseName}-diagnostics'
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
output serverId string = sqlServer.id
output serverName string = sqlServer.name
output databaseId string = sqlDatabase.id
output databaseName string = sqlDatabase.name
output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${config.databaseName};Persist Security Info=False;User ID=${config.administratorLogin};Password=${sqlAdminPasswordSecret.properties.value};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
output managedIdentityPrincipalId string = sqlServer.identity.principalId