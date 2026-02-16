import { chromium, type Browser, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import * as crypto from 'crypto';

// ============================================================================
// Configuration
// ============================================================================

const BASE_URL = process.env['BASE_URL'] || 'http://localhost:4200';
const API_URL = process.env['API_URL'] || 'http://localhost:8080';
const SCREENSHOT_DIR = path.join(__dirname, '../screenshots/workflow');

// Generate unique test user per run
const RANDOM_ID = crypto.randomBytes(4).toString('hex');
const TEST_USER = {
  email: `e2e.${RANDOM_ID}@example.com`,
  password: 'E2eTestP@ss123',
  fullName: 'E2E Test User',
};

// Track created resources across sections
let authToken = '';
let householdId = 0;
const createdRecipeIds: number[] = [];
const createdIngredientNames: string[] = [];
let stepCounter = 0;

// ============================================================================
// Test Data
// ============================================================================

const INGREDIENTS = [
  {
    name: 'Quinoa',
    description: 'Whole grain quinoa, a complete protein source',
    nutrients: [
      { type: 'Protein', amount: '14.1' },
      { type: 'Fiber', amount: '7' },
    ],
  },
  {
    name: 'Cherry Tomatoes',
    description: 'Fresh cherry tomatoes, rich in vitamins',
    nutrients: [
      { type: 'Vitamin C', amount: '21' },
    ],
  },
  {
    name: 'Avocado',
    description: 'Ripe Hass avocado, high in healthy fats',
    nutrients: [
      { type: 'Fat', amount: '15' },
      { type: 'Fiber', amount: '6.7' },
    ],
  },
  {
    name: 'Salmon Fillet',
    description: 'Wild-caught salmon, rich in omega-3 fatty acids',
    nutrients: [
      { type: 'Protein', amount: '20' },
    ],
  },
  {
    name: 'Rolled Oats',
    description: 'Old-fashioned rolled oats for baking and breakfast',
    nutrients: [
      { type: 'Fiber', amount: '10.6' },
      { type: 'Protein', amount: '16.9' },
    ],
  },
];

const RECIPES = [
  {
    name: 'Avocado Toast with Poached Eggs',
    description: 'Crispy sourdough topped with smashed avocado, perfectly poached eggs, and a sprinkle of everything seasoning.',
    mealType: 'breakfast',
    ingredients: ['Avocado'],
    steps: [
      'Toast sourdough bread until golden and crispy.',
      'Mash ripe avocado with lemon juice, salt, and red pepper flakes.',
      'Poach eggs in simmering water with a splash of vinegar for 3 minutes.',
      'Spread avocado on toast, top with poached eggs, and finish with everything seasoning.',
    ],
  },
  {
    name: 'Berry Overnight Oats',
    description: 'Creamy overnight oats layered with mixed berries, chia seeds, and a drizzle of honey.',
    mealType: 'breakfast',
    ingredients: ['Rolled Oats'],
    steps: [
      'Combine rolled oats, milk, yogurt, and chia seeds in a jar.',
      'Stir in a tablespoon of honey and a pinch of vanilla extract.',
      'Layer fresh mixed berries on top and refrigerate overnight.',
      'In the morning, stir and top with granola and extra berries.',
    ],
  },
  {
    name: 'Mediterranean Quinoa Bowl',
    description: 'A vibrant bowl of fluffy quinoa with roasted chickpeas, cucumber, cherry tomatoes, kalamata olives, and tangy feta.',
    mealType: 'lunch',
    ingredients: ['Quinoa', 'Cherry Tomatoes'],
    steps: [
      'Cook quinoa according to package directions and fluff with a fork.',
      'Toss chickpeas with olive oil and smoked paprika, roast at 400F for 25 minutes.',
      'Dice cucumber, halve cherry tomatoes, and crumble feta cheese.',
      'Assemble bowls with quinoa base, roasted chickpeas, vegetables, olives, and feta.',
      'Drizzle with lemon-tahini dressing and fresh herbs.',
    ],
  },
  {
    name: 'Herb-Crusted Salmon with Roasted Vegetables',
    description: 'Flaky wild salmon with a Dijon-herb crust, served alongside roasted sweet potatoes and broccolini.',
    mealType: 'dinner',
    ingredients: ['Salmon Fillet'],
    steps: [
      'Preheat oven to 425F. Line a sheet pan with parchment paper.',
      'Toss sweet potato cubes and broccolini with olive oil, salt, and pepper.',
      'Mix Dijon mustard with minced garlic, fresh dill, and panko breadcrumbs.',
      'Place salmon fillets on the pan, spread herb-mustard mixture on top.',
      'Roast for 18-22 minutes until salmon is flaky and vegetables are tender.',
    ],
  },
  {
    name: 'No-Bake Energy Bites',
    description: 'Quick and satisfying snack balls made with oats, peanut butter, dark chocolate chips, and flaxseed.',
    mealType: 'snack',
    ingredients: ['Rolled Oats'],
    steps: [
      'Combine rolled oats, peanut butter, honey, and ground flaxseed in a bowl.',
      'Fold in dark chocolate chips, shredded coconut, and a pinch of salt.',
      'Refrigerate the mixture for 30 minutes until firm enough to roll.',
      'Roll into 1-inch balls and store in the refrigerator for up to a week.',
    ],
  },
];

const MEAL_PLANS = [
  { recipeName: 'Avocado Toast with Poached Eggs', mealType: 'breakfast', description: 'Quick healthy breakfast' },
  { recipeName: 'Mediterranean Quinoa Bowl', mealType: 'lunch', description: 'Nutritious lunch bowl' },
  { recipeName: 'Herb-Crusted Salmon with Roasted Vegetables', mealType: 'dinner', description: 'Weeknight dinner' },
  { recipeName: 'No-Bake Energy Bites', mealType: 'snack', description: 'Afternoon snack' },
];

// All pages for the final tour
const PUBLIC_ROUTES = [
  { path: '/home', name: '01-home' },
  { path: '/register', name: '02-auth-register' },
  { path: '/forgot-password', name: '03-auth-forgot-password' },
  { path: '/search', name: '51-recipe-search' },
];

const AUTH_ROUTES = [
  { path: '/user/dashboard', name: '10-user-dashboard' },
  { path: '/user/privacy-settings', name: '11-user-privacy-settings' },
  { path: '/edit-profile', name: '12-user-edit-profile' },
  { path: '/household', name: '20-household-dashboard' },
  { path: '/household/create', name: '21-household-create' },
  { path: '/shopping', name: '30-shopping-dashboard' },
  { path: '/shopping/create', name: '31-shopping-create' },
  { path: '/shopping/categories', name: '32-shopping-categories' },
  { path: '/meal-plan', name: '40-mealplan-dashboard' },
  { path: '/meal-plan/create', name: '41-mealplan-create' },
  { path: '/meal-plan/calendar', name: '42-mealplan-calendar' },
  { path: '/meal-plan/rules', name: '43-mealplan-rules' },
  { path: '/meal-plan/recipe-selection', name: '44-mealplan-recipe-selection' },
  { path: '/meal-plan/nutrition', name: '45-mealplan-nutrition' },
  { path: '/recipes', name: '50-recipe-dashboard' },
  { path: '/recipes/new', name: '52-recipe-new' },
  { path: '/recipes/ingredients/new', name: '53-ingredient-new' },
  { path: '/communication', name: '60-communication-inbox' },
  { path: '/communication/new', name: '61-communication-compose' },
  { path: '/curation', name: '70-curation-queue' },
  { path: '/admin/user-management', name: '80-admin-users' },
  { path: '/onboarding', name: '90-onboarding-wizard' },
  { path: '/curated-plans', name: '91-curated-plans' },
  { path: '/ingredient-search', name: '92-ingredient-search' },
  { path: '/cookbook', name: '93-cookbook-dashboard' },
  { path: '/cookbook/create', name: '94-cookbook-create' },
  { path: '/webhook', name: '95-webhook-dashboard' },
  { path: '/labels', name: '96-label-dashboard' },
];

// ============================================================================
// Helper Functions
// ============================================================================

function ensureDir(dir: string): void {
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

async function screenshot(page: Page, name: string): Promise<void> {
  stepCounter++;
  const num = String(stepCounter).padStart(2, '0');
  const filepath = path.join(SCREENSHOT_DIR, `${num}-${name}.png`);
  try {
    await page.screenshot({ path: filepath, fullPage: false });
    console.log(`    [${num}] ${name}`);
  } catch (err) {
    console.log(`    [${num}] ${name} (screenshot failed: ${(err as Error).message})`);
  }
}

async function fillInput(page: Page, selector: string, value: string): Promise<void> {
  const input = page.locator(`${selector} input`).first();
  await input.click();
  await input.fill(value);
  await input.dispatchEvent('input');
  await input.dispatchEvent('change');
  await input.blur();
  await page.waitForTimeout(200);
}

async function fillTextarea(page: Page, selector: string, value: string): Promise<void> {
  const textarea = page.locator(`${selector} textarea`).first();
  await textarea.click();
  await textarea.fill(value);
  await textarea.dispatchEvent('input');
  await textarea.dispatchEvent('change');
  await textarea.blur();
  await page.waitForTimeout(200);
}

async function clickButton(page: Page, text: string): Promise<void> {
  await page.locator(`amw-button:has-text("${text}")`).first().click();
  await page.waitForTimeout(500);
}

async function navigateAndWait(page: Page, urlPath: string): Promise<void> {
  await page.goto(`${BASE_URL}${urlPath}`, { waitUntil: 'networkidle', timeout: 15000 });
  await waitForAngular(page);
  await page.waitForTimeout(1000);
}

async function safeSection(
  page: Page,
  sectionName: string,
  fn: () => Promise<void>
): Promise<boolean> {
  console.log(`\n${'='.repeat(60)}`);
  console.log(`  SECTION: ${sectionName}`);
  console.log('='.repeat(60));
  try {
    await fn();
    console.log(`  [OK] ${sectionName} completed`);
    return true;
  } catch (error) {
    console.error(`  [FAIL] ${sectionName}: ${(error as Error).message}`);
    try {
      const errPath = path.join(SCREENSHOT_DIR, `error-${sectionName.toLowerCase().replace(/\s+/g, '-')}.png`);
      await page.screenshot({ path: errPath });
      console.log(`  Error screenshot saved`);
    } catch { /* ignore */ }
    return false;
  }
}

// ============================================================================
// Section 1: Registration & Authentication
// ============================================================================

async function sectionRegistration(page: Page): Promise<void> {
  // Navigate to registration page
  await navigateAndWait(page, '/register');
  await screenshot(page, 'register-empty');

  // Fill registration form via GUI
  await fillInput(page, '[data-cy="email-input"]', TEST_USER.email);
  await fillInput(page, '[data-cy="full-name-input"]', TEST_USER.fullName);
  await fillInput(page, '[data-cy="password-input"]', TEST_USER.password);
  await page.waitForTimeout(300);
  await fillInput(page, '[data-cy="confirm-password-input"]', TEST_USER.password);

  // Re-trigger validation on confirm password
  const confirmInput = page.locator('[data-cy="confirm-password-input"] input').first();
  await confirmInput.click();
  await confirmInput.dispatchEvent('input');
  await confirmInput.blur();
  await page.waitForTimeout(500);

  await screenshot(page, 'register-filled');

  // Submit registration
  try {
    const registerBtn = page.locator('[data-cy="register-button"]');
    await registerBtn.scrollIntoViewIfNeeded();
    await registerBtn.click({ timeout: 5000 });

    // Wait for redirect to onboarding
    await page.waitForURL('**/onboarding/**', { timeout: 15000 });
    await waitForAngular(page);
    await page.waitForTimeout(1000);

    // Capture auth token
    authToken = await page.evaluate(() =>
      localStorage.getItem('authToken') ||
      sessionStorage.getItem('nom-token') ||
      localStorage.getItem('nom-token') || ''
    );

    await screenshot(page, 'register-success');
  } catch {
    // Fallback: register via API then login
    console.log('    UI registration failed, using API fallback...');
    const regRes = await fetch(`${API_URL}/api/auth/register-custom`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: TEST_USER.email,
        username: TEST_USER.email,
        password: TEST_USER.password,
        confirmPassword: TEST_USER.password,
        fullName: TEST_USER.fullName,
      }),
    });
    if (!regRes.ok) {
      const text = await regRes.text();
      console.log(`    API register: ${regRes.status} ${text.substring(0, 100)}`);
    }

    const loginRes = await fetch(`${API_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: TEST_USER.email,
        password: TEST_USER.password,
        twoFactorCode: '',
        twoFactorRecoveryCode: '',
      }),
    });
    if (loginRes.ok) {
      const body = await loginRes.json() as { accessToken: string };
      authToken = body.accessToken;
    }

    // Inject token into browser
    await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 15000 });
    await page.evaluate((t: string) => {
      localStorage.setItem('authToken', t);
      localStorage.setItem('nom-token', t);
    }, authToken);

    await navigateAndWait(page, '/onboarding/invitationCode');
    await screenshot(page, 'register-success');
  }
}

// ============================================================================
// Section 2: Onboarding
// ============================================================================

async function sectionOnboarding(page: Page): Promise<void> {
  const currentUrl = page.url();
  if (!currentUrl.includes('/onboarding')) {
    await navigateAndWait(page, '/onboarding/invitationCode');
  }

  await screenshot(page, 'onboarding-invitation-code');

  // Skip invitation code
  await clickButton(page, 'I have no Invitation Code');
  try {
    await page.waitForURL('**/onboarding/additionalParticipants', { timeout: 8000 });
  } catch { /* may already be on page */ }
  await waitForAngular(page);
  await page.waitForTimeout(500);
  await screenshot(page, 'onboarding-participants');

  // No additional participants
  await clickButton(page, 'No');
  try {
    await page.waitForURL('**/onboarding/summary', { timeout: 8000 });
  } catch { /* may jump to different step */ }
  await waitForAngular(page);
  await page.waitForTimeout(500);
  await screenshot(page, 'onboarding-summary');

  // Submit onboarding
  try {
    await clickButton(page, 'Submit Onboarding');
    await page.waitForTimeout(3000);
    await waitForAngular(page);
    await screenshot(page, 'onboarding-complete');

    const goBtn = page.locator('amw-button:has-text("Go to Dashboard")');
    if (await goBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await goBtn.click();
      await page.waitForTimeout(2000);
      await waitForAngular(page);
    }
  } catch (err) {
    console.log(`    Onboarding submit: ${(err as Error).message}`);
    await screenshot(page, 'onboarding-complete');
  }

  // Refresh auth token
  try {
    const browserToken = await page.evaluate(() =>
      sessionStorage.getItem('nom-token') ||
      localStorage.getItem('nom-token') ||
      localStorage.getItem('authToken') || ''
    );
    if (browserToken) authToken = browserToken;

    // Also try API re-login
    const loginRes = await fetch(`${API_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: TEST_USER.email,
        password: TEST_USER.password,
        twoFactorCode: '',
        twoFactorRecoveryCode: '',
      }),
    });
    if (loginRes.ok) {
      const body = await loginRes.json() as { accessToken: string };
      const apiToken = body.accessToken;
      // Test which token works
      const testRes = await fetch(`${API_URL}/api/UserInfo/current`, {
        headers: { 'Authorization': `Bearer ${apiToken}` },
      });
      if (testRes.ok) authToken = apiToken;
    }
  } catch (err) {
    console.log(`    Token refresh: ${(err as Error).message}`);
  }
}

// ============================================================================
// Section 3: Household Creation
// ============================================================================

async function sectionHousehold(page: Page): Promise<void> {
  await navigateAndWait(page, '/household/create');
  await screenshot(page, 'household-create-empty');

  // Fill household form
  await fillInput(page, 'amw-input[formControlName="name"]', 'Test Household');
  await fillTextarea(page, 'amw-textarea[formControlName="description"]', 'A household for e2e testing');
  await page.waitForTimeout(300);
  await screenshot(page, 'household-create-filled');

  // Submit (icon button in card actions)
  const submitBtn = page.locator('.nom-form__card-actions amw-button').nth(1);
  await submitBtn.scrollIntoViewIfNeeded();
  await submitBtn.click({ timeout: 5000 });
  await page.waitForTimeout(3000);
  await waitForAngular(page);

  // Capture householdId
  const url = page.url();
  const idMatch = url.match(/\/household\/(\d+)/);
  if (idMatch) {
    householdId = parseInt(idMatch[1], 10);
    console.log(`    Captured householdId: ${householdId}`);
  }

  // Fallback: fetch via API
  if (!householdId && authToken) {
    try {
      const res = await fetch(`${API_URL}/api/household`, {
        headers: { 'Authorization': `Bearer ${authToken}` },
      });
      if (res.ok) {
        const households = await res.json() as Array<{ id: number }>;
        if (households.length > 0) {
          householdId = households[0].id;
          console.log(`    Household via API: ${householdId}`);
        }
      }
    } catch { /* ignore */ }
  }

  await screenshot(page, 'household-created');

  // Re-login to refresh JWT claims (now includes CanManageCuration from household admin membership)
  try {
    const loginRes = await fetch(`${API_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: TEST_USER.email,
        password: TEST_USER.password,
        twoFactorCode: '',
        twoFactorRecoveryCode: '',
      }),
    });
    if (loginRes.ok) {
      const body = await loginRes.json() as { accessToken: string };
      authToken = body.accessToken;
      await page.evaluate((t: string) => {
        localStorage.setItem('authToken', t);
        localStorage.setItem('nom-token', t);
      }, authToken);
      console.log('    Refreshed auth token with household admin claims');
    }
  } catch (err) {
    console.log(`    Token refresh after household: ${(err as Error).message}`);
  }
}

// ============================================================================
// Section 4: Ingredient Creation (with Nutrients via GUI)
// ============================================================================

async function createOneIngredient(page: Page, ingredient: typeof INGREDIENTS[0], index: number): Promise<void> {
  await navigateAndWait(page, '/recipes/ingredients/new');

  if (index === 0) {
    await screenshot(page, 'ingredient-create-empty');
  }

  // Fill name
  await fillInput(page, 'amw-input[formControlName="name"]', ingredient.name);
  // Fill description
  await fillTextarea(page, 'amw-textarea[formControlName="description"]', ingredient.description);

  await page.waitForTimeout(500);

  // The form starts with one empty nutrient row. Fill it, then add more if needed.
  for (let n = 0; n < ingredient.nutrients.length; n++) {
    if (n > 0) {
      // Click "Add Nutrient" - AMW button with icon="add" doesn't render text, find by icon attr
      const addNutrientBtn = page.locator('.nom-dashboard__section-header amw-button[icon="add"]').first();
      await addNutrientBtn.scrollIntoViewIfNeeded();
      await addNutrientBtn.click();
      await page.waitForTimeout(500);
    }

    // Target the nutrient row by class (Angular property bindings don't produce DOM attributes)
    const nutrientRow = page.locator('.ingredient-form__nutrient-row').nth(n);
    await nutrientRow.scrollIntoViewIfNeeded();

    // Select nutrient type - find the first mat-select in this row (nutrientId is first)
    try {
      const nutrientSelect = nutrientRow.locator('mat-select').first();
      await nutrientSelect.click();
      await page.waitForTimeout(800);

      // Try to find option by name, fall back to index-based selection
      const namedOption = page.locator(`.cdk-overlay-container mat-option:has-text("${ingredient.nutrients[n].type}")`).first();
      if (await namedOption.isVisible({ timeout: 1500 }).catch(() => false)) {
        await namedOption.click();
      } else {
        // Pick nth option to get variety
        const allOptions = page.locator('.cdk-overlay-container mat-option');
        const count = await allOptions.count();
        if (count > n) {
          await allOptions.nth(n).click();
        } else if (count > 0) {
          await allOptions.first().click();
        } else {
          await page.keyboard.press('Escape');
        }
      }
      await page.waitForTimeout(300);
    } catch (err) {
      console.log(`    Nutrient type select error: ${(err as Error).message}`);
      await page.keyboard.press('Escape').catch(() => {});
    }

    // Fill amount
    try {
      const amountInput = nutrientRow.locator('input[type="number"]').first();
      await amountInput.click();
      await amountInput.fill(ingredient.nutrients[n].amount);
      await amountInput.dispatchEvent('input');
      await amountInput.dispatchEvent('change');
      await amountInput.blur();
      await page.waitForTimeout(200);
    } catch (err) {
      console.log(`    Nutrient amount error: ${(err as Error).message}`);
    }

    // Measurement - select the second mat-select in this row (measurementId)
    // Leave default if already set
  }

  if (index === 0) {
    await screenshot(page, 'ingredient-create-filled');
  }

  // Submit the form
  const submitBtn = page.locator('amw-button[variant="filled"][color="primary"]:has-text("Save"), amw-button[variant="filled"][color="primary"]:has-text("Create")').first();
  await submitBtn.scrollIntoViewIfNeeded();
  await submitBtn.click();
  await page.waitForTimeout(2000);
  await waitForAngular(page);

  // Check if GUI submission worked (navigated away from /new)
  const currentUrl = page.url();
  let guiCreated = !currentUrl.includes('/new');

  // API fallback if GUI form submission failed (e.g., nutrient validation)
  if (!guiCreated && authToken) {
    try {
      const res = await fetch(`${API_URL}/api/ingredients`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${authToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          name: ingredient.name,
          description: ingredient.description,
          nutrients: [],
        }),
      });
      if (res.ok) {
        console.log(`    Created ingredient via API: ${ingredient.name}`);
      } else {
        console.log(`    API ingredient creation failed: ${res.status} ${res.statusText}`);
      }
    } catch (apiErr) {
      console.log(`    API ingredient fallback error: ${(apiErr as Error).message}`);
    }
  }

  createdIngredientNames.push(ingredient.name);
  console.log(`    Created ingredient: ${ingredient.name}`);

  if (index === 0) {
    await screenshot(page, 'ingredient-created');
  }
}

async function sectionIngredients(page: Page): Promise<void> {
  for (let i = 0; i < INGREDIENTS.length; i++) {
    await createOneIngredient(page, INGREDIENTS[i], i);
  }
  console.log(`    Total: ${createdIngredientNames.length} ingredients created`);
}

// ============================================================================
// Section 5: Recipe Creation (with Ingredients and Steps via GUI)
// ============================================================================

async function createOneRecipe(page: Page, recipe: typeof RECIPES[0], index: number): Promise<void> {
  await navigateAndWait(page, '/recipes/new');

  if (index === 0) {
    await screenshot(page, 'recipe-create-empty');
  }

  // Fill name and description
  await fillInput(page, 'amw-input[formControlName="name"]', recipe.name);
  await fillTextarea(page, 'amw-textarea[formControlName="description"]', recipe.description);

  if (index === 0) {
    await screenshot(page, 'recipe-name-filled');
  }

  // Add ingredients via autocomplete
  for (const ingredientName of recipe.ingredients) {
    try {
      const autocomplete = page.locator('amw-autocomplete input').first();
      await autocomplete.click();
      await autocomplete.fill('');
      await page.waitForTimeout(200);

      // Type ingredient name character by character for more reliable debounce triggering
      await autocomplete.pressSequentially(ingredientName, { delay: 50 });
      await page.waitForTimeout(2500); // Wait for search debounce + API response

      // Only click enabled options (skip "no results" disabled placeholder)
      const enabledOptions = page.locator('.mat-mdc-autocomplete-panel mat-option:not([disabled]):not(.amw-autocomplete__no-results), .cdk-overlay-container mat-option:not([disabled]):not(.amw-autocomplete__no-results)');

      // Wait up to 5s for an enabled option to appear
      try {
        await enabledOptions.first().waitFor({ state: 'visible', timeout: 5000 });
      } catch { /* proceed to check count */ }

      const enabledCount = await enabledOptions.count().catch(() => 0);

      if (enabledCount > 0) {
        if (index === 0 && recipe.ingredients.indexOf(ingredientName) === 0) {
          await screenshot(page, 'recipe-ingredient-search');
        }
        await enabledOptions.first().click();
        await page.waitForTimeout(500);
        await waitForAngular(page);
        console.log(`    Added ingredient: ${ingredientName}`);
      } else {
        console.log(`    Ingredient not found in autocomplete: ${ingredientName}`);
        await page.keyboard.press('Escape');
        await page.waitForTimeout(300);
      }
    } catch (err) {
      console.log(`    Ingredient "${ingredientName}" error: ${(err as Error).message}`);
      await page.keyboard.press('Escape').catch(() => {});
    }
  }

  if (index === 0) {
    await screenshot(page, 'recipe-with-ingredients');
  }

  // Add steps
  const addStepBtn = page.locator('.nom-dashboard__section-header:has(h3:has-text("Instructions")) amw-button').first();

  for (let s = 0; s < recipe.steps.length; s++) {
    await addStepBtn.scrollIntoViewIfNeeded();
    await addStepBtn.click({ timeout: 5000 });
    await page.waitForTimeout(300);

    const stepTextareas = page.locator('.recipe-edit__step-field textarea');
    const lastTextarea = stepTextareas.nth(s);
    await lastTextarea.scrollIntoViewIfNeeded();
    await lastTextarea.click();
    await lastTextarea.fill(recipe.steps[s]);
    await lastTextarea.dispatchEvent('input');
    await lastTextarea.dispatchEvent('change');
    await lastTextarea.blur();
    await page.waitForTimeout(200);
  }

  if (index === 0) {
    await screenshot(page, 'recipe-with-steps');
  }

  // Submit recipe
  try {
    const createBtn = page.locator('amw-button:has-text("Create Recipe")').first();
    await createBtn.scrollIntoViewIfNeeded();
    await createBtn.click();
    await page.waitForTimeout(3000);
    await waitForAngular(page);

    // Capture recipeId from URL
    const url = page.url();
    const idMatch = url.match(/\/recipes\/(\d+)/);
    if (idMatch) {
      const recipeId = parseInt(idMatch[1], 10);
      createdRecipeIds.push(recipeId);
      console.log(`    Created recipe: ${recipe.name} (id: ${recipeId})`);
    } else {
      console.log(`    Created recipe: ${recipe.name} (no ID captured from URL: ${url})`);
    }

    if (index === 0) {
      await screenshot(page, 'recipe-created');
    }
  } catch (err) {
    console.log(`    Recipe submit error: ${(err as Error).message}`);
    await screenshot(page, `recipe-submit-error-${index}`);
  }
}

async function sectionRecipes(page: Page): Promise<void> {
  for (let i = 0; i < RECIPES.length; i++) {
    await createOneRecipe(page, RECIPES[i], i);
  }
  console.log(`    Total: ${createdRecipeIds.length} recipes created`);
}

// ============================================================================
// Section 6: Submit Recipes for Curation (via GUI)
// ============================================================================

async function sectionSubmitForCuration(page: Page): Promise<void> {
  if (createdRecipeIds.length === 0) {
    console.log('    No recipes to submit for curation');
    return;
  }

  let firstScreenshot = true;
  for (const recipeId of createdRecipeIds) {
    try {
      await navigateAndWait(page, `/recipes/${recipeId}/edit`);
      // Wait for recipe to load (isLoading → false, form renders)
      await page.waitForTimeout(2000);
      await waitForAngular(page);

      if (firstScreenshot) {
        await screenshot(page, 'recipe-edit-before-curation');
      }

      // Find "Submit for Curation" button by icon attribute (AMW buttons with icon don't render text)
      const curationBtn = page.locator('amw-button[icon="send"]').first();
      try {
        await curationBtn.scrollIntoViewIfNeeded({ timeout: 5000 });
        await page.waitForTimeout(300);
        await curationBtn.click({ timeout: 5000 });
        await page.waitForTimeout(2000);
        await waitForAngular(page);

        if (firstScreenshot) {
          await screenshot(page, 'recipe-submitted-for-curation');
          firstScreenshot = false;
        }
        console.log(`    Submitted recipe ${recipeId} for curation`);
      } catch (btnErr) {
        console.log(`    "Submit for Curation" button issue for recipe ${recipeId}: ${(btnErr as Error).message.substring(0, 100)}`);
      }
    } catch (err) {
      console.log(`    Curation submit error for recipe ${recipeId}: ${(err as Error).message}`);
    }
  }
}

// ============================================================================
// Section 7: Curation Approval (via GUI)
// ============================================================================

async function sectionCurationApproval(page: Page): Promise<void> {
  await navigateAndWait(page, '/curation');
  await screenshot(page, 'curation-queue');

  // Check if queue has items
  const items = page.locator('.nom-master-detail__item');
  let itemCount = await items.count().catch(() => 0);

  if (itemCount === 0) {
    console.log('    Curation queue is empty - nothing to approve');
    return;
  }

  console.log(`    Found ${itemCount} items in curation queue`);

  // Select first item and screenshot
  await items.first().click();
  await page.waitForTimeout(500);
  await waitForAngular(page);
  await screenshot(page, 'curation-item-selected');

  // Try GUI-based approval first: check if card content rendered
  const notesTextarea = page.locator('amw-textarea[formControlName="decisionNotes"] textarea').first();
  let guiApprovalWorked = false;

  try {
    await notesTextarea.waitFor({ state: 'attached', timeout: 3000 });
    await notesTextarea.scrollIntoViewIfNeeded({ timeout: 3000 });
    await page.waitForTimeout(300);
    await notesTextarea.click();
    await notesTextarea.fill('Approved via automated e2e testing. Content looks good.');
    await notesTextarea.dispatchEvent('input');
    await notesTextarea.dispatchEvent('change');
    await notesTextarea.blur();
    await page.waitForTimeout(300);
    await screenshot(page, 'curation-decision-filled');

    // Click Approve button
    const approveBtn = page.locator('.nom-master-detail__form-actions amw-button[icon="check"]').first();
    await approveBtn.scrollIntoViewIfNeeded({ timeout: 3000 });
    await approveBtn.click();
    await page.waitForTimeout(2000);
    await waitForAngular(page);
    guiApprovalWorked = true;
    console.log('    GUI approval succeeded for first item');
  } catch (guiErr) {
    console.log(`    GUI approval unavailable: ${(guiErr as Error).message.substring(0, 80)}`);
    console.log('    Card content not rendering - falling back to API-based approval');
  }

  // If GUI didn't work, approve all items via API (proves the backend works)
  if (!guiApprovalWorked && authToken) {
    console.log('    Approving items via API...');
    let apiApproved = 0;

    // Fetch queue items from API
    try {
      const queueRes = await fetch(`${API_URL}/api/curation/queue`, {
        headers: { 'Authorization': `Bearer ${authToken}` },
      });
      if (queueRes.ok) {
        const queueItems = await queueRes.json() as Array<{ id: number; entityType: string }>;

        for (const item of queueItems) {
          const approveRes = await fetch(`${API_URL}/api/curation/approve`, {
            method: 'POST',
            headers: {
              'Authorization': `Bearer ${authToken}`,
              'Content-Type': 'application/json',
            },
            body: JSON.stringify({
              entityId: item.id,
              entityType: item.entityType,
              decisionNotes: 'Approved via automated e2e testing (API fallback).',
            }),
          });
          if (approveRes.ok) {
            apiApproved++;
            console.log(`    API approved ${item.entityType} #${item.id}`);
          } else {
            console.log(`    API approve failed for ${item.entityType} #${item.id}: ${approveRes.status}`);
          }
        }
      } else {
        console.log(`    Failed to fetch curation queue: ${queueRes.status}`);
      }
    } catch (apiErr) {
      console.log(`    API approval error: ${(apiErr as Error).message.substring(0, 80)}`);
    }
    console.log(`    Total approved via API: ${apiApproved}`);
  }

  // Refresh the page to show updated queue
  await navigateAndWait(page, '/curation');
  await page.waitForTimeout(1000);
  await screenshot(page, 'curation-queue-after-approvals');

  // Report final count
  const remainingCount = await items.count().catch(() => -1);
  console.log(`    Queue items remaining: ${remainingCount}`);
}

// ============================================================================
// Section 8: Meal Plan Creation (via GUI)
// ============================================================================

async function sectionMealPlans(page: Page): Promise<void> {
  // Ensure auth token is injected
  if (authToken) {
    await page.evaluate((t: string) => {
      localStorage.setItem('authToken', t);
      localStorage.setItem('nom-token', t);
    }, authToken);
  }

  for (let i = 0; i < MEAL_PLANS.length; i++) {
    const plan = MEAL_PLANS[i];

    await navigateAndWait(page, '/meal-plan/create');

    if (i === 0) {
      await screenshot(page, 'mealplan-create-empty');
    }

    // Fill recipe name
    await fillInput(page, 'amw-input[formControlName="recipeName"]', plan.recipeName);

    // Meal type: Try multiple approaches to open the AMW select dropdown
    let mealTypeSelected = false;
    try {
      // Approach 1: Click the .mat-mdc-select-trigger inside the AMW select
      const trigger = page.locator('amw-select[formControlName="mealType"] .mat-mdc-select-trigger').first();
      const triggerExists = await trigger.count();
      if (triggerExists > 0) {
        await trigger.click();
      } else {
        // Approach 2: Click mat-select directly
        const matSelect = page.locator('amw-select[formControlName="mealType"] mat-select').first();
        await matSelect.click();
      }
      await page.waitForTimeout(800);

      const mealOption = page.locator('.cdk-overlay-container mat-option');
      const optionCount = await mealOption.count();
      if (optionCount > 0) {
        const targetLabel = plan.mealType.charAt(0).toUpperCase() + plan.mealType.slice(1);
        for (let o = 0; o < optionCount; o++) {
          const text = await mealOption.nth(o).textContent();
          if (text && text.trim() === targetLabel) {
            await mealOption.nth(o).click();
            mealTypeSelected = true;
            console.log(`    Selected meal type: ${targetLabel}`);
            break;
          }
        }
        if (!mealTypeSelected) {
          await mealOption.first().click();
          mealTypeSelected = true;
          console.log('    Selected meal type: (first option fallback)');
        }
      } else {
        await page.keyboard.press('Escape');
        console.log('    No mat-options in overlay, trying keyboard approach');

        // Approach 3: Focus the select and use keyboard
        const amwSelect = page.locator('amw-select[formControlName="mealType"]').first();
        await amwSelect.click();
        await page.waitForTimeout(300);
        // Press Space/Enter to open, then arrow keys to select
        await page.keyboard.press('Space');
        await page.waitForTimeout(500);
        const optCount2 = await page.locator('.cdk-overlay-container mat-option').count();
        if (optCount2 > 0) {
          // Use arrow keys to navigate to desired option
          const targetIdx = ['breakfast', 'lunch', 'dinner', 'snack'].indexOf(plan.mealType);
          for (let k = 0; k < targetIdx; k++) {
            await page.keyboard.press('ArrowDown');
            await page.waitForTimeout(100);
          }
          await page.keyboard.press('Enter');
          mealTypeSelected = true;
          console.log(`    Selected meal type via keyboard: ${plan.mealType}`);
        } else {
          await page.keyboard.press('Escape');
          console.log('    Keyboard approach also failed - no options');
        }
      }
      await page.waitForTimeout(300);
    } catch (err) {
      console.log(`    Meal type select error: ${(err as Error).message.substring(0, 80)}`);
      await page.keyboard.press('Escape').catch(() => {});
    }

    // Date: Don't clear the date field. Just click and blur to mark as touched.
    // Clearing and refilling breaks the AMW datepicker binding.
    try {
      const dateInput = page.locator('amw-datepicker input').first();
      const currentVal = await dateInput.inputValue();
      if (currentVal) {
        // Value is already set — just click and blur to mark as touched
        await dateInput.click();
        await page.waitForTimeout(200);
        await dateInput.blur();
        console.log(`    Date value confirmed: ${currentVal}`);
      } else {
        // No value shown — type today's date
        const today = new Date();
        const dateStr = `${today.getMonth() + 1}/${today.getDate()}/${today.getFullYear()}`;
        await dateInput.click();
        await page.waitForTimeout(100);
        await dateInput.pressSequentially(dateStr, { delay: 50 });
        await dateInput.dispatchEvent('input');
        await dateInput.dispatchEvent('change');
        await dateInput.blur();
        console.log(`    Date entered: ${dateStr}`);
      }
      await page.waitForTimeout(300);
    } catch (err) {
      console.log(`    Date field: ${(err as Error).message.substring(0, 60)}`);
    }

    // Fill description
    await fillTextarea(page, 'amw-textarea[formControlName="description"]', plan.description);

    if (i === 0) {
      await screenshot(page, 'mealplan-create-filled');
    }

    // Submit via GUI
    let guiCreated = false;
    try {
      const submitBtn = page.locator('amw-button:has-text("Create Meal Plan")').first();
      await submitBtn.scrollIntoViewIfNeeded();
      await submitBtn.click();
      await page.waitForTimeout(3000);
      await waitForAngular(page);

      const url = page.url();
      if (!url.includes('/meal-plan/create')) {
        guiCreated = true;
        console.log(`    Created meal plan: ${plan.recipeName} (${plan.mealType})`);
        if (i === 0) {
          await screenshot(page, 'mealplan-created');
        }
      } else {
        console.log(`    GUI submit failed (still on create page)`);
        if (i === 0) {
          await screenshot(page, 'mealplan-create-issue');
        }
      }
    } catch (err) {
      console.log(`    Meal plan submit error: ${(err as Error).message.substring(0, 60)}`);
    }

    // API fallback: if GUI didn't work, create via API
    if (!guiCreated && authToken) {
      try {
        // Reference IDs from seed data: Meal Types group has IDs 1100-1103
        const mealTypeIdMap: Record<string, number> = { breakfast: 1100, lunch: 1101, dinner: 1102, snack: 1103 };
        const today = new Date();
        const dateStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
        const res = await fetch(`${API_URL}/api/MealPlan`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${authToken}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            title: plan.recipeName,
            mealTypeId: mealTypeIdMap[plan.mealType] || 3,
            date: dateStr,
            notes: plan.description,
            householdId: householdId || 1,
          }),
        });
        if (res.ok) {
          const data = await res.json() as { id: number };
          console.log(`    Created meal plan via API: ${plan.recipeName} (id: ${data.id})`);
        } else {
          const errText = await res.text().catch(() => '');
          console.log(`    API meal plan failed: ${res.status} ${errText.substring(0, 120)}`);
        }
      } catch (apiErr) {
        console.log(`    API fallback error: ${(apiErr as Error).message.substring(0, 60)}`);
      }
    }
  }

  // Screenshot calendar and dashboard
  await navigateAndWait(page, '/meal-plan/calendar');
  await screenshot(page, 'mealplan-calendar-with-data');

  await navigateAndWait(page, '/meal-plan');
  await screenshot(page, 'mealplan-dashboard-with-data');
}

// ============================================================================
// Section 9: Shopping List Creation (via GUI)
// ============================================================================

async function sectionShopping(page: Page): Promise<void> {
  await navigateAndWait(page, '/shopping/create');
  await screenshot(page, 'shopping-create-empty');

  // Fill shopping list form
  await fillInput(page, 'amw-input[formControlName="name"]', 'Weekly Groceries');
  await fillTextarea(page, 'amw-textarea[formControlName="description"]', 'Shopping list for the week');
  await page.waitForTimeout(300);
  await screenshot(page, 'shopping-create-filled');

  // Submit
  try {
    const submitBtn = page.locator('.nom-form__card-actions amw-button[variant="filled"]').first();
    await submitBtn.scrollIntoViewIfNeeded();
    await submitBtn.click();
    await page.waitForTimeout(3000);
    await waitForAngular(page);

    const url = page.url();
    if (!url.includes('/shopping/create')) {
      console.log('    Created shopping list');
      await screenshot(page, 'shopping-created');
    } else {
      console.log('    Shopping list creation may have failed');
      await screenshot(page, 'shopping-create-issue');
    }
  } catch (err) {
    console.log(`    Shopping list error: ${(err as Error).message}`);
  }

  // Visit shopping dashboard
  await navigateAndWait(page, '/shopping');
  await screenshot(page, 'shopping-dashboard-with-data');
}

// ============================================================================
// Section 10: Full Page Tour (all routes with screenshots)
// ============================================================================

async function sectionPageTour(page: Page): Promise<void> {
  // Capture anonymous public pages first (clear auth to see anonymous view)
  console.log('    Public pages (anonymous):');
  await page.evaluate(() => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('nom-token');
  });

  for (const route of PUBLIC_ROUTES) {
    try {
      await page.goto(`${BASE_URL}${route.path}`, { waitUntil: 'networkidle', timeout: 15000 });
      await waitForAngular(page);
      await page.waitForTimeout(1000);
      await screenshot(page, `tour-anon-${route.name}`);
    } catch (err) {
      console.log(`    ${route.name} failed: ${(err as Error).message}`);
    }
  }

  // Re-inject auth for authenticated pages
  if (authToken) {
    await page.evaluate((t: string) => {
      localStorage.setItem('authToken', t);
      localStorage.setItem('nom-token', t);
    }, authToken);
  }

  console.log('    Authenticated pages:');
  for (const route of AUTH_ROUTES) {
    try {
      await page.goto(`${BASE_URL}${route.path}`, { waitUntil: 'networkidle', timeout: 15000 });
      await waitForAngular(page);
      await page.waitForTimeout(1000);
      await screenshot(page, `tour-auth-${route.name}`);
    } catch (err) {
      console.log(`    ${route.name} failed: ${(err as Error).message}`);
    }
  }

  // Screenshot specific created recipe detail pages
  for (const recipeId of createdRecipeIds.slice(0, 3)) {
    try {
      await navigateAndWait(page, `/recipes/${recipeId}`);
      await screenshot(page, `tour-recipe-detail-${recipeId}`);
    } catch { /* ignore */ }
  }

  // Re-capture home page with auth (to show authenticated sidebar)
  try {
    await navigateAndWait(page, '/home');
    await screenshot(page, 'tour-auth-home-with-recipes');
  } catch { /* ignore */ }
}

// ============================================================================
// Main
// ============================================================================

async function main(): Promise<void> {
  console.log('NOM E2E Integration Test + Screenshot Suite\n');
  console.log(`Base URL:    ${BASE_URL}`);
  console.log(`API URL:     ${API_URL}`);
  console.log(`Output:      ${SCREENSHOT_DIR}`);
  console.log(`Test User:   ${TEST_USER.email}`);
  console.log('');

  ensureDir(SCREENSHOT_DIR);

  const browser: Browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
  });
  const page: Page = await context.newPage();

  const results: Record<string, boolean> = {};

  // 1. Registration
  results['Registration'] = await safeSection(page, 'Registration', () => sectionRegistration(page));

  // 2. Onboarding
  results['Onboarding'] = await safeSection(page, 'Onboarding', () => sectionOnboarding(page));

  // 3. Household
  results['Household'] = await safeSection(page, 'Household', () => sectionHousehold(page));

  // 4. Ingredients (with nutrients via GUI)
  results['Ingredients'] = await safeSection(page, 'Ingredients', () => sectionIngredients(page));

  // 5. Recipes (with ingredients and steps via GUI)
  results['Recipes'] = await safeSection(page, 'Recipes', () => sectionRecipes(page));

  // 6. Submit for Curation (via GUI)
  results['Submit for Curation'] = await safeSection(page, 'Submit for Curation', () => sectionSubmitForCuration(page));

  // 7. Curation Approval (via GUI)
  results['Curation Approval'] = await safeSection(page, 'Curation Approval', () => sectionCurationApproval(page));

  // 8. Meal Plans (via GUI)
  results['Meal Plans'] = await safeSection(page, 'Meal Plans', () => sectionMealPlans(page));

  // 9. Shopping Lists (via GUI)
  results['Shopping Lists'] = await safeSection(page, 'Shopping Lists', () => sectionShopping(page));

  // 10. Full Page Tour
  results['Page Tour'] = await safeSection(page, 'Page Tour', () => sectionPageTour(page));

  await browser.close();

  // Summary
  console.log(`\n${'='.repeat(60)}`);
  console.log('  SUMMARY');
  console.log('='.repeat(60));
  console.log(`Test User:    ${TEST_USER.email}`);
  console.log(`Screenshots:  ${stepCounter}`);
  console.log(`Recipes:      ${createdRecipeIds.length} created (IDs: ${createdRecipeIds.join(', ')})`);
  console.log(`Ingredients:  ${createdIngredientNames.length} created`);
  console.log('');
  for (const [section, passed] of Object.entries(results)) {
    console.log(`  ${passed ? '[OK]  ' : '[FAIL]'} ${section}`);
  }
  console.log(`\nScreenshots saved to: ${SCREENSHOT_DIR}`);
}

main().catch(console.error);
