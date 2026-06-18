/**
 * k6 Load Test — Distributed Lock POC
 *
 * Scenarios:
 *  1. locked_ramp   — gradual ramp-up on the safe (locked) endpoint
 *  2. locked_spike  — sudden traffic spike to stress the lock queue
 *  3. race_demo     — parallel hits on the unsafe endpoint to demonstrate lost updates
 *
 * Run:
 *   k6 run k6/load-test.js
 *   k6 run --env BASE_URL=http://localhost:5000 k6/load-test.js
 *
 * Dashboard (live):
 *   k6 run --out dashboard k6/load-test.js
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend, Rate } from 'k6/metrics';

// ── Config ────────────────────────────────────────────────────────────────────
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

// Unique counter names per scenario so results don't mix
const COUNTER_LOCKED = `k6-locked-${Date.now()}`;
const COUNTER_UNSAFE = `k6-unsafe-${Date.now()}`;

// ── Custom metrics ─────────────────────────────────────────────────────────────
const lockedDuration  = new Trend('locked_increment_duration',  true);
const unsafeDuration  = new Trend('unsafe_increment_duration',  true);
const successRate     = new Rate('increment_success_rate');
const lockContention  = new Counter('lock_contentions');   // 5xx counted as contention

// ── Scenarios ─────────────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    /**
     * Gradual ramp — validates throughput under increasing concurrency.
     * Target: all requests succeed, final counter == total VU*iterations.
     */
    locked_ramp: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '15s', target: 10  },   // warm-up
        { duration: '30s', target: 50  },   // ramp to medium load
        { duration: '30s', target: 100 },   // peak load
        { duration: '15s', target: 0   },   // cool-down
      ],
      exec: 'incrementLocked',
      gracefulRampDown: '5s',
    },

    /**
     * Spike — 200 VUs fire simultaneously to stress-test the lock queue.
     * Validates that no requests fail; they simply queue behind the lock holder.
     */
    locked_spike: {
      executor: 'constant-vus',
      vus: 200,
      duration: '20s',
      exec: 'incrementLocked',
      startTime: '95s',   // starts after ramp scenario finishes
    },

    /**
     * Race demo — same load on the UNSAFE endpoint.
     * After the test, compare GET /counters/{name} value vs total requests.
     * Lost updates = total_requests − actual_value.
     */
    race_demo: {
      executor: 'constant-vus',
      vus: 50,
      duration: '30s',
      exec: 'incrementUnsafe',
      startTime: '120s',
    },
  },

  thresholds: {
    // 95% of locked increments must complete in under 2 s (includes lock wait)
    'locked_increment_duration': ['p(95)<2000'],
    // Unsafe endpoint is faster but we just track it
    'unsafe_increment_duration': ['p(95)<500'],
    // Zero failures expected on the locked path
    'increment_success_rate': ['rate>0.99'],
    // HTTP errors < 1%
    'http_req_failed': ['rate<0.01'],
  },
};

// ── Scenario functions ─────────────────────────────────────────────────────────

export function incrementLocked() {
  const res = http.post(`${BASE_URL}/counters/${COUNTER_LOCKED}/increment`, null, {
    tags: { scenario: 'locked' },
  });

  lockedDuration.add(res.timings.duration);

  const ok = check(res, {
    'locked: status 200': (r) => r.status === 200,
    'locked: has value':  (r) => JSON.parse(r.body).value > 0,
  });

  successRate.add(ok);

  if (res.status >= 500) {
    lockContention.add(1);
  }

  // No sleep — we want sustained pressure
}

export function incrementUnsafe() {
  const res = http.post(`${BASE_URL}/counters/${COUNTER_UNSAFE}/increment-unsafe`, null, {
    tags: { scenario: 'unsafe' },
  });

  unsafeDuration.add(res.timings.duration);

  check(res, {
    'unsafe: status 200': (r) => r.status === 200,
  });
}

// ── Setup & Teardown ───────────────────────────────────────────────────────────

export function setup() {
  console.log(`🔒 Locked counter   : ${COUNTER_LOCKED}`);
  console.log(`⚠️  Unsafe counter   : ${COUNTER_UNSAFE}`);
  console.log(`📡 API base URL     : ${BASE_URL}`);

  // Health check
  const health = http.get(`${BASE_URL}/health`);
  if (health.status !== 200) {
    throw new Error(`API not healthy: ${health.status} ${health.body}`);
  }
}

export function teardown() {
  // Fetch final values for the summary report
  const lockedRes = http.get(`${BASE_URL}/counters/${COUNTER_LOCKED}`);
  const unsafeRes = http.get(`${BASE_URL}/counters/${COUNTER_UNSAFE}`);

  if (lockedRes.status === 200) {
    const locked = JSON.parse(lockedRes.body);
    console.log(`\n📊 Final Results`);
    console.log(`🔒 Locked  counter value : ${locked.value}`);
  }

  if (unsafeRes.status === 200) {
    const unsafe = JSON.parse(unsafeRes.body);
    console.log(`⚠️  Unsafe  counter value : ${unsafe.value}`);
    console.log(`   (compare vs total unsafe requests to see lost updates)`);
  }

  // Optional cleanup — comment out if you want to inspect values in Mongo
  // http.del(`${BASE_URL}/counters/${COUNTER_LOCKED}`);
  // http.del(`${BASE_URL}/counters/${COUNTER_UNSAFE}`);
}
