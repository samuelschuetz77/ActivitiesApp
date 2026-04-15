/**
 * k6 smoke test — 100 RPS throughout a rolling deploy to verify zero-downtime.
 *
 * Uses ramping-arrival-rate executor: k6 drives the target iteration rate
 * regardless of response time (unlike VU-based tests where slow responses
 * reduce throughput). Each iteration fires 5 requests, so:
 *   20 iterations/s × 5 requests = 100 RPS
 *
 * Start this BEFORE triggering the rollout so traffic is already flowing
 * when pods begin swapping.
 *
 * Usage:
 *   k6 run \
 *     --env WEB_URL=https://activor.duckdns.org \
 *     --env API_URL=http://localhost:18080 \
 *     deploy/k6/smoke-test.js
 */
import http from 'k6/http';
import { check } from 'k6';
import { Rate } from 'k6/metrics';

const errorRate = new Rate('errors');

export const options = {
  scenarios: {
    zdd_smoke: {
      executor: 'ramping-arrival-rate',
      // Pre-allocate enough VUs to sustain 20 iter/s at ~1s/iteration.
      // Add headroom for latency spikes during the rollout.
      preAllocatedVUs: 30,
      maxVUs: 60,
      startRate: 0,
      timeUnit: '1s',
      stages: [
        { duration: '10s', target: 20 },   // ramp to 100 RPS before rollout triggers
        { duration: '120s', target: 20 },  // hold at 100 RPS through the full rollout window
        { duration: '10s', target: 0 },    // ramp down after rollout confirmed complete
      ],
    },
  },
  thresholds: {
    // Zero tolerance for server errors during the rollout window
    errors: ['rate<0.01'],
    http_req_failed: ['rate<0.01'],
    // p95 latency must stay under 2s — a spike here indicates pod drain issues
    http_req_duration: ['p(95)<2000'],
  },
};

const WEB_URL = __ENV.WEB_URL || 'https://activor.duckdns.org';
const API_URL = __ENV.API_URL || 'http://localhost:18080';

export default function () {
  // 5 requests per iteration × 20 iterations/s = 100 RPS

  // --- Web frontend (through ingress → Service → pod) ---
  const webHealth = http.get(`${WEB_URL}/health`);
  check(webHealth, { 'web /health 200': (r) => r.status === 200 });
  errorRate.add(webHealth.status >= 500);

  const webAlive = http.get(`${WEB_URL}/alive`);
  check(webAlive, { 'web /alive 200': (r) => r.status === 200 });
  errorRate.add(webAlive.status >= 500);

  // --- API (via port-forward from self-hosted runner) ---
  const apiHealth = http.get(`${API_URL}/health`);
  check(apiHealth, { 'api /health 200': (r) => r.status === 200 });
  errorRate.add(apiHealth.status >= 500);

  const apiAlive = http.get(`${API_URL}/alive`);
  check(apiAlive, { 'api /alive 200': (r) => r.status === 200 });
  errorRate.add(apiAlive.status >= 500);

  const apiVersion = http.get(`${API_URL}/api/version`);
  check(apiVersion, { 'api /version 200': (r) => r.status === 200 });
  errorRate.add(apiVersion.status >= 500);
  // No sleep — arrival rate executor manages timing, not the VU loop
}
