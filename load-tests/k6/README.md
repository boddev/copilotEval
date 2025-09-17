# Load Testing Scripts for CopilotEval

This directory contains k6 load testing scripts to validate the performance and autoscaling capabilities of the CopilotEval API.

## Prerequisites

Install k6:
```bash
# macOS
brew install k6

# Ubuntu/Debian
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update
sudo apt-get install k6

# Windows
choco install k6
```

## Test Scripts

### 1. job-api-load-test.js
Basic load test for the job submission and retrieval APIs.
- **Target**: 10-20 concurrent users
- **Duration**: 16 minutes
- **Purpose**: Validate normal operation under moderate load

```bash
k6 run job-api-load-test.js
```

### 2. stress-test.js
Stress test to find the breaking point of the API.
- **Target**: Up to 100 concurrent users
- **Duration**: 8 minutes
- **Purpose**: Identify performance bottlenecks and failure modes

```bash
k6 run stress-test.js
```

### 3. autoscale-test.js
Autoscaling validation test with spike patterns.
- **Target**: Up to 300 concurrent users with spikes
- **Duration**: 27 minutes
- **Purpose**: Validate autoscaling triggers and thresholds

```bash
k6 run autoscale-test.js
```

## Expected Results

### Performance Thresholds
- **Response Time**: 95th percentile < 2-5 seconds (depending on test)
- **Error Rate**: < 10-25% (depending on test intensity)
- **Throughput**: Should handle sustained load without degradation

### Autoscaling Validation
- **Scale Up**: System should add instances when load increases
- **Scale Down**: System should remove instances when load decreases
- **Response Time**: Should remain reasonable even during scaling events
- **Availability**: Service should remain available (allow some 503s during scaling)

## Running Tests Against Different Environments

### Local Development
```bash
# Ensure backend is running on localhost:5000
k6 run job-api-load-test.js
```

### Staging Environment
```bash
# Update BASE_URL in the test files or use environment variable
BASE_URL=https://staging.copiloteval.com k6 run job-api-load-test.js
```

### Production Environment
```bash
# Use with caution and approval
BASE_URL=https://prod.copiloteval.com k6 run job-api-load-test.js
```

## Monitoring During Tests

Monitor these metrics during load tests:
- **CPU Usage** of backend instances
- **Memory Usage** of backend instances
- **Database Performance** (query time, connections)
- **Service Bus** (queue depth, processing rate)
- **HTTP Response Times** and error rates
- **Autoscaling Events** (instance creation/termination)

## Interpreting Results

### Successful Test Indicators
- All thresholds pass (green)
- Error rate stays within acceptable limits
- Response times remain consistent
- Autoscaling triggers appropriately

### Warning Signs
- High error rates (> threshold)
- Degrading response times over time
- Failed autoscaling (no new instances when needed)
- Database timeouts or connection pool exhaustion

## Customization

Modify the test parameters in each script:
- **stages**: Adjust user ramp-up patterns
- **thresholds**: Modify acceptable performance criteria
- **payload**: Change job submission data
- **sleep**: Adjust user behavior timing