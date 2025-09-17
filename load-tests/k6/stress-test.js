import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');

export const options = {
  stages: [
    { duration: '1m', target: 50 },  // Ramp up to 50 users
    { duration: '3m', target: 100 }, // Ramp up to 100 users
    { duration: '3m', target: 100 }, // Stay at 100 users
    { duration: '1m', target: 0 },   // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<3000'], // 95% of requests should complete within 3s
    http_req_failed: ['rate<0.15'],    // Error rate should be less than 15%
    errors: ['rate<0.15'],             // Custom error rate should be less than 15%
  },
};

const BASE_URL = 'http://localhost:5000';

export default function () {
  // Stress test: Multiple rapid job submissions
  const jobCount = Math.floor(Math.random() * 3) + 1; // 1-3 jobs per user
  
  for (let i = 0; i < jobCount; i++) {
    const jobPayload = {
      name: `Stress Test Job ${__VU}-${__ITER}-${i}`,
      description: `Stress testing job submission - VU: ${__VU}, Iteration: ${__ITER}, Job: ${i}`,
      type: 'bulk_evaluation',
      configuration: {
        data_source: `stress-test-data-${i}.csv`,
        prompt_template: `Stress test prompt ${i}: {context}`,
        evaluation_criteria: {
          similarity_threshold: 0.7 + (i * 0.1),
          use_semantics_scoring: true
        },
        agent_configuration: {
          additional_instructions: `Stress test instructions for job ${i}`,
          knowledge_source: `stress_test_${i}`
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
        timeout: '30s', // Longer timeout for stress conditions
      }
    );

    const success = check(response, {
      'stress test job submission status is 202': (r) => r.status === 202,
      'stress test response time < 5000ms': (r) => r.timings.duration < 5000,
      'stress test response has job_id': (r) => {
        if (r.status !== 202) return false;
        try {
          const body = JSON.parse(r.body);
          return body.job_id && body.job_id.startsWith('job_');
        } catch {
          return false;
        }
      },
    });

    errorRate.add(!success);

    // Brief pause between job submissions from same user
    sleep(0.5);
  }

  // Test jobs list under load
  const listResponse = http.get(`${BASE_URL}/api/jobs?limit=10`);
  
  const listCheck = check(listResponse, {
    'jobs list under stress returns 200': (r) => r.status === 200,
    'jobs list under stress response time < 3000ms': (r) => r.timings.duration < 3000,
  });

  errorRate.add(!listCheck);

  // Random sleep to simulate real user behavior
  sleep(Math.random() * 3 + 1);
}