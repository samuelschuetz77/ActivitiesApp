import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const logsDir = path.resolve(__dirname, '..', '..', 'playwright-logs');

function log(msg: string) {
  const line = `[${new Date().toISOString()}] ${msg}`;
  console.log(line);
  fs.mkdirSync(logsDir, { recursive: true });
  fs.appendFileSync(path.join(logsDir, 'perf-run.log'), line + '\n');
}

// Single test — one browser, check all health/perf in sequence
test('Health and performance check', async ({ page }) => {
  test.setTimeout(60_000);

  // ── Health endpoints ──
  log('Checking /alive...');
  let resp = await page.goto('/alive', { waitUntil: 'commit' });
  log(`/alive → ${resp?.status()}`);

  log('Checking /health...');
  resp = await page.goto('/health', { waitUntil: 'commit' });
  log(`/health → ${resp?.status()}`);

  // ── Page load timing ──
  const pages = ['/', '/activities'];
  for (const p of pages) {
    const start = Date.now();
    resp = await page.goto(p, { waitUntil: 'networkidle' });
    const ms = Date.now() - start;
    log(`${p} loaded in ${ms}ms (status ${resp?.status()})`);
    expect(resp?.status()).toBeLessThan(400);
  }

  // ── Check version ──
  log('Checking deployed version...');
  resp = await page.goto('/api/version', { waitUntil: 'commit' });
  const versionText = await page.locator('body').textContent();
  log(`Version: ${versionText?.substring(0, 200)}`);
});
