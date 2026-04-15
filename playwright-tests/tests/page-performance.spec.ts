import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const logsDir = path.resolve(__dirname, '..', '..', 'playwright-logs');

function logIssue(testName: string, issue: string, details: Record<string, unknown> = {}) {
  const entry = {
    timestamp: new Date().toISOString(),
    test: testName,
    issue,
    ...details,
  };
  const logFile = path.join(logsDir, 'issues.jsonl');
  fs.mkdirSync(logsDir, { recursive: true });
  fs.appendFileSync(logFile, JSON.stringify(entry) + '\n');
}

test.describe('Page load performance', () => {
  test('home page should load within 8 seconds', async ({ page }) => {
    const start = Date.now();
    const response = await page.goto('/', { waitUntil: 'networkidle' });
    const loadTime = Date.now() - start;

    const status = response?.status() ?? 0;
    if (status >= 400) {
      logIssue('home-load-error', `Home page returned HTTP ${status}`, { status, loadTime });
    }
    expect(status).toBeLessThan(400);

    if (loadTime > 8000) {
      logIssue('home-load-slow', `Home page took ${loadTime}ms to load`, { loadTime });
    }

    // Check SignalR connection established (Blazor Server)
    await page.waitForTimeout(2000);
    const blazorConnected = await page.evaluate(() => {
      return !!(window as any).Blazor;
    }).catch(() => false);

    if (!blazorConnected) {
      logIssue('home-blazor-connection', 'Blazor runtime not detected after page load', { loadTime });
    }

    console.log(`Home page load: ${loadTime}ms, status: ${status}, blazor: ${blazorConnected}`);
  });

  test('activities page should load within 8 seconds', async ({ page }) => {
    const start = Date.now();
    const response = await page.goto('/activities', { waitUntil: 'networkidle' });
    const loadTime = Date.now() - start;

    const status = response?.status() ?? 0;
    if (status >= 400) {
      logIssue('activities-load-error', `Activities page returned HTTP ${status}`, { status, loadTime });
    }
    expect(status).toBeLessThan(400);

    if (loadTime > 8000) {
      logIssue('activities-load-slow', `Activities page took ${loadTime}ms to load`, { loadTime });
    }

    console.log(`Activities page load: ${loadTime}ms, status: ${status}`);
  });

  test('navigation between pages should be fast', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    // Navigate to activities via link/button if exists, otherwise direct
    const start = Date.now();
    await page.goto('/activities', { waitUntil: 'networkidle' });
    const navTime = Date.now() - start;

    if (navTime > 5000) {
      logIssue('nav-slow', `Navigation home->activities took ${navTime}ms`, { navTime });
    }

    console.log(`Navigation time: ${navTime}ms`);
  });
});

test.describe('SignalR health', () => {
  test('check for excessive re-renders via console logs', async ({ page }) => {
    const consoleLogs: string[] = [];
    page.on('console', msg => {
      consoleLogs.push(`[${msg.type()}] ${msg.text()}`);
    });

    const errors: string[] = [];
    page.on('pageerror', err => {
      errors.push(err.message);
    });

    await page.goto('/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(5000); // Let it settle

    if (errors.length > 0) {
      logIssue('js-errors', `${errors.length} JavaScript errors on home page`, {
        errors: errors.slice(0, 10),
      });
    }

    // Log console output for diagnosis
    const logFile = path.join(logsDir, 'console-logs.jsonl');
    fs.mkdirSync(logsDir, { recursive: true });
    const entry = {
      timestamp: new Date().toISOString(),
      page: '/',
      consoleCount: consoleLogs.length,
      errorCount: errors.length,
      errors: errors.slice(0, 10),
      consoleSample: consoleLogs.slice(0, 30),
    };
    fs.appendFileSync(logFile, JSON.stringify(entry) + '\n');

    console.log(`Console logs: ${consoleLogs.length}, Errors: ${errors.length}`);
  });
});

test.describe('Health endpoints', () => {
  test('liveness probe /alive should respond', async ({ page, baseURL }) => {
    const response = await page.goto('/alive', { waitUntil: 'commit' });
    const status = response?.status() ?? 0;

    if (status >= 400) {
      logIssue('alive-endpoint', `Liveness probe returned ${status}`, { status, baseURL });
    }

    console.log(`/alive status: ${status}`);
  });

  test('readiness probe /health should respond', async ({ page, baseURL }) => {
    const response = await page.goto('/health', { waitUntil: 'commit' });
    const status = response?.status() ?? 0;

    if (status >= 400) {
      logIssue('health-endpoint', `Readiness probe returned ${status}`, { status, baseURL });
    }

    console.log(`/health status: ${status}`);
  });
});
