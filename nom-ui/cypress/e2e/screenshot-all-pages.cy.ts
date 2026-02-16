// Screenshot all pages for visual styling audit
// This test navigates to each screen and captures screenshots for analysis

describe('Screenshot All Pages', () => {
  // Public routes (no auth required)
  const publicRoutes = [
    { path: '/home', name: '01-home' },
    { path: '/register', name: '02-auth-register' },
    { path: '/forgot-password', name: '03-auth-forgot-password' },
    { path: '/reset-password', name: '04-auth-reset-password' },
    { path: '/confirm-email', name: '05-auth-confirm-email' },
    { path: '/send-confirmation', name: '06-auth-send-confirmation' },
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

    // Cookbook
    { path: '/cookbook', name: '93-cookbook-dashboard' },
    { path: '/cookbook/create', name: '94-cookbook-create' },

    // Webhook
    { path: '/webhook', name: '95-webhook-dashboard' },

    // Labels
    { path: '/labels', name: '96-label-dashboard' },
  ];

  describe('Public Pages', () => {
    publicRoutes.forEach(({ path, name }) => {
      it(`screenshots ${name}`, () => {
        cy.visit(path, { failOnStatusCode: false });
        cy.wait(1000); // Wait for page load and animations
        cy.screenshotPage(name);
      });
    });
  });

  describe('Authenticated Pages (may show login redirect)', () => {
    authRoutes.forEach(({ path, name }) => {
      it(`screenshots ${name}`, () => {
        cy.visit(path, { failOnStatusCode: false });
        cy.wait(1000); // Wait for page load and animations
        cy.screenshotPage(name);
      });
    });
  });
});
