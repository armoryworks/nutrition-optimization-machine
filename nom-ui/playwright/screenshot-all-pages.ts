import { chromium, type Browser, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE_URL = process.env['BASE_URL'] || 'http://localhost:4200';
const API_URL = process.env['API_URL'] || 'http://localhost:8080';
const SCREENSHOT_DIR = path.join(__dirname, '../screenshots');

const SCREENSHOT_USER = process.env['SCREENSHOT_USER'] || 'admin@nom.local';
const SCREENSHOT_PASS = process.env['SCREENSHOT_PASS'] || 'Admin123!';

// Public routes (no auth required)
const publicRoutes = [
  { path: '/home', name: '01-home' },
  { path: '/register', name: '02-auth-register' },
  { path: '/forgot-password', name: '03-auth-forgot-password' },
  { path: '/reset-password', name: '04-auth-reset-password' },
  { path: '/confirm-email', name: '05-auth-confirm-email' },
  { path: '/send-confirmation', name: '06-auth-send-confirmation' },
  { path: '/search', name: '51-recipe-search' },
];

// Routes that require authentication
const authRoutes = [
  // User
  { path: '/user/dashboard', name: '10-user-dashboard' },
  { path: '/user/privacy-settings', name: '11-user-privacy-settings' },
  { path: '/edit-profile', name: '12-user-edit-profile' },
  { path: '/update-info', name: '13-user-update-info' },
  { path: '/update-two-factor', name: '14-user-update-two-factor' },

  // Household
  { path: '/household', name: '20-household-dashboard' },
  { path: '/household/create', name: '21-household-create' },

  // Shopping
  { path: '/shopping', name: '30-shopping-dashboard' },
  { path: '/shopping/create', name: '31-shopping-create' },
  { path: '/shopping/categories', name: '32-shopping-categories' },

  // Meal Plan
  { path: '/meal-plan', name: '40-mealplan-dashboard' },
  { path: '/meal-plan/create', name: '41-mealplan-create' },
  { path: '/meal-plan/calendar', name: '42-mealplan-calendar' },
  { path: '/meal-plan/rules', name: '43-mealplan-rules' },
  { path: '/meal-plan/recipe-selection', name: '44-mealplan-recipe-selection' },
  { path: '/meal-plan/nutrition', name: '45-mealplan-nutrition' },

  // Recipe
  { path: '/recipes', name: '50-recipe-dashboard' },
  { path: '/recipes/new', name: '52-recipe-new' },

  // Communication
  { path: '/communication', name: '60-communication-inbox' },
  { path: '/communication/new', name: '61-communication-compose' },

  // Curation
  { path: '/curation', name: '70-curation-queue' },

  // Admin
  { path: '/admin/user-management', name: '80-admin-users' },

  // Onboarding
  { path: '/onboarding', name: '90-onboarding-wizard' },

  // Plans
  { path: '/curated-plans', name: '91-curated-plans' },

  // Ingredient Search
  { path: '/ingredient-search', name: '92-ingredient-search' },

  // Cookbook
  { path: '/cookbook', name: '93-cookbook-dashboard' },
  { path: '/cookbook/create', name: '94-cookbook-create' },

  // Webhook
  { path: '/webhook', name: '95-webhook-dashboard' },

  // Labels
  { path: '/labels', name: '96-label-dashboard' },
];

async function ensureDir(dir: string): Promise<void> {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

async function waitForAngular(page: Page): Promise<void> {
  try {
    await page.waitForFunction(() => {
      const win = window as any;
      if (win.getAllAngularTestabilities) {
        const testabilities = win.getAllAngularTestabilities();
        return testabilities.every((t: any) => t.isStable());
      }
      return true;
    }, { timeout: 10000 });
  } catch {
    // Continue if Angular check times out
  }
}

async function screenshotPage(page: Page, name: string): Promise<void> {
  const filepath = path.join(SCREENSHOT_DIR, `${name}.png`);
  await page.screenshot({ path: filepath, fullPage: false });
  console.log(`  ✓ ${name}`);
}

async function captureRoute(page: Page, route: { path: string; name: string }): Promise<void> {
  try {
    await page.goto(`${BASE_URL}${route.path}`, { waitUntil: 'networkidle', timeout: 15000 });
    await waitForAngular(page);
    await page.waitForTimeout(1000); // Wait for animations
    await screenshotPage(page, route.name);
  } catch (error) {
    console.log(`  ✗ ${route.name} - ${(error as Error).message}`);
  }
}

async function registerUser(page: Page): Promise<boolean> {
  try {
    console.log(`\nRegistering user ${SCREENSHOT_USER}...`);

    const response = await page.request.post(`${API_URL}/api/auth/register-custom`, {
      data: {
        email: SCREENSHOT_USER,
        username: SCREENSHOT_USER,
        password: SCREENSHOT_PASS,
        confirmPassword: SCREENSHOT_PASS,
        fullName: 'Screenshot Admin',
      },
    });

    if (response.ok()) {
      console.log('  ✓ User registered successfully');
      return true;
    }

    const status = response.status();
    if (status === 400) {
      // User likely already exists
      console.log('  ⊘ User already exists (continuing to login)');
      return true;
    }

    console.log(`  ✗ Registration failed: ${status} ${response.statusText()}`);
    return false;
  } catch (error) {
    console.log(`  ✗ Registration error: ${(error as Error).message}`);
    return false;
  }
}

async function loginAndSetToken(page: Page): Promise<boolean> {
  try {
    // Ensure user exists before attempting login
    await registerUser(page);

    console.log(`Authenticating as ${SCREENSHOT_USER}...`);

    const response = await page.request.post(`${API_URL}/api/auth/login`, {
      data: {
        email: SCREENSHOT_USER,
        password: SCREENSHOT_PASS,
        twoFactorCode: '',
        twoFactorRecoveryCode: '',
      },
    });

    if (!response.ok()) {
      console.log(`  ✗ Login failed: ${response.status()} ${response.statusText()}`);
      return false;
    }

    const body = await response.json();
    const token = body.accessToken;

    if (!token) {
      console.log('  ✗ Login succeeded but no accessToken in response');
      return false;
    }

    // Navigate to app first so localStorage is on the right origin
    await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 15000 });

    // Set the auth token in localStorage (matches AuthService.login behavior)
    await page.evaluate((t: string) => {
      localStorage.setItem('authToken', t);
    }, token);

    console.log('  ✓ Authenticated successfully\n');
    return true;
  } catch (error) {
    console.log(`  ✗ Login error: ${(error as Error).message}`);
    return false;
  }
}

async function main(): Promise<void> {
  console.log('Screenshot All Pages - Playwright\n');

  await ensureDir(SCREENSHOT_DIR);

  const browser: Browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
  });
  const page: Page = await context.newPage();

  // Capture public pages first (anonymous view)
  console.log('Public Pages:');
  for (const route of publicRoutes) {
    await captureRoute(page, route);
  }

  // Authenticate for protected pages
  const loggedIn = await loginAndSetToken(page);

  if (loggedIn) {
    console.log('Authenticated Pages:');
    for (const route of authRoutes) {
      await captureRoute(page, route);
    }
  } else {
    console.log('Authenticated Pages (SKIPPED - login failed):');
    console.log('  Set SCREENSHOT_USER and SCREENSHOT_PASS environment variables');
    console.log('  or ensure the default test account exists.');
  }

  await browser.close();

  console.log(`\nScreenshots saved to: ${SCREENSHOT_DIR}`);
}

main().catch(console.error);
