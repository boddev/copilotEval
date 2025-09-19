<#
Simple Azure CLI deployment script (PowerShell) driven by a parameters JSON file.
This creates a minimal set of resources described in infra/parameters/dev.parameters.json.

Usage:
  .\deploy_cli.ps1 -ParametersFile ./parameters/dev.parameters.json -ResourceGroup rg-copiloteval-dev -SubscriptionId <sub-id> [-Location eastus] [-Yes]
#>

param(
  [string]$ParametersFile = "./parameters/dev.parameters.json",
  [Parameter(Mandatory=$true)] [string]$ResourceGroup,
  [Parameter(Mandatory=$true)] [string]$SubscriptionId,
  [string]$Location = "eastus",
  [switch]$Yes
)

function Info($m) { Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Success($m) { Write-Host "[OK] $m" -ForegroundColor Green }
function Err($m) { Write-Host "[ERR] $m" -ForegroundColor Red }

# Ensure Azure CLI installed
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Err "Azure CLI not found. Install az first."; exit 1 }

# Read parameters file
if (-not (Test-Path $ParametersFile)) { Err "Parameters file not found: $ParametersFile"; exit 1 }
$raw = Get-Content $ParametersFile -Raw | ConvertFrom-Json

# Extract values
$namePrefix = $raw.parameters.namePrefix.value
$enableMonitoring = $raw.parameters.enableMonitoring.value
$storageSku = $raw.parameters.storageConfig.value.skuName
$containers = $raw.parameters.storageConfig.value.containers
$sbSku = $raw.parameters.serviceBusConfig.value.skuName
$queues = $raw.parameters.serviceBusConfig.value.queues
$functionConfig = $raw.parameters.functionAppConfig.value
$sqlConfig = $raw.parameters.sqlConfig.value
$tags = $raw.parameters.tags.value

Info "Using name prefix: $namePrefix"

# Login & set subscription context
try { az account show --output none } catch { Err "Not logged into Azure CLI. Run 'az login' first."; exit 1 }
az account set --subscription $SubscriptionId | Out-Null

# Create resource group if needed
$rgExists = $false
try { az group show --name $ResourceGroup --output none; $rgExists = $true } catch { $rgExists = $false }
if (-not $rgExists) {
  if (-not $Yes) {
    $reply = Read-Host "Resource group '$ResourceGroup' does not exist. Create it in '$Location'? (y/N)"
    if ($reply.ToLower() -ne 'y') { Err "Aborting"; exit 1 }
  }
  Info "Creating resource group $ResourceGroup..."
  az group create --name $ResourceGroup --location $Location | Out-Null
  Success "Resource group created"
} else { Success "Resource group $ResourceGroup exists" }

# Create storage account
$storageName = ($namePrefix + 'st' + (Get-Random -Maximum 9999)).ToLower()
Info "Creating storage account: $storageName (sku: $storageSku)"
az storage account create --name $storageName --resource-group $ResourceGroup --location $Location --sku $storageSku --kind StorageV2 | Out-Null
$storageConn = az storage account show-connection-string --name $storageName --resource-group $ResourceGroup -o tsv
if (-not $storageConn) { Err "Failed to get storage connection string"; exit 1 }
Success "Storage account created: $storageName"

# Create containers
foreach ($c in $containers) {
  Info "Creating container: $c"
  az storage container create --name $c --account-name $storageName --connection-string $storageConn | Out-Null
}
Success "Storage containers created"

# Create Service Bus namespace
$sbName = ($namePrefix + 'sb' + (Get-Random -Maximum 9999)).ToLower()
Info "Creating Service Bus namespace: $sbName (sku: $sbSku)"
# Ensure servicebus extension is available (some CLI installs require it)
Info "Ensuring 'servicebus' CLI extension is present"
az extension add --name servicebus --yes 2>$null | Out-Null
az extension list --query "[?name=='servicebus'] | length(@)" -o tsv | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Warn "Could not verify/add 'servicebus' extension; continuing and hoping commands are available." }
az servicebus namespace create --resource-group $ResourceGroup --name $sbName --location $Location --sku $sbSku | Out-Null

# Create queues
foreach ($q in $queues) {
  $qName = $q.name
  Info "Creating queue: $qName"
  az servicebus queue create --resource-group $ResourceGroup --namespace-name $sbName --name $qName | Out-Null
  if ($LASTEXITCODE -ne 0) { Err "Failed to create Service Bus queue: $qName"; exit 1 }
}
Success "Service Bus namespace and queues created"

# Create Function App (Consumption) – requires a storage account
$funcName = ($namePrefix + 'func' + (Get-Random -Maximum 9999)).ToLower()
Info "Creating Function App: $funcName"
# Choose appropriate Functions runtime version based on requested runtime
$functionsVersion = 4
switch ($functionConfig.runtime.ToLower()) {
  'dotnet' { $functionsVersion = 4 }
  'node'   { $functionsVersion = 4 }
  'python' { $functionsVersion = 4 }
  default  { $functionsVersion = 4 }
}
Info "Using Functions runtime version: $functionsVersion"
az functionapp create --resource-group $ResourceGroup --name $funcName --consumption-plan-location $Location --runtime $functionConfig.runtime --runtime-version $functionConfig.runtimeVersion --functions-version $functionsVersion --storage-account $storageName | Out-Null
Success "Function App created: $funcName"

# Create SQL Server + Database
if ($sqlConfig.databaseName) {
  $sqlServerName = ($namePrefix + 'sql' + (Get-Random -Maximum 9999)).ToLower()
  $adminUser = $sqlConfig.administratorLogin
  $adminPass = $sqlConfig.administratorLoginPassword
  if ([string]::IsNullOrWhiteSpace($adminPass)) {
    Write-Host "SQL admin password not provided in parameters. Please enter a strong password (required to create SQL server):"
    $secure = Read-Host -AsSecureString
    $adminPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
  }

  Info "Creating SQL server: $sqlServerName"
  az sql server create --name $sqlServerName --resource-group $ResourceGroup --location $Location --admin-user $adminUser --admin-password $adminPass | Out-Null
  Success "SQL server created: $sqlServerName"

  Info "Creating SQL database: $($sqlConfig.databaseName)"
  az sql db create --resource-group $ResourceGroup --server $sqlServerName --name $sqlConfig.databaseName --service-objective $sqlConfig.skuName --max-size $sqlConfig.maxSizeBytes | Out-Null
  Success "SQL database created: $($sqlConfig.databaseName)"
}

# Backend Web App: create App Service plan + Web App, set app settings from backend/appsettings.Development.json, then deploy
$appPlanName = ($namePrefix + 'plan' + (Get-Random -Maximum 9999)).ToLower()
$backendName = ($namePrefix + 'web' + (Get-Random -Maximum 9999)).ToLower()
Info "Creating App Service plan: $appPlanName (sku B1)"
az appservice plan create --name $appPlanName --resource-group $ResourceGroup --is-linux --sku B1 | Out-Null
if ($LASTEXITCODE -ne 0) { Err "Failed to create App Service plan: $appPlanName"; exit 1 }

# Create the Web App resource with linuxFxVersion set using a direct ARM resource create (avoids shell parsing of '|')
$planId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/serverfarms/$appPlanName"
$siteResId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$backendName"
$siteBody = @{ location = $Location; properties = @{ serverFarmId = $planId; siteConfig = @{ linuxFxVersion = "DOTNET|8" } } } | ConvertTo-Json -Depth 10
# Compact JSON to avoid newline/formatting issues when passing as a single CLI argument
$siteBodyCompact = $siteBody -replace "`r?`n", ''
# Write JSON payload to temp file and pass via @file to az to avoid CLI parsing issues on Windows
$tmpFile = Join-Path $env:TEMP ("siteprops_$backendName.json")
Set-Content -Path $tmpFile -Value $siteBodyCompact -NoNewline -Force
Info "Writing site payload to $tmpFile and creating Web App resource"
az resource create --id $siteResId --api-version 2021-02-01 --properties "@$tmpFile" | Out-Null
if ($LASTEXITCODE -ne 0) { Err "Failed to create Web App resource: $backendName"; exit 1 }
Remove-Item $tmpFile -ErrorAction SilentlyContinue

# Read backend appsettings file and flatten into key=value pairs suitable for `az webapp config appsettings set`
$appSettingsFile = Join-Path $PSScriptRoot "..\backend\appsettings.Development.json"
if (Test-Path $appSettingsFile) {
  $json = Get-Content $appSettingsFile -Raw | ConvertFrom-Json
  $flat = @{
  }
  function Flatten-Object($obj, $prefix) {
    foreach ($p in $obj.psobject.Properties) {
      $key = $p.Name
      $val = $p.Value
      if ($val -is [System.Management.Automation.PSCustomObject] -or $val -is [hashtable]) {
        Flatten-Object $val ("$prefix$key" + "__")
      } else {
        $flat["$prefix$key"] = $val
      }
    }
  }
  Flatten-Object $json ""

  # Add connection strings from created resources
  $sbConn = az servicebus namespace authorization-rule keys list --resource-group $ResourceGroup --namespace-name $sbName --name RootManageSharedAccessKey --query primaryConnectionString -o tsv
  if ($sbConn) { $flat['ConnectionStrings__ServiceBus'] = $sbConn }
  if ($storageConn) { $flat['ConnectionStrings__BlobStorage'] = $storageConn }

  # Build settings array (key=value)
  $settingsList = @()
  foreach ($k in $flat.Keys) { $settingsList += "$k=$($flat[$k])" }

  Info "Setting app settings on $backendName"
  az webapp config appsettings set --resource-group $ResourceGroup --name $backendName --settings $settingsList | Out-Null
  if ($LASTEXITCODE -ne 0) { Err "Failed to set app settings on $backendName"; exit 1 }
}
else {
  Write-Warn "Backend appsettings file not found: $appSettingsFile — skipping automatic app settings population"
}

### Publish and deploy backend via zip deploy
Info "Publishing backend project..."
dotnet publish ./backend/CopilotEvalApi.csproj -c Release -o ./backend_publish
if ($LASTEXITCODE -ne 0) { Err "dotnet publish failed"; exit 1 }
$zipPath = "./backend_app.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path ./backend_publish/* -DestinationPath $zipPath -Force

Info "Deploying backend (zip) to $backendName..."
az webapp deployment source config-zip --resource-group $ResourceGroup --name $backendName --src $zipPath | Out-Null
if ($LASTEXITCODE -ne 0) { Err "Zip deployment to Web App failed"; exit 1 }
Success "Backend deployed to Web App: $backendName"

# Enable static website hosting and upload frontend
Info "Enabling static website hosting on storage account: $storageName"
az storage blob service-properties update --account-name $storageName --static-website --index-document index.html --404-document index.html --connection-string $storageConn | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Warn "Failed to enable static website on storage account: $storageName" } else {
  $webEndpoint = az storage account show --name $storageName --resource-group $ResourceGroup --query "primaryEndpoints.web" -o tsv
  Info "Static website endpoint: $webEndpoint"

  # Build frontend
  $frontendPath = Join-Path $PSScriptRoot "..\frontend"
  if (Test-Path $frontendPath) {
    Info "Building frontend at $frontendPath"
    Push-Location $frontendPath
    # Set Vite env var so the frontend is built pointing to the deployed backend API
    $env:VITE_API_BASE = "https://$backendName.azurewebsites.net/api"
    npm install --silent
    npm run build --silent
    Pop-Location

    $distPath = Join-Path $frontendPath "dist"
    if (Test-Path $distPath) {
      Info "Uploading frontend to storage account '\$web' container"
      az storage blob upload-batch --account-name $storageName --source $distPath --destination '$web' --connection-string $storageConn | Out-Null
      if ($LASTEXITCODE -ne 0) { Err "Failed to upload frontend to storage account" } else { Success "Frontend uploaded: $webEndpoint" }
    } else { Write-Warn "Frontend build output not found at: $distPath" }
  } else { Write-Warn "Frontend folder not found at path: $frontendPath" }
}

# Tagging - apply tags to resource group (will inherit)
if ($tags) {
  $tagArgs = @()
  foreach ($k in $tags.Keys) { $tagArgs += "$k=$($tags[$k])" }
  Info "Applying tags to resource group"
  az group update --name $ResourceGroup --tags $tagArgs | Out-Null
  if ($LASTEXITCODE -ne 0) { Err "Failed to apply tags to resource group"; exit 1 }
  Success "Tags applied"
}

Info "Deployment (CLI-driven) completed. Outputting key names:" 
Write-Host "  Resource Group: $ResourceGroup"
Write-Host "  Storage Account: $storageName"
Write-Host "  Function App: $funcName"
Write-Host "  Service Bus Namespace: $sbName"
if ($sqlServerName) { Write-Host "  SQL Server: $sqlServerName, Database: $($sqlConfig.databaseName)" }
Write-Host "  Backend Web App: $backendName"

Write-Host "NOTE: This script uses the simplified defaults from the dev parameters and does not implement every platform-specific property present in the Bicep templates (monitoring, advanced alert rules, Key Vault secrets, fine-grained queue settings)."
Write-Host "If you want those additional features, I can extend the script to create them one-by-one."

