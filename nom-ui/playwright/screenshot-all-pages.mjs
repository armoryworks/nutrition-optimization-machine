import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import crypto from 'crypto';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const BASE_URL = 'http://localhost:4200';
const API_URL = 'http://localhost:7053/api';
const SCREENSHOT_DIR = path.join(__dirname, '../screenshots');

// Generate random test user credentials
function generateTestUser() {
  const randomId = crypto.randomBytes(4).toString('hex');
  return {
    email: `test.user.${randomId}@example.com`,
    password: `TestPass123!${randomId}`,
    fullName: `Test User ${randomId}`,
    username: `testuser${randomId}`
  };
}

// Public routes - captured BEFORE login (no auth required)
const publicRoutes = {
  'public': [
    { path: '/home', name: '01-home' },
    { path: '/about', name: '02-about' },
  ],
  'auth': [
    { path: '/register', name: '03-auth-register' },
    { path: '/forgot-password', name: '04-auth-forgot-password' },
    { path: '/reset-password', name: '05-auth-reset-password' },
    { path: '/confirm-email', name: '06-auth-confirm-email' },
    { path: '/send-confirmation', name: '07-auth-send-confirmation' },
  ],
};

// Authenticated routes - captured AFTER login
const authRoutes = {
  'user': [
    { path: '/user/dashboard', name: '10-user-dashboard' },
    { path: '/user/privacy-settings', name: '11-user-privacy-settings' },
    { path: '/edit-profile', name: '12-user-edit-profile' },
    { path: '/update-info', name: '13-user-update-info' },
    { path: '/update-two-factor', name: '14-user-update-two-factor' },
  ],
  'household': [
    { path: '/household', name: '20-household-dashboard' },
    { path: '/household/create', name: '21-household-create' },
    // Note: /household/:id routes require actual household data
  ],
  'shopping': [
    { path: '/shopping', name: '30-shopping-dashboard' },
    { path: '/shopping/create', name: '31-shopping-create' },
    { path: '/shopping/categories', name: '32-shopping-categories' },
    // Note: /shopping/:id routes require actual shopping list data
  ],
  'meal-plan': [
    { path: '/meal-plan', name: '40-mealplan-dashboard' },
    { path: '/meal-plan/create', name: '41-mealplan-create' },
    { path: '/meal-plan/calendar', name: '42-mealplan-calendar' },
    { path: '/meal-plan/rules', name: '43-mealplan-rules' },
    { path: '/meal-plan/recipe-selection', name: '44-mealplan-recipe-selection' },
    { path: '/meal-plan/shopping-list', name: '45-mealplan-shopping-list' },
    { path: '/meal-plan/print', name: '46-mealplan-print' },
    { path: '/meal-plan/nutrition', name: '47-mealplan-nutrition' },
    // Note: /meal-plan/:id routes require actual meal plan data
  ],
  'recipe': [
    { path: '/recipes', name: '50-recipe-dashboard' },
    { path: '/recipes/search', name: '51-recipe-search' },
    { path: '/recipes/new', name: '52-recipe-new' },
    { path: '/recipes/ingredients/new', name: '53-recipe-ingredient-new' },
    // Note: /recipes/:id routes require actual recipe data
  ],
  'communication': [
    { path: '/communication', name: '60-communication-inbox' },
    { path: '/communication/new', name: '61-communication-compose' },
    // Note: /communication/thread/:id requires actual thread data
  ],
  'admin': [
    { path: '/curation', name: '70-curation-queue' },
    { path: '/admin/user-management', name: '80-admin-users' },
  ],
  'onboarding': [
    { path: '/onboarding', name: '90-onboarding-wizard' },
    { path: '/onboarding/invitationCode', name: '91-onboarding-invitation' },
    { path: '/onboarding/additionalParticipants', name: '92-onboarding-participants' },
    { path: '/onboarding/restrictionScope', name: '93-onboarding-restrictions' },
    { path: '/curated-plans', name: '94-curated-plans' },
    { path: '/ingredient-search', name: '95-ingredient-search' },
  ],
};

function ensureDir(dir) {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

async function waitForAngular(page) {
  try {
    await page.waitForFunction(() => {
      const win = window;
      if (win.getAllAngularTestabilities) {
        const testabilities = win.getAllAngularTestabilities();
        return testabilities.every(t => t.isStable());
      }
      return true;
    }, { timeout: 10000 });
  } catch {
    // Continue if Angular check times out
  }
}

// Register a new user via API
async function registerUser(user) {
  console.log(`Registering test user: ${user.email}`);

  const response = await fetch(`${API_URL}/auth/register-custom`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      email: user.email,
      username: user.email,
      password: user.password,
      confirmPassword: user.password,
      fullName: user.fullName,
      groupToken: null,
      householdToken: null
    })
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Registration failed: ${response.status} - ${text}`);
  }

  console.log('✓ User registered successfully');
  return true;
}

// Login through the UI popover
async function loginThroughUI(page, user) {
  console.log(`Logging in through UI popover as: ${user.email}`);

  // Set up network monitoring for the login request
  let loginResponse = null;
  page.on('response', async (response) => {
    if (response.url().includes('/auth/login')) {
      loginResponse = {
        status: response.status(),
        body: await response.text().catch(() => '')
      };
      console.log(`  API Response: ${response.status()} - ${loginResponse.body.substring(0, 100)}`);
    }
  });

  // Navigate to home page
  await page.goto(`${BASE_URL}/home`, { waitUntil: 'networkidle' });
  await waitForAngular(page);
  await page.waitForTimeout(1000);

  // Click the Login button to open the popover
  const loginButton = page.locator('button:has-text("Login"), amw-button:has-text("Login")').first();
  await loginButton.click();
  console.log('  Clicked Login button');

  // Wait for the login popover to appear
  await page.waitForSelector('[data-cy="login-popover"]', { timeout: 5000 });
  console.log('  Login popover opened');

  // Fill in the form - the inputs are inside amw-input components
  const popover = page.locator('[data-cy="login-popover"]');

  // Wait for inputs to be ready
  await page.waitForTimeout(500);

  // Get the actual native input elements
  const emailInput = popover.locator('input').first();
  const passwordInput = popover.locator('input').nth(1);

  // Fill email with proper event triggering
  await emailInput.click();
  await emailInput.fill(user.email);
  await emailInput.dispatchEvent('input');
  await emailInput.dispatchEvent('change');
  console.log('  Filled email');

  // Fill password with proper event triggering
  await passwordInput.click();
  await passwordInput.fill(user.password);
  await passwordInput.dispatchEvent('input');
  await passwordInput.dispatchEvent('change');
  await passwordInput.blur();
  console.log('  Filled password');

  // Wait for Angular to process
  await page.waitForTimeout(500);
  await waitForAngular(page);

  // Check form validity before submitting
  const formValid = await page.evaluate(() => {
    const form = document.querySelector('[data-cy="login-popover"] form');
    return form ? form.checkValidity() : 'no form found';
  });
  console.log(`  Form valid: ${formValid}`);

  // Check if button is disabled
  const submitButton = popover.locator('button:has-text("Sign In")').first();
  const isDisabled = await submitButton.isDisabled();
  console.log(`  Submit button disabled: ${isDisabled}`);

  // Take screenshot before clicking
  await page.screenshot({ path: path.join(SCREENSHOT_DIR, 'debug-before-submit.png') });

  // Click the Sign In button
  await submitButton.click({ force: true });
  console.log('  Clicked Sign In');

  // Wait for login to complete (may involve network request)
  await page.waitForTimeout(5000);
  await waitForAngular(page);

  // Take debug screenshot
  await page.screenshot({ path: path.join(SCREENSHOT_DIR, 'debug-login-attempt.png') });

  // Check if we're logged in
  const token = await page.evaluate(() => localStorage.getItem('authToken'));
  if (token) {
    console.log('✓ Login through UI successful');
    return true;
  }

  // Check for error messages
  const errorMsg = await popover.locator('.login-popover__error').textContent().catch(() => null);
  if (errorMsg) {
    console.log(`✗ Login failed: ${errorMsg}`);
  }

  // Check page for any visible error
  const pageContent = await page.content();
  if (pageContent.includes('Email not confirmed') || pageContent.includes('confirmation')) {
    console.log('✗ Login failed: Email confirmation required');
    console.log('  Note: New users need to confirm their email before logging in');
  } else {
    console.log('✗ Login through UI failed - no token found');
  }

  return false;
}

async function captureRoute(page, route, category) {
  try {
    await page.goto(`${BASE_URL}${route.path}`, {
      waitUntil: 'networkidle',
      timeout: 20000
    });

    await waitForAngular(page);
    await page.waitForTimeout(1500); // Wait for animations

    // Create category subdirectory
    const categoryDir = path.join(SCREENSHOT_DIR, category);
    ensureDir(categoryDir);

    const filepath = path.join(categoryDir, `${route.name}.png`);
    await page.screenshot({ path: filepath, fullPage: false });

    // Get final URL to see if redirected
    const finalUrl = page.url();
    const wasRedirected = !finalUrl.includes(route.path);

    console.log(`  ${wasRedirected ? '→' : '✓'} ${route.name}${wasRedirected ? ` (→ ${finalUrl.replace(BASE_URL, '')})` : ''}`);

    return { route, wasRedirected, finalUrl };
  } catch (error) {
    console.log(`  ✗ ${route.name} - ${error.message}`);
    return { route, error: error.message };
  }
}

async function main() {
  console.log('Screenshot All Pages - Playwright\n');
  console.log('Base URL:', BASE_URL);
  console.log('API URL:', API_URL);
  console.log('Output:', SCREENSHOT_DIR);
  console.log('');

  ensureDir(SCREENSHOT_DIR);

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 720 }
  });

  const page = await context.newPage();

  const results = {
    testUser: null,
    loggedIn: false,
    captured: [],
    redirected: [],
    failed: []
  };

  // ========================================
  // PHASE 1: Capture PUBLIC routes (before login)
  // ========================================
  console.log('='.repeat(50));
  console.log('PHASE 1: Public Pages (no auth)');
  console.log('='.repeat(50));

  for (const [category, categoryRoutes] of Object.entries(publicRoutes)) {
    console.log(`\n${category.toUpperCase()} Pages:`);

    for (const route of categoryRoutes) {
      const result = await captureRoute(page, route, category);

      if (result.error) {
        results.failed.push(result);
      } else if (result.wasRedirected) {
        results.redirected.push(result);
      } else {
        results.captured.push(result);
      }
    }
  }

  // ========================================
  // PHASE 2: Register and Login
  // ========================================
  console.log('\n' + '='.repeat(50));
  console.log('PHASE 2: Authentication');
  console.log('='.repeat(50) + '\n');

  const testUser = generateTestUser();
  results.testUser = { email: testUser.email, fullName: testUser.fullName };

  try {
    await registerUser(testUser);
    results.loggedIn = await loginThroughUI(page, testUser);
  } catch (error) {
    console.error('Authentication setup failed:', error.message);
    console.log('Continuing without authentication...\n');
  }

  // ========================================
  // PHASE 3: Capture AUTHENTICATED routes
  // ========================================
  console.log('\n' + '='.repeat(50));
  console.log('PHASE 3: Authenticated Pages');
  console.log('='.repeat(50));

  for (const [category, categoryRoutes] of Object.entries(authRoutes)) {
    console.log(`\n${category.toUpperCase()} Pages:`);

    for (const route of categoryRoutes) {
      const result = await captureRoute(page, route, category);

      if (result.error) {
        results.failed.push(result);
      } else if (result.wasRedirected) {
        results.redirected.push(result);
      } else {
        results.captured.push(result);
      }
    }
  }

  await browser.close();

  // ========================================
  // SUMMARY
  // ========================================
  console.log('\n' + '='.repeat(50));
  console.log('SUMMARY');
  console.log('='.repeat(50));
  console.log(`Test User: ${testUser.email}`);
  console.log(`Logged In: ${results.loggedIn}`);
  console.log(`✓ Captured: ${results.captured.length}`);
  console.log(`→ Redirected: ${results.redirected.length}`);
  console.log(`✗ Failed: ${results.failed.length}`);
  console.log(`Total Routes: ${results.captured.length + results.redirected.length + results.failed.length}`);
  console.log(`\nScreenshots saved to: ${SCREENSHOT_DIR}`);

  // Write summary JSON
  const summaryPath = path.join(SCREENSHOT_DIR, 'capture-summary.json');
  fs.writeFileSync(summaryPath, JSON.stringify(results, null, 2));
  console.log(`Summary saved to: ${summaryPath}`);
}

main().catch(console.error);
