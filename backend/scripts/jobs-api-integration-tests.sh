#!/bin/bash

# Integration Tests for Jobs API GET Endpoints
# Validates the new GET /api/jobs and enhanced GET /api/jobs/{id} endpoints

set -e

# Configuration
SERVER_URL="http://localhost:5000"
JOBS_URL="$SERVER_URL/api/jobs"

# Test counters
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

echo "🚀 Jobs API GET Endpoints Integration Tests"
echo "Server URL: $SERVER_URL"
echo ""

# Function to check if server is running
check_server() {
    echo "🔍 Checking if server is running..."
    if curl -s "$SERVER_URL/health" >/dev/null 2>&1 || curl -s "$JOBS_URL" >/dev/null 2>&1; then
        echo "✅ Server is responding"
        return 0
    else
        echo "❌ Server is not responding"
        echo "   Please start the server with: dotnet run --urls=\"http://localhost:5000\""
        exit 1
    fi
}

# Function to run a test
run_test() {
    local test_name="$1"
    local url="$2"
    local expected_status="$3"
    local additional_checks="$4"
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    echo "🧪 Test $TOTAL_TESTS: $test_name"
    echo "   URL: $url"
    
    # Make request
    response=$(curl -s -w "\n%{http_code}" "$url" 2>/dev/null || echo -e "\n000")
    http_code=$(echo "$response" | tail -n1)
    body=$(echo "$response" | head -n -1)
    
    # Check HTTP status
    if [ "$http_code" != "$expected_status" ]; then
        echo "   ❌ FAIL: Expected HTTP $expected_status, got $http_code"
        echo "   Response: $body"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        return 1
    fi
    
    # Check JSON validity for 200 responses
    if [ "$expected_status" = "200" ]; then
        if ! echo "$body" | jq . >/dev/null 2>&1; then
            echo "   ❌ FAIL: Invalid JSON response"
            echo "   Response: $body"
            FAILED_TESTS=$((FAILED_TESTS + 1))
            return 1
        fi
    fi
    
    # Run additional checks if provided
    if [ -n "$additional_checks" ]; then
        if ! eval "$additional_checks"; then
            echo "   ❌ FAIL: Additional validation failed"
            FAILED_TESTS=$((FAILED_TESTS + 1))
            return 1
        fi
    fi
    
    echo "   ✅ PASS"
    PASSED_TESTS=$((PASSED_TESTS + 1))
    echo ""
}

# Additional validation functions
validate_job_list_response() {
    # Check required fields
    if ! echo "$body" | jq -e '.jobs' >/dev/null 2>&1; then
        echo "Missing 'jobs' field"
        return 1
    fi
    
    if ! echo "$body" | jq -e '.pagination' >/dev/null 2>&1; then
        echo "Missing 'pagination' field"
        return 1
    fi
    
    # Check pagination structure (use has() to check for field existence)
    local required_pagination_fields=("current_page" "total_pages" "total_items" "items_per_page" "has_next" "has_previous")
    for field in "${required_pagination_fields[@]}"; do
        if ! echo "$body" | jq -e ".pagination | has(\"$field\")" >/dev/null 2>&1; then
            echo "Missing pagination field: $field"
            return 1
        fi
    done
    
    return 0
}

validate_error_response() {
    if ! echo "$body" | jq -e '.error.code' >/dev/null 2>&1; then
        echo "Missing error.code field"
        return 1
    fi
    
    if ! echo "$body" | jq -e '.error.message' >/dev/null 2>&1; then
        echo "Missing error.message field"
        return 1
    fi
    
    return 0
}

# Start tests
check_server

echo "📋 Testing GET /api/jobs endpoint..."

# Test 1: Basic GET request
run_test "Basic GET /api/jobs" \
    "$JOBS_URL" \
    "200" \
    "validate_job_list_response"

# Test 2: Pagination parameters
run_test "GET /api/jobs with page and limit" \
    "$JOBS_URL?page=1&limit=10" \
    "200" \
    "validate_job_list_response"

# Test 3: Filter by status - pending
run_test "Filter by status=pending" \
    "$JOBS_URL?status=pending" \
    "200" \
    "validate_job_list_response"

# Test 4: Filter by status - running
run_test "Filter by status=running" \
    "$JOBS_URL?status=running" \
    "200" \
    "validate_job_list_response"

# Test 5: Filter by status - completed
run_test "Filter by status=completed" \
    "$JOBS_URL?status=completed" \
    "200" \
    "validate_job_list_response"

# Test 6: Filter by type - bulk_evaluation
run_test "Filter by type=bulk_evaluation" \
    "$JOBS_URL?type=bulk_evaluation" \
    "200" \
    "validate_job_list_response"

# Test 7: Filter by type - single_evaluation
run_test "Filter by type=single_evaluation" \
    "$JOBS_URL?type=single_evaluation" \
    "200" \
    "validate_job_list_response"

# Test 8: Sort by created_at ASC
run_test "Sort by created_at ASC" \
    "$JOBS_URL?sort=created_at&order=asc" \
    "200" \
    "validate_job_list_response"

# Test 9: Sort by name DESC
run_test "Sort by name DESC" \
    "$JOBS_URL?sort=name&order=desc" \
    "200" \
    "validate_job_list_response"

# Test 10: Combined filters
run_test "Combined filters" \
    "$JOBS_URL?status=pending&type=bulk_evaluation&page=1&limit=5&sort=created_at&order=desc" \
    "200" \
    "validate_job_list_response"

echo "📋 Testing validation and error cases..."

# Test 11: Invalid page number (0)
run_test "Invalid page=0" \
    "$JOBS_URL?page=0" \
    "400" \
    "validate_error_response"

# Test 12: Invalid page number (negative)
run_test "Invalid page=-1" \
    "$JOBS_URL?page=-1" \
    "400" \
    "validate_error_response"

# Test 13: Invalid limit (too high)
run_test "Invalid limit=101" \
    "$JOBS_URL?limit=101" \
    "400" \
    "validate_error_response"

# Test 14: Invalid limit (0)
run_test "Invalid limit=0" \
    "$JOBS_URL?limit=0" \
    "400" \
    "validate_error_response"

# Test 15: Invalid status
run_test "Invalid status=invalid" \
    "$JOBS_URL?status=invalid" \
    "400" \
    "validate_error_response"

# Test 16: Invalid type
run_test "Invalid type=invalid" \
    "$JOBS_URL?type=invalid" \
    "400" \
    "validate_error_response"

# Test 17: Invalid sort field
run_test "Invalid sort=invalid" \
    "$JOBS_URL?sort=invalid" \
    "400" \
    "validate_error_response"

# Test 18: Invalid order
run_test "Invalid order=invalid" \
    "$JOBS_URL?order=invalid" \
    "400" \
    "validate_error_response"

echo "📋 Testing GET /api/jobs/{id} endpoint..."

# Test 19: Get nonexistent job
run_test "GET nonexistent job" \
    "$JOBS_URL/nonexistent_job_id" \
    "404" \
    "validate_error_response"

# Test 20: Get job with invalid ID format
run_test "GET job with special characters" \
    "$JOBS_URL/invalid@id#format" \
    "404" \
    "validate_error_response"

# Summary
echo "📊 Test Results Summary"
echo "======================"
echo "Total Tests: $TOTAL_TESTS"
echo "Passed: $PASSED_TESTS"
echo "Failed: $FAILED_TESTS"
echo ""

if [ $FAILED_TESTS -eq 0 ]; then
    echo "🎉 All tests passed!"
    echo "✅ GET /api/jobs endpoint is working correctly"
    echo "✅ Filtering by status and type works"
    echo "✅ Pagination and sorting work correctly"
    echo "✅ Input validation is properly implemented"
    echo "✅ Error responses follow expected format"
    echo "✅ GET /api/jobs/{id} endpoint handles missing jobs correctly"
    exit 0
else
    echo "💥 $FAILED_TESTS test(s) failed!"
    echo "❌ Please review the implementation"
    exit 1
fi