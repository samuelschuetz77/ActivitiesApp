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

// ---------- HOME PAGE SEARCH ----------

test.describe('Home page search bar', () => {
  test('typing in search bar should not corrupt input text', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const searchInput = page.locator('input.search-input');
    await expect(searchInput).toBeVisible({ timeout: 15_000 });

    // Type a word character by character with realistic delay
    const testWord = 'pizza';
    await searchInput.click();
    await searchInput.pressSequentially(testWord, { delay: 80 });

    // Wait for any async re-renders to settle
    await page.waitForTimeout(500);

    const actualValue = await searchInput.inputValue();
    if (actualValue !== testWord) {
      logIssue('home-search-typing', 'Search input text corrupted during typing', {
        expected: testWord,
        actual: actualValue,
        page: 'Home',
      });
    }
    expect(actualValue).toBe(testWord);
  });

  test('search results should appear after typing', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const searchInput = page.locator('input.search-input');
    await expect(searchInput).toBeVisible({ timeout: 15_000 });
    await searchInput.click();
    await searchInput.pressSequentially('food', { delay: 80 });

    // Wait for debounce (200ms) + render
    await page.waitForTimeout(800);

    const searchResultsSection = page.locator('#search-results');
    const hasResults = await searchResultsSection.isVisible().catch(() => false);

    if (!hasResults) {
      logIssue('home-search-results', 'No search results section appeared after typing "food"', {
        page: 'Home',
      });
    }
    // Not a hard fail — might have no data. Log it.
  });

  test('clearing search should remove search results', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const searchInput = page.locator('input.search-input');
    await expect(searchInput).toBeVisible({ timeout: 15_000 });
    await searchInput.fill('pizza');
    await page.waitForTimeout(500);
    await searchInput.fill('');
    await page.waitForTimeout(500);

    const searchResultsSection = page.locator('#search-results');
    const stillVisible = await searchResultsSection.isVisible().catch(() => false);
    if (stillVisible) {
      logIssue('home-search-clear', 'Search results still visible after clearing input', {
        page: 'Home',
      });
    }
    expect(stillVisible).toBe(false);
  });
});

// ---------- ACTIVITIES PAGE SEARCH ----------

test.describe('Activities page search bar', () => {
  test('typing in search bar should not corrupt input text', async ({ page }) => {
    await page.goto('/activities', { waitUntil: 'networkidle' });

    const searchInput = page.locator('input.search-input');
    await expect(searchInput).toBeVisible({ timeout: 15_000 });

    const testWord = 'hiking';
    await searchInput.click();
    await searchInput.pressSequentially(testWord, { delay: 80 });

    await page.waitForTimeout(500);

    const actualValue = await searchInput.inputValue();
    if (actualValue !== testWord) {
      logIssue('activities-search-typing', 'Search input text corrupted during typing', {
        expected: testWord,
        actual: actualValue,
        page: 'Activities',
      });
    }
    expect(actualValue).toBe(testWord);
  });

  test('rapid typing should not lose characters', async ({ page }) => {
    await page.goto('/activities', { waitUntil: 'networkidle' });

    const searchInput = page.locator('input.search-input');
    await expect(searchInput).toBeVisible({ timeout: 15_000 });

    // Fast typing — this is what triggers the "gopher" bug
    const testWord = 'restaurant';
    await searchInput.click();
    await searchInput.pressSequentially(testWord, { delay: 30 });

    await page.waitForTimeout(600);

    const actualValue = await searchInput.inputValue();
    if (actualValue !== testWord) {
      logIssue('activities-search-rapid-typing', 'Characters lost/added during rapid typing', {
        expected: testWord,
        actual: actualValue,
        page: 'Activities',
        typingDelay: '30ms',
      });
    }
    expect(actualValue).toBe(testWord);
  });
});
