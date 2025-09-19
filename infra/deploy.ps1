<#
.SYNOPSIS
  Deploy CopilotEval Bicep templates to Azure (PowerShell version of deploy.sh)

.SYNTAX
  .\deploy.ps1 -Environment dev -ResourceGroup rg-name -SubscriptionId <sub-id> [-Location "East US 2"] [-ValidateOnly] [-Yes]

.DESCRIPTION
  Validates and deploys the infra/main.bicep template using the parameter file located at infra/parameters/<environment>.parameters.json.
#>

param(
  [ValidateSet('dev','staging','prod')]
  [string]$Environment = 'dev',

  [Parameter(Mandatory=$true)]
  [string]$ResourceGroup,

  [string]$Location = 'East US 2',

  [Parameter(Mandatory=$true)]
  [string]$SubscriptionId,

  [switch]$ValidateOnly,

  [switch]$Yes
)

function Write-Info($msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Write-Success($msg) { Write-Host "[SUCCESS] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-ErrorAndExit($msg) { Write-Host "[ERROR] $msg" -ForegroundColor Red; exit 1 }

# Resolve script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$InfraDir = Resolve-Path "$ScriptDir"

Write-Info "Starting CopilotEval infra deployment"
Write-Info "Environment: $Environment"
Write-Info "Resource Group: $ResourceGroup"
Write-Info "Location: $Location"
Write-Info "Subscription: $SubscriptionId"

# Ensure Azure CLI available
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
  Write-ErrorAndExit "Azure CLI (az) not found. Please install Azure CLI before running this script."
}

# Ensure logged in
try {
  az account show --output none 2>$null
} catch {
  Write-ErrorAndExit "Not logged in to Azure CLI. Run 'az login' first."
}

# Set subscription
Write-Info "Setting Azure subscription to $SubscriptionId"
az account set --subscription $SubscriptionId | Out-Null

# Validate environment
if (-not ($Environment -in @('dev','staging','prod'))) {
  Write-ErrorAndExit "Environment must be one of: dev, staging, prod"
}

# Parameter file path
$ParamFile = Join-Path $InfraDir "parameters\$($Environment).parameters.json"
if (-not (Test-Path $ParamFile)) {
  Write-ErrorAndExit "Parameters file not found: $ParamFile"
}

# Ensure resource group exists (or create)
$rgExists = $false
try {
  az group show --name $ResourceGroup --output none 2>$null
  $rgExists = $true
} catch {
  $rgExists = $false
}

if (-not $rgExists) {
  if (-not $Yes) {
    $confirm = Read-Host "Resource group '$ResourceGroup' does not exist. Create it in '$Location'? (y/N)"
    if ($confirm.ToLower() -ne 'y') { Write-ErrorAndExit "Resource group required. Exiting." }
  }
  Write-Info "Creating resource group '$ResourceGroup' in '$Location'..."
  az group create --name $ResourceGroup --location "$Location" | Out-Null
  Write-Success "Resource group created"
} else {
  Write-Success "Resource group '$ResourceGroup' already exists"
}

# Validate Bicep template
$TemplateFile = Join-Path $InfraDir 'main.bicep'
Write-Info "Validating Bicep template: $TemplateFile"
$validateResult = az deployment group validate --resource-group $ResourceGroup --template-file $TemplateFile --parameters "@$ParamFile" --output json 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-ErrorAndExit "Template validation failed:`n$validateResult"
}
Write-Success "Template validation passed"

if ($ValidateOnly) {
  Write-Success "Validation-only mode enabled. Exiting."
  exit 0
}

# Summary / confirmation
Write-Info "The deployment will create resources defined in main.bicep using parameters file: $ParamFile"
if (-not $Yes) {
  $cont = Read-Host "Continue with deployment to resource group '$ResourceGroup'? (y/N)"
  if ($cont.ToLower() -ne 'y') { Write-Warn "Deployment cancelled by user"; exit 0 }
}

# Deploy
$deploymentName = "copiloteval-infra-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Write-Info "Starting deployment '$deploymentName'..."
az deployment group create --resource-group $ResourceGroup --template-file $TemplateFile --parameters "@$ParamFile" --name $deploymentName --output table

if ($LASTEXITCODE -eq 0) {
  Write-Success "Deployment completed successfully"
  Write-Info "Retrieving deployment outputs..."
  az deployment group show --resource-group $ResourceGroup --name $deploymentName --query "properties.outputs" --output table
  Write-Host "`nNext steps:`n  - Update Key Vault secrets with runtime values (SQL admin, API keys).`n  - Deploy backend/frontend apps or configure CI/CD to use the deployed infrastructure.`n  - Run smoke tests to verify connectivity." -ForegroundColor Cyan
} else {
  Write-ErrorAndExit "Deployment failed. See Azure CLI output for details."
}

