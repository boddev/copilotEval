#!/bin/bash

# Contract Test Script for Copilot Evaluation API
# This script validates sample payloads against their JSON schemas

set -e

echo "🚀 Starting API Contract Tests..."

# Change to backend directory
cd "$(dirname "$0")/.."

SCHEMA_DIR="openapi"
SAMPLES_DIR="docs/sample-payloads"
FAILED_TESTS=0
TOTAL_TESTS=0

echo "📋 Schema Directory: $SCHEMA_DIR"
echo "📋 Samples Directory: $SAMPLES_DIR"

# Function to validate a JSON file against a schema
validate_json() {
    local schema_file=$1
    local json_file=$2
    local test_name=$3
    
    echo "🔍 Testing: $test_name"
    echo "   Schema: $schema_file"
    echo "   JSON: $json_file"
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    if ajv -s "$schema_file" -d "$json_file" --strict=false --verbose 2>/dev/null; then
        echo "   ✅ PASS: $test_name"
    else
        echo "   ❌ FAIL: $test_name"
        echo "   Running detailed validation:"
        ajv -s "$schema_file" -d "$json_file" --strict=false --verbose || true
        FAILED_TESTS=$((FAILED_TESTS + 1))
    fi
    echo ""
}

# Test JobMessage samples against the JobMessage schema
echo "🧪 Testing JobMessage samples..."

if [ -f "$SCHEMA_DIR/job-message-schema.json" ]; then
    # Test job message payloads
    validate_json "$SCHEMA_DIR/job-message-schema.json" "$SAMPLES_DIR/job-message-created.json" "JobMessage Created"
    validate_json "$SCHEMA_DIR/job-message-schema.json" "$SAMPLES_DIR/job-message-progress.json" "JobMessage Progress"
    validate_json "$SCHEMA_DIR/job-message-schema.json" "$SAMPLES_DIR/job-message-completed-large-payload.json" "JobMessage Completed (Large Payload)"
    validate_json "$SCHEMA_DIR/job-message-schema.json" "$SAMPLES_DIR/job-message-failed.json" "JobMessage Failed"
else
    echo "❌ JobMessage schema not found: $SCHEMA_DIR/job-message-schema.json"
    FAILED_TESTS=$((FAILED_TESTS + 1))
fi

# Validate OpenAPI spec itself
echo "🔧 Validating OpenAPI specification..."
TOTAL_TESTS=$((TOTAL_TESTS + 1))

if swagger-cli validate "$SCHEMA_DIR/openapi.yaml" 2>/dev/null; then
    echo "✅ PASS: OpenAPI Specification"
else
    echo "❌ FAIL: OpenAPI Specification"
    swagger-cli validate "$SCHEMA_DIR/openapi.yaml" || true
    FAILED_TESTS=$((FAILED_TESTS + 1))
fi

# Test OpenAPI with redocly for additional validation
echo ""
echo "🔧 Linting OpenAPI specification with Redocly..."
TOTAL_TESTS=$((TOTAL_TESTS + 1))

if redocly lint "$SCHEMA_DIR/openapi.yaml" --silent 2>/dev/null; then
    echo "✅ PASS: OpenAPI Redocly Lint"
else
    echo "⚠️  WARN: OpenAPI Redocly Lint (warnings allowed)"
    # Don't fail on warnings, just show them
    redocly lint "$SCHEMA_DIR/openapi.yaml" || true
fi

# Summary
echo "📊 Test Results Summary:"
echo "   Total Tests: $TOTAL_TESTS"
echo "   Passed: $((TOTAL_TESTS - FAILED_TESTS))"
echo "   Failed: $FAILED_TESTS"

if [ $FAILED_TESTS -eq 0 ]; then
    echo "🎉 All contract tests passed!"
    exit 0
else
    echo "💥 $FAILED_TESTS test(s) failed!"
    exit 1
fi