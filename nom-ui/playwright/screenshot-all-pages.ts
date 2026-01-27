import { chromium, type Browser, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE_URL = 'http://localhost:4200';
const SCREENSHOT_DIR = path.join(__dirname, '../screenshots');

// Public routes (no auth required)
const publicRoutes = [
  { path: '/home', name: '01-home' },
  { path: '/register', name: '02-auth-register' },
  { path: '/forgot-password', name: '03-auth-forgot-password' },
  { path: '/reset-password', name: '04-auth-reset-password' },
  { path: '/confirm-email', name: '05-auth-confirm-email' },
  { path: '/send-confirmation', name: '06-auth-send-confirmation' },
  { path: '/login', name: '07-auth-login' },
];

// Routes that require authentication (will show login redirect)
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

  // Recipe
  { path: '/recipes', name: '50-recipe-dashboard' },
  { path: '/recipes/search', name: '51-recipe-search' },
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

async function main(): Promise<void> {
  console.log('Screenshot All Pages - Playwright\n');

  await ensureDir(SCREENSHOT_DIR);

  const browser: Browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 720 }
  });
  const page: Page = await context.newPage();

  console.log('Public Pages:');
  for (const route of publicRoutes) {
    await captureRoute(page, route);
  }

  console.log('\nAuthenticated Pages (may show login redirect):');
  for (const route of authRoutes) {
    await captureRoute(page, route);
  }

  await browser.close();

  console.log(`\n✓ Screenshots saved to: ${SCREENSHOT_DIR}`);
}

main().catch(console.error);
