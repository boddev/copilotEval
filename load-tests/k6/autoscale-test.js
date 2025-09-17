import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');

export const options = {
  stages: [
    { duration: '5m', target: 200 },  // Ramp up to 200 users quickly
    { duration: '10m', target: 200 }, // Stay at 200 users for extended period
    { duration: '5m', target: 300 },  // Spike to 300 users
    { duration: '2m', target: 300 },  // Hold the spike
    { duration: '5m', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<5000'], // 95% of requests should complete within 5s
    http_req_failed: ['rate<0.25'],    // Error rate should be less than 25% (allowing for some failures under extreme load)
    errors: ['rate<0.25'],
  },
};

const BASE_URL = 'http://localhost:5000';

export default function () {
  // Autoscale trigger test: High volume of concurrent requests
  
  // Simulate different user behaviors
  const userBehavior = Math.random();
  
  if (userBehavior < 0.6) {
    // 60% of users: Job submission
    const jobPayload = {
      name: `Autoscale Test Job ${__VU}-${__ITER}`,
      description: `Autoscale testing - VU: ${__VU}, Iteration: ${__ITER}`,
      type: 'bulk_evaluation',
      configuration: {
        data_source: `autoscale-test-data-${__VU}.csv`,
        prompt_template: `Autoscale test prompt for user ${__VU}: {context}`,
        evaluation_criteria: {
          similarity_threshold: 0.8,
          use_semantics_scoring: true
        },
        agent_configuration: {
          additional_instructions: `Autoscale test instructions for VU ${__VU}`,
          knowledge_source: `autoscale_test`
        }
      }
    };

    const response = http.post(
      `${BASE_URL}/api/jobs`,
      JSON.stringify(jobPayload),
      {
        headers: {
          'Content-Type': 'application/json',
        },
        timeout: '60s', // Extended timeout for autoscale conditions
      }
    );

    const success = check(response, {
      'autoscale job submission handled': (r) => r.status === 202 || r.status === 503, // Allow 503 Service Unavailable during scaling
      'autoscale job response time reasonable': (r) => r.timings.duration < 10000,
    });

    errorRate.add(!success);

  } else if (userBehavior < 0.9) {
    // 30% of users: Browse jobs
    const page = Math.floor(Math.random() * 5) + 1;
    const listResponse = http.get(`${BASE_URL}/api/jobs?page=${page}&limit=20`);
    
    const listCheck = check(listResponse, {
      'autoscale jobs list handled': (r) => r.status === 200 || r.status === 503,
      'autoscale list response time reasonable': (r) => r.timings.duration < 8000,
    });

    errorRate.add(!listCheck);

  } else {
    // 10% of users: Check specific job status
    const jobId = `job_test_${Math.floor(Math.random() * 100)}`;
    const jobResponse = http.get(`${BASE_URL}/api/jobs/${jobId}`);
    
    const jobCheck = check(jobResponse, {
      'autoscale job check handled': (r) => r.status === 200 || r.status === 404 || r.status === 503,
      'autoscale job check response time reasonable': (r) => r.timings.duration < 6000,
    });

    errorRate.add(!jobCheck);
  }

  // Variable sleep to create realistic load patterns
  const sleepTime = Math.random() * 2 + 0.5; // 0.5 to 2.5 seconds
  sleep(sleepTime);
}