# Azure AD Authentication & Local Development Setup

This document provides instructions for configuring Azure AD authentication for local development and understanding the production deployment configuration.

## Overview

The CopilotEval backend now includes Azure AD authentication and managed identity support:

- **Production**: Uses managed identities for secure access to Azure resources
- **Development**: Uses local configuration with environment variables or user secrets

## Local Development Setup

### Prerequisites

1. Azure AD tenant with appropriate permissions
2. Azure AD App Registration (see OAuth-Setup-Guide.md for details)
3. Access to Azure Key Vault (for production deployment)

### Configuration Options

#### Option 1: Environment Variables (Recommended)

Set the following environment variables in your development environment:

```bash
export AZURE_CLIENT_ID="your-app-registration-client-id"
export AZURE_CLIENT_SECRET="your-app-registration-client-secret"  
export AZURE_TENANT_ID="your-azure-ad-tenant-id"
export KEYVAULT_VAULT_URL="https://your-keyvault.vault.azure.net/"
```

These are already configured in `Properties/launchSettings.json`.

#### Option 2: Update appsettings.Development.json

Replace the placeholder values in `backend/appsettings.Development.json`:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "your-domain.onmicrosoft.com",
    "TenantId": "your-tenant-id-here",
    "ClientId": "your-client-id-here",
    "ClientSecret": "your-client-secret-here",
    "Audience": "your-client-id-here"
  },
  "KeyVault": {
    "VaultUrl": "https://your-keyvault.vault.azure.net/"
  }
}
```

#### Option 3: .NET User Secrets (Most Secure)

Initialize and set user secrets:

```bash
cd backend
dotnet user-secrets init
dotnet user-secrets set "AzureAd:ClientId" "your-client-id"
dotnet user-secrets set "AzureAd:ClientSecret" "your-client-secret"
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id"
dotnet user-secrets set "KeyVault:VaultUrl" "https://your-keyvault.vault.azure.net/"
```

## Production Configuration

### Managed Identity Access

In production, the application uses managed identity to access:

- **Azure Key Vault**: For retrieving secrets (connection strings, API keys)
- **Service Bus**: For secure message queue access
- **Other Azure resources**: As needed

### Key Vault Secrets

The following secrets are expected in Key Vault:

- `azuread-client-secret`: Azure AD app registration client secret
- `servicebus-connection-string`: Service Bus namespace connection
- `sql-connection-string`: Database connection string
- `copilot-api-key`: Microsoft Copilot API key (if needed)

### Infrastructure Deployment

The bicep templates in `infra/modules/keyvault.bicep` include:

- RBAC role assignments for managed identities
- Automatic secret access for backend and worker applications
- Environment-specific security configurations

## Testing Authentication

### Verify Endpoints are Protected

```bash
# Health check (unprotected) - should return 200
curl http://localhost:5000/api/health

# Protected endpoints (without auth) - should return 401
curl http://localhost:5000/api/copilot/agents
curl http://localhost:5000/api/copilot/chat
curl http://localhost:5000/api/similarity/score
```

### OAuth Flow Testing

```bash
# Get OAuth URL (should work)
curl http://localhost:5000/api/auth/url

# Test with valid Bearer token
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" http://localhost:5000/api/copilot/agents
```

## Security Considerations

### Development

- Never commit secrets to source control
- Use environment variables or user secrets for local development
- Ensure `appsettings.json` remains in `.gitignore`

### Production

- Managed identities eliminate need for secrets in configuration
- Key Vault provides secure secret storage
- RBAC ensures least-privilege access
- All communications use HTTPS

## Troubleshooting

### Common Issues

1. **401 Unauthorized**: Check that JWT token is valid and not expired
2. **403 Forbidden**: Verify the token audience matches the configured audience
3. **JWT validation failed**: Ensure tenant ID and authority are correct
4. **Key Vault access denied**: Verify managed identity has proper RBAC permissions

### Debug Logging

The application logs authentication events:

- Token validation success/failure
- Managed identity usage
- Key Vault access attempts

Check logs for detailed error information.

## Next Steps

1. Deploy infrastructure using bicep templates
2. Configure Azure AD app registration permissions
3. Set up managed identities in Azure
4. Deploy application with managed identity configuration
5. Verify end-to-end authentication flow

For more details on OAuth setup, see `OAuth-Setup-Guide.md`.