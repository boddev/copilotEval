#!/bin/bash

# Comprehensive Test Runner for CopilotEval
# This script runs all test suites in the correct order

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

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
    print_success "$1"
}

test_failed() {
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    FAILED_TESTS=$((FAILED_TESTS + 1))
    print_error "$1"
}

# Parse command line arguments
RUN_BACKEND_TESTS=true
RUN_FRONTEND_TESTS=true
RUN_E2E_TESTS=true
RUN_LOAD_TESTS=false
VERBOSE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --backend-only)
            RUN_FRONTEND_TESTS=false
            RUN_E2E_TESTS=false
            shift
            ;;
        --frontend-only)
            RUN_BACKEND_TESTS=false
            RUN_E2E_TESTS=false
            shift
            ;;
        --e2e-only)
            RUN_BACKEND_TESTS=false
            RUN_FRONTEND_TESTS=false
            shift
            ;;
        --include-load-tests)
            RUN_LOAD_TESTS=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  --backend-only       Run only backend tests"
            echo "  --frontend-only      Run only frontend tests"
            echo "  --e2e-only          Run only E2E tests"
            echo "  --include-load-tests Include load tests (requires k6)"
            echo "  --verbose           Enable verbose output"
            echo "  --help              Show this help message"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

print_status "🚀 Starting CopilotEval Test Suite"
echo ""

# Function to check if a command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Check prerequisites
print_status "Checking prerequisites..."

if ! command_exists dotnet; then
    test_failed "❌ .NET SDK not found. Please install .NET 8.0 SDK"
    exit 1
else
    test_passed "✅ .NET SDK found"
fi

if ! command_exists npm; then
    test_failed "❌ Node.js/npm not found. Please install Node.js 18+"
    exit 1
else
    test_passed "✅ Node.js/npm found"
fi

if [ "$RUN_LOAD_TESTS" = true ] && ! command_exists k6; then
    print_warning "⚠️ k6 not found. Load tests will be skipped"
    RUN_LOAD_TESTS=false
fi

echo ""

# Backend Tests
if [ "$RUN_BACKEND_TESTS" = true ]; then
    print_status "🧪 Running Backend Tests..."
    
    # Build backend
    print_status "Building backend..."
    if dotnet build backend/CopilotEvalApi.csproj; then
        test_passed "✅ Backend build successful"
    else
        test_failed "❌ Backend build failed"
        exit 1
    fi
    
    # Run backend unit tests
    print_status "Running backend unit tests..."
    if dotnet test tests/backend/CopilotEvalApi.Tests/CopilotEvalApi.Tests.csproj --no-build; then
        test_passed "✅ Backend unit tests passed"
    else
        test_failed "❌ Backend unit tests failed"
    fi
    
    echo ""
fi

# Frontend Tests
if [ "$RUN_FRONTEND_TESTS" = true ]; then
    print_status "🧪 Running Frontend Tests..."
    
    # Install frontend dependencies
    print_status "Installing frontend dependencies..."
    if (cd frontend && npm ci); then
        test_passed "✅ Frontend dependencies installed"
    else
        test_failed "❌ Frontend dependency installation failed"
        exit 1
    fi
    
    # Lint frontend
    print_status "Linting frontend code..."
    if (cd frontend && npm run lint); then
        test_passed "✅ Frontend linting passed"
    else
        test_failed "❌ Frontend linting failed"
    fi
    
    # Build frontend
    print_status "Building frontend..."
    if (cd frontend && npm run build); then
        test_passed "✅ Frontend build successful"
    else
        test_failed "❌ Frontend build failed"
        exit 1
    fi
    
    # Run frontend unit tests
    print_status "Running frontend unit tests..."
    if (cd frontend && npm test -- --run); then
        test_passed "✅ Frontend unit tests passed"
    else
        test_failed "❌ Frontend unit tests failed"
    fi
    
    echo ""
fi

# Integration Tests
if [ "$RUN_BACKEND_TESTS" = true ]; then
    print_status "🔗 Running Integration Tests..."
    
    # Start backend server for integration tests
    print_status "Starting backend server for integration tests..."
    (cd backend && dotnet run --urls "http://localhost:5000" > /dev/null 2>&1 &)
    BACKEND_PID=$!
    sleep 10
    
    # Run API integration tests
    print_status "Running API integration tests..."
    if (cd backend/scripts && chmod +x jobs-api-integration-tests.sh && ./jobs-api-integration-tests.sh); then
        test_passed "✅ API integration tests passed"
    else
        test_failed "❌ API integration tests failed"
    fi
    
    # Run contract tests
    print_status "Running contract tests..."
    if (cd backend/scripts && chmod +x contract-tests.sh && ./contract-tests.sh); then
        test_passed "✅ Contract tests passed"
    else
        test_failed "❌ Contract tests failed"
    fi
    
    # Clean up backend server
    kill $BACKEND_PID 2>/dev/null || true
    sleep 2
    
    echo ""
fi

# E2E Tests
if [ "$RUN_E2E_TESTS" = true ]; then
    print_status "🌐 Running End-to-End Tests..."
    
    # Install E2E test dependencies
    print_status "Installing E2E test dependencies..."
    if (cd tests/e2e && npm ci); then
        test_passed "✅ E2E dependencies installed"
    else
        test_failed "❌ E2E dependency installation failed"
    fi
    
    # Install Playwright browsers
    print_status "Installing Playwright browsers..."
    if (cd tests/e2e && npx playwright install); then
        test_passed "✅ Playwright browsers installed"
    else
        test_failed "❌ Playwright browser installation failed"
    fi
    
    # Run Playwright tests
    print_status "Running Playwright E2E tests..."
    if (cd tests/e2e && npx playwright test); then
        test_passed "✅ E2E tests passed"
    else
        test_failed "❌ E2E tests failed"
    fi
    
    echo ""
fi

# Load Tests
if [ "$RUN_LOAD_TESTS" = true ]; then
    print_status "📈 Running Load Tests..."
    
    # Start backend server for load tests
    print_status "Starting backend server for load tests..."
    (cd backend && dotnet run --urls "http://localhost:5000" > /dev/null 2>&1 &)
    BACKEND_PID=$!
    sleep 15
    
    # Run basic load test
    print_status "Running basic load test..."
    if (cd load-tests/k6 && k6 run job-api-load-test.js); then
        test_passed "✅ Basic load test passed"
    else
        test_failed "❌ Basic load test failed"
    fi
    
    # Clean up backend server
    kill $BACKEND_PID 2>/dev/null || true
    sleep 2
    
    echo ""
fi

# Test Summary
echo ""
print_status "📊 Test Summary"
echo "=================="
echo "Total Tests: $TOTAL_TESTS"
echo "Passed: $PASSED_TESTS"
echo "Failed: $FAILED_TESTS"
echo ""

if [ $FAILED_TESTS -eq 0 ]; then
    print_success "🎉 All tests passed successfully!"
    echo ""
    print_status "Next steps:"
    echo "1. Commit your changes"
    echo "2. Push to trigger CI pipeline"
    echo "3. Monitor deployment"
    
    if [ "$RUN_LOAD_TESTS" = false ]; then
        echo "4. Run load tests with: $0 --include-load-tests"
    fi
    
    exit 0
else
    print_error "❌ $FAILED_TESTS test(s) failed. Please review and fix issues before proceeding."
    echo ""
    print_status "Tips for debugging:"
    echo "- Run individual test suites with --backend-only, --frontend-only, or --e2e-only"
    echo "- Use --verbose for more detailed output"
    echo "- Check logs for specific error messages"
    echo "- Ensure all dependencies are installed and up to date"
    
    exit 1
fi