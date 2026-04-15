import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const logsDir = path.resolve(__dirname, '..', '..', 'playwright-logs');

function logIssue(issue: string, details: Record<string, unknown> = {}) {
  const entry = { timestamp: new Date().toISOString(), issue, ...details };
  fs.mkdirSync(logsDir, { recursive: true });
  fs.appendFileSync(path.join(logsDir, 'issues.jsonl'), JSON.stringify(entry) + '\n');
}

function log(msg: string) {
  const line = `[${new Date().toISOString()}] ${msg}`;
  console.log(line);
  fs.mkdirSync(logsDir, { recursive: true });
  fs.appendFileSync(path.join(logsDir, 'test-run.log'), line + '\n');
}

async function checkForBlazorCrash(page: Page, context: string): Promise<boolean> {
  // Blazor shows "An unhandled error has occurred. Reload" banner on crash
  const errorBanner = page.locator('text=An unhandled error has occurred');
  if (await errorBanner.isVisible({ timeout: 500 }).catch(() => false)) {
    logIssue('Blazor crash', { context });
    log(`BLAZOR CRASHED during: ${context}`);
    await page.screenshot({ path: path.join(logsDir, `blazor-crash-${context}.png`) });

    // Click reload link or refresh page
    const reloadLink = page.locator('text=Reload');
    if (await reloadLink.isVisible().catch(() => false)) {
      await reloadLink.click();
      await page.waitForTimeout(3000);
    } else {
      await page.reload({ waitUntil: 'networkidle' });
      await page.waitForTimeout(2000);
    }
    return true;
  }
  return false;
}

// Single test — one browser, full user journey
test('Full search and location flow', async ({ page }) => {
  test.setTimeout(120_000);

  const errors: string[] = [];
  page.on('pageerror', err => errors.push(err.message));
  page.on('console', msg => {
    if (msg.type() === 'error') errors.push(`[console] ${msg.text()}`);
  });

  // ── 1. Load home page ──
  log('Loading home page...');
  const loadStart = Date.now();
  await page.goto('/', { waitUntil: 'networkidle' });
  const loadMs = Date.now() - loadStart;
  log(`Home loaded in ${loadMs}ms`);

  if (loadMs > 8000) {
    logIssue('Home page slow', { loadMs });
  }

  // Wait for Blazor to be interactive
  await page.waitForTimeout(1500);

  // ── 2. Set location to Pleasant Hill, Oregon via ZIP ──
  log('Setting location to Pleasant Hill, OR (97455)...');

  const locationBtn = page.locator('button.location-btn');
  await expect(locationBtn).toBeVisible({ timeout: 10_000 });
  await locationBtn.click();
  log('Location popup opened');

  // Check if Blazor crashed from location click
  const crashedOnLocation = await checkForBlazorCrash(page, 'location-popup-open');
  if (crashedOnLocation) {
    log('Blazor crashed opening location popup — retrying after reload...');
    await page.waitForTimeout(2000);
    // Try again after reload
    const retryBtn = page.locator('button.location-btn');
    if (await retryBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await retryBtn.click();
      await page.waitForTimeout(1000);
    }
  }

  const zipInput = page.locator('input.zip-input');
  const zipVisible = await zipInput.isVisible({ timeout: 5_000 }).catch(() => false);
  if (zipVisible) {
    await zipInput.fill('97455');
    log('Entered ZIP 97455');

    const applyBtn = page.locator('button.zip-btn', { hasText: 'Apply' });
    await applyBtn.click();
    log('Clicked Apply');

    // Wait for location to resolve and popup to close
    await page.waitForTimeout(3000);
    await checkForBlazorCrash(page, 'location-apply');

    // Check if location error appeared
    const locError = page.locator('.location-error');
    if (await locError.isVisible().catch(() => false)) {
      const errorText = await locError.textContent();
      logIssue('Location set failed', { zip: '97455', error: errorText });
      log(`Location error: ${errorText}`);
    } else {
      log('Location set successfully');
    }
  } else {
    log('ZIP input never appeared — location popup broken');
    logIssue('Location popup ZIP input missing', { crashedOnLocation });
    await page.screenshot({ path: path.join(logsDir, 'zip-input-missing.png') });
  }

  // ── 3. Test search on home page ──
  log('Testing home page search...');
  const homeSearch = page.locator('input.search-input');
  await expect(homeSearch).toBeVisible({ timeout: 5_000 });

  // Test: type "pizza" and check input stays intact
  await homeSearch.click();
  await homeSearch.pressSequentially('pizza', { delay: 100 });
  await page.waitForTimeout(800);

  let val = await homeSearch.inputValue();
  log(`Typed "pizza", input shows: "${val}"`);
  if (val !== 'pizza') {
    logIssue('Home search corrupted input', { typed: 'pizza', got: val });
    // Take screenshot of the corruption
    await page.screenshot({ path: path.join(logsDir, 'home-search-corrupted.png') });
  }

  // Wait for search results
  await page.waitForTimeout(1000);
  const searchResults = page.locator('#search-results');
  const hasResults = await searchResults.isVisible().catch(() => false);
  if (hasResults) {
    const resultCount = await page.locator('#search-results .activity-card').count();
    log(`Search results appeared: ${resultCount} cards`);
  } else {
    log('No search results section visible (may have no data)');
  }

  // Clear and try another search
  await homeSearch.fill('');
  await page.waitForTimeout(500);
  await homeSearch.pressSequentially('hiking', { delay: 100 });
  await page.waitForTimeout(800);

  val = await homeSearch.inputValue();
  log(`Typed "hiking", input shows: "${val}"`);
  if (val !== 'hiking') {
    logIssue('Home search corrupted on second search', { typed: 'hiking', got: val });
  }

  // Clear search
  await homeSearch.fill('');
  await page.waitForTimeout(500);

  // ── 4. Try tag cards on home page ──
  log('Testing tag card click...');
  const tagCard = page.locator('.tag-card').first();
  if (await tagCard.isVisible().catch(() => false)) {
    const tagName = await tagCard.locator('.tag-card-label').textContent();
    log(`Clicking tag: "${tagName}"`);
    await tagCard.click();
    await page.waitForTimeout(3000);

    const filteredSection = page.locator('#filtered-results');
    if (await filteredSection.isVisible().catch(() => false)) {
      const cardCount = await page.locator('#filtered-results .activity-card').count();
      log(`Tag "${tagName}" returned ${cardCount} activities`);
    } else {
      const loading = page.locator('.loading');
      if (await loading.isVisible().catch(() => false)) {
        log(`Tag "${tagName}" still loading...`);
        await page.waitForTimeout(5000);
      }
      log(`Tag "${tagName}" — no filtered results section visible`);
    }
  }

  // ── 5. Navigate to Activities page ──
  log('Navigating to /activities...');
  await page.goto('/activities', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);
  log('Activities page loaded');

  // ── 6. Set location again on Activities page ──
  log('Setting location on activities page...');
  const actLocBtn = page.locator('button.location-btn');
  if (await actLocBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await actLocBtn.click();
    await page.waitForTimeout(500);

    const crashedAct = await checkForBlazorCrash(page, 'activities-location-popup');
    if (crashedAct) {
      log('Blazor crashed on activities location popup too — skipping location set');
    } else {
      const actZipInput = page.locator('input.zip-input');
      if (await actZipInput.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await actZipInput.fill('97455');
        const actApplyBtn = page.locator('button.zip-btn', { hasText: 'Apply' });
        await actApplyBtn.click();
        await page.waitForTimeout(3000);
        await checkForBlazorCrash(page, 'activities-location-apply');
        log('Location set on activities page');
      } else {
        log('ZIP input not visible on activities page');
      }
    }
  } else {
    log('Location button not visible on activities page');
  }

  // ── 7. Test search on Activities page ──
  log('Testing activities search...');
  const actSearch = page.locator('input.search-input');
  await expect(actSearch).toBeVisible({ timeout: 5_000 });

  const searchTerms = ['restaurant', 'park', 'coffee', 'bar'];
  for (const term of searchTerms) {
    await actSearch.fill('');
    await page.waitForTimeout(300);
    await actSearch.pressSequentially(term, { delay: 80 });
    await page.waitForTimeout(600);

    val = await actSearch.inputValue();
    const cardCount = await page.locator('.cards-grid .activity-card').count();
    log(`Search "${term}" → input="${val}", ${cardCount} results`);

    if (val !== term) {
      logIssue('Activities search corrupted', { typed: term, got: val });
      await page.screenshot({ path: path.join(logsDir, `activities-search-${term}-corrupted.png`) });
    }
  }

  // ── 8. Test rapid typing (the gopher test) ──
  log('Testing rapid typing...');
  await actSearch.fill('');
  await page.waitForTimeout(300);
  await actSearch.pressSequentially('mediterranean food', { delay: 30 });
  await page.waitForTimeout(800);

  val = await actSearch.inputValue();
  log(`Rapid typed "mediterranean food" → input="${val}"`);
  if (val !== 'mediterranean food') {
    logIssue('Rapid typing lost characters', { typed: 'mediterranean food', got: val });
    await page.screenshot({ path: path.join(logsDir, 'rapid-typing-corrupted.png') });
  }

  // ── 9. Test filter dropdowns ──
  log('Testing filter dropdowns...');
  await actSearch.fill('');
  await page.waitForTimeout(300);

  const radiusSelect = page.locator('select.filter-select').first();
  if (await radiusSelect.isVisible().catch(() => false)) {
    // Radius select has values: 5, 10, 25, 50
    await radiusSelect.selectOption('25');
    await page.waitForTimeout(2000);
    const widerCount = await page.locator('.cards-grid .activity-card').count();
    log(`Radius 25mi → ${widerCount} results`);

    await radiusSelect.selectOption('10');
    await page.waitForTimeout(1000);
    const normalCount = await page.locator('.cards-grid .activity-card').count();
    log(`Radius 10mi → ${normalCount} results`);
  }

  await checkForBlazorCrash(page, 'after-filters');

  // ── 10. Click into an activity detail ──
  log('Testing activity detail navigation...');
  const firstCard = page.locator('.activity-card').first();
  if (await firstCard.isVisible().catch(() => false)) {
    const cardTitle = await firstCard.locator('.card-title').textContent();
    log(`Clicking activity: "${cardTitle}"`);
    await firstCard.click();
    await page.waitForTimeout(2000);

    const currentUrl = page.url();
    log(`Navigated to: ${currentUrl}`);
    if (currentUrl.includes('/activity/')) {
      log('Activity detail page loaded');
    } else {
      logIssue('Activity detail navigation failed', { expected: '/activity/*', got: currentUrl });
    }
  } else {
    log('No activity cards visible to click');
  }

  // ── Summary ──
  log(`\nDone. JS errors: ${errors.length}`);
  if (errors.length > 0) {
    logIssue('JavaScript errors during test', { count: errors.length, errors: errors.slice(0, 10) });
    errors.forEach(e => log(`  ERROR: ${e}`));
  }

  await page.screenshot({ path: path.join(logsDir, 'final-state.png'), fullPage: true });
  log('Final screenshot saved');
});
