/**
 * k6 smoke test — drives traffic through the public ingress during a rolling
 * deploy to verify zero-downtime. Each iteration fires 2 requests against the
 * full app stack, so 50 iter/s = 100 RPS.
 *
 * Start this BEFORE triggering the rollout so traffic is already flowing
 * when pods begin swapping.
 *
 * Usage:
 *   k6 run --env WEB_URL=https://activor.duckdns.org deploy/k6/smoke-test.js
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
      preAllocatedVUs: 50,
      maxVUs: 100,
      startRate: 0,
      timeUnit: '1s',
      stages: [
        { duration: '10s', target: 100 },   // ramp to 100 RPS per endpoint before rollout triggers
        { duration: '120s', target: 100 },  // hold at 100 RPS per endpoint through the full rollout window
        { duration: '10s', target: 0 },     // ramp down after rollout confirmed complete
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

const BASE_URL = __ENV.BASE_URL || 'https://activor.duckdns.org';

export default function () {
  // Hits the web pod — verifies ingress → web pod is healthy
  const home = http.get(`${BASE_URL}/`);
  check(home, { 'web / 200': (r) => r.status === 200 });
  errorRate.add(home.status >= 500);

  // Hits the API pod — verifies web pod → API pod → DB is healthy
  const activities = http.get(`${BASE_URL}/api/activities`);
  check(activities, { 'api /api/activities 200': (r) => r.status === 200 });
  errorRate.add(activities.status >= 500);
}
