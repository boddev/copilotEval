#!/bin/bash

# Copilot Evaluation Infrastructure Smoke Tests
# This script performs connectivity and basic functionality tests

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
SUBSCRIPTION_ID=""

# Test results
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

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

# Test result functions
test_passed() {
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    PASSED_TESTS=$((PASSED_TESTS + 1))
    print_success "✅ $1"
}

test_failed() {
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    FAILED_TESTS=$((FAILED_TESTS + 1))
    print_error "❌ $1"
}

test_warning() {
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    print_warning "⚠️  $1"
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  -e, --environment       Environment (dev|staging|prod) [default: dev]"
    echo "  -g, --resource-group    Resource group name (required)"
    echo "  -s, --subscription      Azure subscription ID (required)"
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
        -s|--subscription)
            SUBSCRIPTION_ID="$2"
            shift 2
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

print_status "Starting CopilotEval infrastructure smoke tests"
print_status "Environment: $ENVIRONMENT"
print_status "Resource Group: $RESOURCE_GROUP"
print_status "Subscription: $SUBSCRIPTION_ID"

# Set subscription
az account set --subscription "$SUBSCRIPTION_ID"

# Get resource names
print_status "Discovering deployed resources..."
UNIQUE_STRING=$(az group show --name "$RESOURCE_GROUP" --query "id" --output tsv | md5sum | cut -c1-4)
NAME_PREFIX="copiloteval"

KEY_VAULT_NAME="${NAME_PREFIX}-kv-${ENVIRONMENT}-${UNIQUE_STRING}"
STORAGE_ACCOUNT_NAME="${NAME_PREFIX}st${ENVIRONMENT}${UNIQUE_STRING}"
SERVICE_BUS_NAMESPACE="${NAME_PREFIX}-sb-${ENVIRONMENT}-${UNIQUE_STRING}"
SQL_SERVER_NAME="${NAME_PREFIX}-sql-${ENVIRONMENT}-${UNIQUE_STRING}"
FUNCTION_APP_NAME="${NAME_PREFIX}-func-${ENVIRONMENT}-${UNIQUE_STRING}"

print_status "Testing resource names:"
echo "  Key Vault: $KEY_VAULT_NAME"
echo "  Storage Account: $STORAGE_ACCOUNT_NAME"
echo "  Service Bus: $SERVICE_BUS_NAMESPACE"
echo "  SQL Server: $SQL_SERVER_NAME"
echo "  Function App: $FUNCTION_APP_NAME"

echo ""
print_status "🧪 Running smoke tests..."
echo ""

# Test 1: Resource Group exists
print_status "Test 1: Checking if resource group exists..."
if az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "Resource group '$RESOURCE_GROUP' exists"
else
    test_failed "Resource group '$RESOURCE_GROUP' not found"
fi

# Test 2: Key Vault accessibility
print_status "Test 2: Checking Key Vault accessibility..."
if az keyvault show --name "$KEY_VAULT_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "Key Vault '$KEY_VAULT_NAME' is accessible"
    
    # Test Key Vault secrets
    print_status "Test 2a: Checking Key Vault secrets..."
    if az keyvault secret list --vault-name "$KEY_VAULT_NAME" &> /dev/null; then
        SECRET_COUNT=$(az keyvault secret list --vault-name "$KEY_VAULT_NAME" --query "length(@)" --output tsv)
        if [ "$SECRET_COUNT" -gt 0 ]; then
            test_passed "Key Vault contains $SECRET_COUNT secrets"
        else
            test_warning "Key Vault exists but contains no secrets"
        fi
    else
        test_failed "Cannot list Key Vault secrets (permission issue)"
    fi
else
    test_failed "Key Vault '$KEY_VAULT_NAME' is not accessible"
fi

# Test 3: Storage Account accessibility
print_status "Test 3: Checking Storage Account accessibility..."
if az storage account show --name "$STORAGE_ACCOUNT_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "Storage Account '$STORAGE_ACCOUNT_NAME' is accessible"
    
    # Test storage containers
    print_status "Test 3a: Checking storage containers..."
    ACCOUNT_KEY=$(az storage account keys list --account-name "$STORAGE_ACCOUNT_NAME" --resource-group "$RESOURCE_GROUP" --query "[0].value" --output tsv 2>/dev/null)
    if [ $? -eq 0 ]; then
        CONTAINERS=$(az storage container list --account-name "$STORAGE_ACCOUNT_NAME" --account-key "$ACCOUNT_KEY" --query "length(@)" --output tsv 2>/dev/null)
        if [ "$CONTAINERS" -gt 0 ]; then
            test_passed "Storage Account contains $CONTAINERS containers"
        else
            test_warning "Storage Account exists but contains no containers"
        fi
    else
        test_failed "Cannot access Storage Account keys (permission issue)"
    fi
else
    test_failed "Storage Account '$STORAGE_ACCOUNT_NAME' is not accessible"
fi

# Test 4: Service Bus accessibility
print_status "Test 4: Checking Service Bus accessibility..."
if az servicebus namespace show --name "$SERVICE_BUS_NAMESPACE" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "Service Bus namespace '$SERVICE_BUS_NAMESPACE' is accessible"
    
    # Test Service Bus queues
    print_status "Test 4a: Checking Service Bus queues..."
    QUEUES=$(az servicebus queue list --namespace-name "$SERVICE_BUS_NAMESPACE" --resource-group "$RESOURCE_GROUP" --query "length(@)" --output tsv 2>/dev/null)
    if [ "$QUEUES" -gt 0 ]; then
        test_passed "Service Bus contains $QUEUES queues"
        
        # Test specific queues
        if az servicebus queue show --name "job-messages" --namespace-name "$SERVICE_BUS_NAMESPACE" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
            test_passed "job-messages queue exists"
        else
            test_failed "job-messages queue not found"
        fi
        
        if az servicebus queue show --name "job-results" --namespace-name "$SERVICE_BUS_NAMESPACE" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
            test_passed "job-results queue exists"
        else
            test_failed "job-results queue not found"
        fi
    else
        test_failed "Service Bus exists but contains no queues"
    fi
else
    test_failed "Service Bus namespace '$SERVICE_BUS_NAMESPACE' is not accessible"
fi

# Test 5: SQL Server accessibility
print_status "Test 5: Checking SQL Server accessibility..."
if az sql server show --name "$SQL_SERVER_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "SQL Server '$SQL_SERVER_NAME' is accessible"
    
    # Test SQL Database
    print_status "Test 5a: Checking SQL Database..."
    DB_NAME="copiloteval-${ENVIRONMENT}"
    if az sql db show --server "$SQL_SERVER_NAME" --name "$DB_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
        test_passed "SQL Database '$DB_NAME' exists"
    else
        test_failed "SQL Database '$DB_NAME' not found"
    fi
else
    test_failed "SQL Server '$SQL_SERVER_NAME' is not accessible"
fi

# Test 6: Function App accessibility
print_status "Test 6: Checking Function App accessibility..."
if az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    test_passed "Function App '$FUNCTION_APP_NAME' is accessible"
    
    # Test Function App status
    print_status "Test 6a: Checking Function App status..."
    STATE=$(az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" --query "state" --output tsv)
    if [ "$STATE" == "Running" ]; then
        test_passed "Function App is running"
    else
        test_warning "Function App state is '$STATE' (expected: Running)"
    fi
    
    # Test Function App URL
    print_status "Test 6b: Checking Function App URL accessibility..."
    FUNCTION_URL=$(az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" --query "defaultHostName" --output tsv)
    if curl -s -o /dev/null -w "%{http_code}" "https://$FUNCTION_URL" | grep -q "200\|401\|403"; then
        test_passed "Function App URL is accessible at https://$FUNCTION_URL"
    else
        test_warning "Function App URL may not be accessible (no response or error)"
    fi
else
    test_failed "Function App '$FUNCTION_APP_NAME' is not accessible"
fi

# Test 7: Monitoring resources (if enabled)
if [[ "$ENVIRONMENT" != "dev" ]]; then
    print_status "Test 7: Checking monitoring resources..."
    APP_INSIGHTS_NAME="${NAME_PREFIX}-ai-${ENVIRONMENT}-${UNIQUE_STRING}"
    
    if az monitor app-insights component show --app "$APP_INSIGHTS_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
        test_passed "Application Insights '$APP_INSIGHTS_NAME' is accessible"
    else
        test_failed "Application Insights '$APP_INSIGHTS_NAME' is not accessible"
    fi
else
    test_warning "Monitoring resources not tested (dev environment)"
fi

# Test 8: Network connectivity test
print_status "Test 8: Testing network connectivity..."
if az network nsg list --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    NSG_COUNT=$(az network nsg list --resource-group "$RESOURCE_GROUP" --query "length(@)" --output tsv)
    if [ "$NSG_COUNT" -gt 0 ]; then
        test_passed "Network Security Groups configured ($NSG_COUNT found)"
    else
        test_warning "No Network Security Groups found (may be using default rules)"
    fi
else
    test_warning "Unable to check Network Security Groups"
fi

# Summary
echo ""
print_status "📊 Test Summary:"
echo "  Total Tests: $TOTAL_TESTS"
echo "  Passed: $PASSED_TESTS"
echo "  Failed: $FAILED_TESTS"
echo "  Warnings: $((TOTAL_TESTS - PASSED_TESTS - FAILED_TESTS))"

if [ $FAILED_TESTS -eq 0 ]; then
    print_success "🎉 All critical tests passed! Infrastructure is ready for use."
    
    print_status "Next steps:"
    echo "1. Deploy your application code to the Function App"
    echo "2. Update Key Vault secrets with production values"
    echo "3. Configure monitoring alerts and dashboards"
    echo "4. Run integration tests with your application"
    
    exit 0
else
    print_error "❌ $FAILED_TESTS test(s) failed. Please review and fix issues before proceeding."
    exit 1
fi