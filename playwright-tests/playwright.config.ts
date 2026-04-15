import { defineConfig } from '@playwright/test';
import * as path from 'path';

const logsDir = path.resolve(__dirname, '..', 'playwright-logs');

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: path.join(logsDir, 'html-report'), open: 'never' }],
    ['json', { outputFile: path.join(logsDir, 'results.json') }],
  ],
  use: {
    headless: false,            // You said you want to watch
    video: 'on',                // Record video of every test
    screenshot: 'on',           // Screenshot on every test (pass or fail)
    trace: 'on',                // Full trace for debugging
    actionTimeout: 10_000,
    navigationTimeout: 30_000,
  },
  outputDir: path.join(logsDir, 'test-artifacts'),
  projects: [
    {
      name: 'azure',
      use: {
        baseURL: 'https://2-blazor-hpf8f8c9g9gmhsde.westus-01.azurewebsites.net',
      },
    },
    {
      name: 'duckdns',
      use: {
        baseURL: 'https://activor.duckdns.org',
      },
    },
    {
      name: 'pr',
      use: {
        baseURL: 'http://pr-23.activor.duckdns.org',
      },
    },
  ],
});
