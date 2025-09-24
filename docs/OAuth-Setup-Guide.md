# M365 Copilot Evaluation Tool - OAuth Configuration Guide

## Overview

This application now integrates with Microsoft 365 Copilot Chat API using OAuth 2.0 authentication. You'll need to register an Azure AD application and configure the backend with the appropriate credentials.

## Step 1: Azure AD App Registration

### 1.1 Create Azure AD Application

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations**
3. Click **New registration**
4. Fill in the details:
   - **Name**: `Copilot Evaluation Tool`
   - **Supported account types**: `Accounts in this organizational directory only`
   - **Redirect URI**: `Web` - `http://localhost:3000` (for development)
5. Click **Register**

### 1.2 Configure Authentication

1. In your app registration, go to **Authentication**
2. Add additional redirect URIs if needed:
   - `http://localhost:3000/`
   - `http://localhost:5173/` (Vite dev server)
3. Under **Implicit grant and hybrid flows**, check:
   - ✅ **Access tokens (used for implicit flows)**
   - ✅ **ID tokens (used for implicit and hybrid flows)**
4. Click **Save**

### 1.3 Configure API Permissions

1. Go to **API permissions**
2. Click **Add a permission**
3. Select **Microsoft Graph**
4. Choose **Delegated permissions**
5. Add the following permissions:
   - `openid`
   - `profile`
   - `User.Read`
   - `Sites.Read.All`
   - `Mail.Read`
   - `Files.Read.All`
   - `ChatMessage.Read`
   - `Team.ReadBasic.All`
   - `ChannelMessage.Read.All`

**Note**: Some permissions may require admin consent. Contact your IT administrator if needed.

### 1.4 Create Client Secret

1. Go to **Certificates & secrets**
2. Click **New client secret**
3. Add description: `Copilot Eval Tool Secret`
4. Choose expiration: `24 months` (recommended)
5. Click **Add**
6. **Important**: Copy the secret value immediately - you won't be able to see it again!

### 1.5 Note Important Values

Copy these values from your app registration:
- **Application (client) ID**: Found on the Overview page
- **Directory (tenant) ID**: Found on the Overview page  
- **Client secret**: The value you just created

## Step 2: Backend Configuration

### 2.1 Update appsettings.json

Replace the placeholder values in `appsettings.json`:

```json
{
  "AzureAd": {
    "ClientId": "YOUR_APPLICATION_CLIENT_ID_HERE",
    "ClientSecret": "YOUR_CLIENT_SECRET_HERE", 
    "TenantId": "YOUR_TENANT_ID_HERE",
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID_HERE"
  },
  "MicrosoftGraph": {
    "BaseUrl": "https://graph.microsoft.com/v1.0",
    "CopilotChatUrl": "https://graph.microsoft.com/beta/me/copilot/conversations",
    "Scopes": [
      "https://graph.microsoft.com/Sites.Read.All",
      "https://graph.microsoft.com/Mail.Read", 
      "https://graph.microsoft.com/Files.Read.All",
      "https://graph.microsoft.com/User.Read"
    ]
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:5173"]
  }
}
```

### 2.2 Update appsettings.Development.json

For development, you can use the same configuration or override specific values:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AzureAd": {
    "ClientId": "YOUR_APPLICATION_CLIENT_ID_HERE",
    "ClientSecret": "YOUR_CLIENT_SECRET_HERE",
    "TenantId": "YOUR_TENANT_ID_HERE", 
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID_HERE"
  }
}
```

## Step 3: Run the Application

### 3.1 Start the Backend

```bash
cd backend
dotnet run --project LLMResponseValidator.csproj
```

The backend will start on `http://localhost:5000`

### 3.2 Start the Frontend

```bash
cd frontend
npm run dev
```

The frontend will start on `http://localhost:5173`

## Step 4: Test OAuth Flow

1. Open your browser to `http://localhost:5173`
2. Click **"Sign in to M365"** button
3. You'll be redirected to Microsoft login
4. Sign in with your M365 account
5. Grant permissions when prompted
6. You'll be redirected back to the application
7. You should see **"Authenticated"** status in the navigation bar

## Step 5: Test M365 Copilot Integration

1. After successful authentication, add a validation entry:
   - **Prompt**: "What is the weather like today?"
   - **Expected Output**: "I don't have access to real-time weather data..."
2. Click **"Run Validation"** 
3. The application will call M365 Copilot and compare responses

## Troubleshooting

### Common Issues

1. **"Invalid client" error**: Check ClientId and ClientSecret are correct
2. **"Invalid redirect URI"**: Ensure redirect URI matches exactly in Azure AD
3. **"Insufficient privileges"**: Some permissions may need admin consent
4. **CORS errors**: Verify AllowedOrigins includes your frontend URL

### Debug Information

The backend logs detailed information about:
- OAuth token exchange
- Microsoft Graph API calls  
- M365 Copilot responses
- Error messages and stack traces

Check the console output for debugging information.

### Required Permissions Summary

The application requires these Microsoft Graph permissions:
- **User.Read**: Basic user profile
- **Sites.Read.All**: Access SharePoint sites (for Copilot context)
- **Mail.Read**: Access email (for Copilot context) 
- **Files.Read.All**: Access files (for Copilot context)

## Security Notes

1. **Never commit secrets**: Keep appsettings.json out of source control if it contains real secrets
2. **Use Azure Key Vault**: For production, store secrets in Azure Key Vault
3. **Rotate secrets regularly**: Client secrets should be rotated periodically
4. **Principle of least privilege**: Only request permissions you actually need

## M365 Copilot API Limitations

1. **Preview feature**: M365 Copilot Chat API is in beta
2. **Rate limits**: Microsoft may throttle requests
3. **Tenant requirements**: Your organization must have M365 Copilot licenses
4. **Admin approval**: Some permissions require admin consent

## Next Steps

After successful configuration:
1. Test with sample prompts and expected outputs
2. Upload CSV files with validation data
3. Analyze similarity scores and response quality
4. Export results for further analysis

For support, check the Microsoft Graph documentation or contact your IT administrator for Azure AD configuration assistance.
