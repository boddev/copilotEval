#!/bin/bash

# Copilot Evaluation Infrastructure Deployment Script
# This script deploys the Bicep templates to Azure

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
ENVIRONMENT="dev"
RESOURCE_GROUP=""
LOCATION="East US 2"
SUBSCRIPTION_ID=""
VALIDATE_ONLY=false
SKIP_CONFIRMATION=false

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  -e, --environment       Environment (dev|staging|prod) [default: dev]"
    echo "  -g, --resource-group    Resource group name (required)"
    echo "  -l, --location          Azure region [default: East US 2]"
    echo "  -s, --subscription      Azure subscription ID (required)"
    echo "  -v, --validate-only     Only validate templates, don't deploy"
    echo "  -y, --yes               Skip confirmation prompts"
    echo "  -h, --help              Show this help message"
    echo ""
    echo "Example:"
    echo "  $0 -e dev -g rg-copiloteval-dev -s 12345678-1234-1234-1234-123456789012"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        -g|--resource-group)
            RESOURCE_GROUP="$2"
            shift 2
            ;;
        -l|--location)
            LOCATION="$2"
            shift 2
            ;;
        -s|--subscription)
            SUBSCRIPTION_ID="$2"
            shift 2
            ;;
        -v|--validate-only)
            VALIDATE_ONLY=true
            shift
            ;;
        -y|--yes)
            SKIP_CONFIRMATION=true
            shift
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Validate required parameters
if [[ -z "$RESOURCE_GROUP" ]]; then
    print_error "Resource group name is required"
    show_usage
    exit 1
fi

if [[ -z "$SUBSCRIPTION_ID" ]]; then
    print_error "Subscription ID is required"
    show_usage
    exit 1
fi

# Validate environment
if [[ ! "$ENVIRONMENT" =~ ^(dev|staging|prod)$ ]]; then
    print_error "Environment must be one of: dev, staging, prod"
    exit 1
fi

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$SCRIPT_DIR"

print_status "Starting CopilotEval infrastructure deployment"
print_status "Environment: $ENVIRONMENT"
print_status "Resource Group: $RESOURCE_GROUP"
print_status "Location: $LOCATION"
print_status "Subscription: $SUBSCRIPTION_ID"

if [ "$VALIDATE_ONLY" = true ]; then
    print_status "Validation mode: Templates will be validated but not deployed"
fi

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    print_error "Azure CLI is not installed. Please install it first."
    exit 1
fi

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    print_error "Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

# Set subscription
print_status "Setting Azure subscription to $SUBSCRIPTION_ID"
az account set --subscription "$SUBSCRIPTION_ID"

# Check if resource group exists
print_status "Checking if resource group exists..."
if ! az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    if [ "$SKIP_CONFIRMATION" = false ]; then
        read -p "Resource group '$RESOURCE_GROUP' does not exist. Create it? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            print_error "Resource group is required. Exiting."
            exit 1
        fi
    fi
    
    print_status "Creating resource group '$RESOURCE_GROUP' in '$LOCATION'"
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
    print_success "Resource group created successfully"
else
    print_success "Resource group '$RESOURCE_GROUP' already exists"
fi

# Validate Bicep template
print_status "Validating Bicep template..."
VALIDATION_RESULT=$(az deployment group validate \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$INFRA_DIR/main.bicep" \
    --parameters "@$INFRA_DIR/parameters/$ENVIRONMENT.parameters.json" \
    --output json 2>&1)

if [ $? -ne 0 ]; then
    print_error "Template validation failed:"
    echo "$VALIDATION_RESULT"
    exit 1
fi

print_success "Template validation passed"

if [ "$VALIDATE_ONLY" = true ]; then
    print_success "Validation completed successfully. Exiting (validation-only mode)."
    exit 0
fi

# Show what will be deployed
print_status "Deployment will create the following resources:"
echo "  - Key Vault for secrets management"
echo "  - Storage Account with containers for job data"
echo "  - Service Bus namespace with queues for job processing"
echo "  - SQL Database for job tracking and results"
echo "  - Function App for asynchronous job processing"
if [[ "$ENVIRONMENT" != "dev" ]]; then
    echo "  - Application Insights and Log Analytics for monitoring"
    echo "  - Alert rules and action groups"
fi

if [ "$SKIP_CONFIRMATION" = false ]; then
    read -p "Continue with deployment? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_warning "Deployment cancelled by user"
        exit 0
    fi
fi

# Deploy the template
print_status "Starting deployment to resource group '$RESOURCE_GROUP'..."
DEPLOYMENT_NAME="copiloteval-infra-$(date +%Y%m%d-%H%M%S)"

az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$INFRA_DIR/main.bicep" \
    --parameters "@$INFRA_DIR/parameters/$ENVIRONMENT.parameters.json" \
    --name "$DEPLOYMENT_NAME" \
    --output table

if [ $? -eq 0 ]; then
    print_success "Deployment completed successfully!"
    
    # Get deployment outputs
    print_status "Retrieving deployment outputs..."
    az deployment group show \
        --resource-group "$RESOURCE_GROUP" \
        --name "$DEPLOYMENT_NAME" \
        --query "properties.outputs" \
        --output table
    
    print_status "Next steps:"
    echo "1. Update Key Vault secrets with actual values:"
    echo "   - sql-admin-password: Set a strong password for SQL admin"
    echo "   - copilot-api-key: Set your Copilot API key"
    echo "2. Deploy your Function App code to the created Function App"
    echo "3. Update your application configuration to use the deployed resources"
    echo "4. Run smoke tests to verify connectivity"
    
else
    print_error "Deployment failed!"
    exit 1
fi