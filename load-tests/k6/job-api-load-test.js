import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');

export const options = {
  stages: [
    { duration: '2m', target: 10 }, // Ramp up to 10 users
    { duration: '5m', target: 10 }, // Stay at 10 users
    { duration: '2m', target: 20 }, // Ramp up to 20 users
    { duration: '5m', target: 20 }, // Stay at 20 users
    { duration: '2m', target: 0 },  // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'], // 95% of requests should complete within 2s
    http_req_failed: ['rate<0.1'],     // Error rate should be less than 10%
    errors: ['rate<0.1'],              // Custom error rate should be less than 10%
  },
};

const BASE_URL = 'http://localhost:5000';

export default function () {
  // Test 1: Get jobs list
  const jobsResponse = http.get(`${BASE_URL}/api/jobs`);
  
  const jobsCheck = check(jobsResponse, {
    'jobs list status is 200': (r) => r.status === 200,
    'jobs list response time < 1000ms': (r) => r.timings.duration < 1000,
  });
  
  errorRate.add(!jobsCheck);

  sleep(1);

  // Test 2: Submit a job
  const jobPayload = {
    name: `Load Test Job ${Math.random().toString(36).substr(2, 9)}`,
    description: 'Load testing job submission',
    type: 'bulk_evaluation',
    configuration: {
      data_source: 'load-test-data.csv',
      prompt_template: 'Load test prompt: {context}',
      evaluation_criteria: {
        similarity_threshold: 0.8,
        use_semantics_scoring: true
      },
      agent_configuration: {
        additional_instructions: 'Load test instructions',
        knowledge_source: 'load_test'
      }
    }
  };

  const submitResponse = http.post(
    `${BASE_URL}/api/jobs`,
    JSON.stringify(jobPayload),
    {
      headers: {
        'Content-Type': 'application/json',
      },
    }
  );

  const submitCheck = check(submitResponse, {
    'job submission status is 202': (r) => r.status === 202,
    'job submission response time < 2000ms': (r) => r.timings.duration < 2000,
    'job submission returns job_id': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.job_id && body.job_id.startsWith('job_');
      } catch {
        return false;
      }
    },
  });

  errorRate.add(!submitCheck);

  // Test 3: Get specific job (if submission was successful)
  if (submitResponse.status === 202) {
    try {
      const submitBody = JSON.parse(submitResponse.body);
      const jobId = submitBody.job_id;

      sleep(1);

      const jobResponse = http.get(`${BASE_URL}/api/jobs/${jobId}`);
      
      const jobCheck = check(jobResponse, {
        'job retrieval status is 200': (r) => r.status === 200,
        'job retrieval response time < 1000ms': (r) => r.timings.duration < 1000,
        'job retrieval returns correct job': (r) => {
          try {
            const body = JSON.parse(r.body);
            return body.id === jobId;
          } catch {
            return false;
          }
        },
      });

      errorRate.add(!jobCheck);
    } catch (e) {
      console.error('Failed to parse job submission response:', e);
      errorRate.add(true);
    }
  }

  sleep(2);
}