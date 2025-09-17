@description('Name of the Key Vault')
param keyVaultName string

@description('Location for the Key Vault')
param location string = resourceGroup().location

@description('Tags to apply to the Key Vault')
param tags object = {}

@description('Environment name')
param environment string

@description('SKU name for the Key Vault')
param skuName string = 'standard'

@description('Enable RBAC authorization instead of access policies')
param enableRbacAuthorization bool = true

@description('Enable soft delete')
param enableSoftDelete bool = true

@description('Soft delete retention in days')
param softDeleteRetentionInDays int = environment == 'prod' ? 90 : 7

@description('Enable purge protection')
param enablePurgeProtection bool = environment == 'prod'

@description('Enable vault for disk encryption')
param enabledForDiskEncryption bool = false

@description('Enable vault for template deployment')
param enabledForTemplateDeployment bool = true

@description('Enable vault for deployment')
param enabledForDeployment bool = false

@description('Network ACLs for the Key Vault')
param networkAcls object = {
  bypass: 'AzureServices'
  defaultAction: 'Allow'
  ipRules: []
  virtualNetworkRules: []
}

@description('Principal IDs that need access to Key Vault secrets (backend app, worker function)')
param principalIds array = []

// Key Vault resource
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: skuName
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: enableRbacAuthorization
    enableSoftDelete: enableSoftDelete
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: enablePurgeProtection ? true : null
    enabledForDiskEncryption: enabledForDiskEncryption
    enabledForTemplateDeployment: enabledForTemplateDeployment
    enabledForDeployment: enabledForDeployment
    networkAcls: networkAcls
    publicNetworkAccess: 'Enabled'
  }
}

// Create initial secrets placeholders
resource sqlAdminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'sql-admin-password'
  parent: keyVault
  properties: {
    value: 'P@ssw0rd123!' // This should be updated with a proper generated password
    attributes: {
      enabled: true
    }
  }
}

resource copilotApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'copilot-api-key'
  parent: keyVault
  properties: {
    value: 'placeholder-api-key' // This should be updated with the actual API key
    attributes: {
      enabled: true
    }
  }
}

resource functionAppKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'function-app-key'
  parent: keyVault
  properties: {
    value: 'func-key-${uniqueString(resourceGroup().id)}' // Generate a unique key for function app authentication
    attributes: {
      enabled: true
    }
  }
}

// RBAC role assignments for managed identities to access Key Vault secrets
resource keyVaultSecretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in principalIds: {
  name: guid(keyVault.id, principalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

// Additional secrets for Service Bus and other services
resource serviceBusConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'servicebus-connection-string'
  parent: keyVault
  properties: {
    value: 'placeholder-servicebus-connection' // This will be updated by the infrastructure deployment
    attributes: {
      enabled: true
    }
  }
}

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'sql-connection-string'
  parent: keyVault
  properties: {
    value: 'placeholder-sql-connection' // This will be updated by the infrastructure deployment
    attributes: {
      enabled: true
    }
  }
}

resource azureAdClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'azuread-client-secret'
  parent: keyVault
  properties: {
    value: 'placeholder-client-secret' // This should be updated with the actual client secret
    attributes: {
      enabled: true
    }
  }
}

// Diagnostic settings for monitoring (if environment is not dev)
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (environment != 'dev') {
  scope: keyVault
  name: '${keyVaultName}-diagnostics'
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
output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output vaultUri string = keyVault.properties.vaultUri
output tenantId string = keyVault.properties.tenantId