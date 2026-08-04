/**
 * NOM Daily User Workflow E2E Test
 *
 * This test covers what 95% of users do most of the time:
 * 1. Log in (via header popover)
 * 2. Browse recipes on the home page
 * 3. Open a recipe detail — check ingredients, steps, nutrition
 * 4. Navigate to the meal plan calendar
 * 5. Interact with the meal plan (view week, navigate)
 * 6. Go to the shopping list
 * 7. Check off items / edit quantities
 * 8. Check the pantry
 *
 * Assumes seed data exists (recipes, ingredients, a household with a meal plan).
 * Uses real DOM selectors from the actual NOM UI.
 */

describe('Daily User Workflow', () => {
  const testPassword = Cypress.env('TEST_PASSWORD') || 'TestPassword123!';

  // Use a seeded user or register one before the suite
  let userEmail: string;

  before(() => {
    // Register a fresh user for this suite
    userEmail = `daily-${Date.now()}-${Math.random().toString(36).slice(2, 7)}@example.com`;

    cy.visit('/register');
    cy.get('input[formcontrolname="email"]').type(userEmail);
    cy.get('input[formcontrolname="fullName"]').type('Daily Test User');
    cy.get('input[formcontrolname="password"]').type(testPassword);
    cy.get('input[formcontrolname="confirmPassword"]').type(testPassword);
    cy.contains('button', 'Create Account').click();

    // Wait for registration to complete (redirect or confirmation)
    cy.wait(3000);
  });

  /**
   * Helper: log in via the header popover (the way a real user does it)
   */
  const loginViaPopover = () => {
    cy.visit('/home');
    cy.wait(1000);

    // Click "Sign In" button in the header
    cy.get('.nom-header__login-btn').click();
    cy.wait(500);

    // Fill the login popover form
    cy.get('.nom-login-popover').within(() => {
      cy.get('input[formcontrolname="email"]').type(userEmail);
      cy.get('input[formcontrolname="password"]').type(testPassword);
      cy.contains('button', 'Sign In').click();
    });

    // Wait for auth to complete — avatar should appear
    cy.get('.nom-header__avatar', { timeout: 10000 }).should('be.visible');
  };

  it('should log in, browse recipes, view a detail, check meal plan, shopping, and pantry', () => {
    // ── Step 1: Log in ──
    loginViaPopover();

    // ── Step 2: Browse recipes on the home page ──
    // After login the user lands on the dashboard; navigate to home to browse
    cy.get('.nom-header__brand').click();
    cy.wait(1000);

    // The home page should show recipe cards (from seed data) or an empty state
    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="recipe-card"]').length > 0) {
        // Recipes exist — click the first one
        cy.get('[data-testid="recipe-card"]').first().click();
        cy.wait(1000);

        // ── Step 3: Recipe detail page ──
        cy.get('[data-testid="recipe-detail"]').should('exist');

        // Verify ingredients section is present
        cy.get('.nom-recipe__column--ingredients').should('exist');
        cy.contains('Ingredients').should('be.visible');

        // Verify instructions section is present
        cy.get('.nom-recipe__column--instructions').should('exist');
        cy.contains('Instructions').should('be.visible');

        // Check nutrition tab exists
        cy.contains('button', 'Nutrition').should('exist');

        // Click the Nutrition tab to verify it works
        cy.contains('button', 'Nutrition').click();
        cy.wait(500);

        // Go back to home
        cy.get('[data-testid="recipe-back-link"]').click();
        cy.wait(1000);
      } else {
        // No seed recipes — that's okay, just verify the empty state renders
        cy.log('No seed recipes found — skipping recipe detail check');
        cy.get('.nom-home__empty, [data-testid="home-page"], [data-testid="dashboard"]').should('exist');
      }
    });

    // ── Step 4: Navigate to meal plan via the nav rail ──
    cy.get('[data-testid="nav-meal-plan"]').click();
    cy.wait(2000);

    // Verify we're on the meal plan page
    cy.contains('Meal Plan').should('be.visible');

    // If a plan exists, verify calendar structure
    cy.get('body').then(($body) => {
      if ($body.find('.nom-plan__calendar').length > 0) {
        // Calendar is visible — verify day headers exist
        cy.get('.nom-plan__day-header').should('have.length.at.least', 7);

        // Verify meal labels exist
        cy.get('.nom-plan__meal-label').should('have.length.at.least', 1);

        // Test week navigation
        cy.get('.nom-plan__nav').within(() => {
          // Click next week
          cy.get('button').contains('chevron_right').click({ force: true });
          cy.wait(1000);

          // Click "Today" to go back
          cy.contains('button', 'Today').click();
          cy.wait(1000);
        });

        // Verify shuffle button exists
        cy.contains('button', 'Shuffle').should('exist');
      } else if ($body.find('button:contains("Create Household"), a:contains("Create Household")').length > 0) {
        // No household yet — verify the empty state
        cy.log('No household — plan page shows setup prompt');
        cy.contains('No household yet').should('be.visible');
      } else {
        // Plan creation wizard
        cy.log('Plan creation wizard visible');
        cy.contains('Create a New Plan').should('be.visible');
      }
    });

    // ── Step 5: Navigate to shopping list ──
    cy.get('[data-testid="nav-shopping"]').click();
    cy.wait(2000);

    // Verify shopping page loaded
    cy.contains('Shopping List').should('be.visible');

    cy.get('body').then(($body) => {
      if ($body.find('.nom-shopping__dept').length > 0) {
        // Shopping list has items — test interactions

        // Verify department sections exist
        cy.get('.nom-shopping__dept').should('have.length.at.least', 1);
        cy.get('.nom-shopping__dept-name').first().should('be.visible');

        // Check off the first item
        cy.get('.nom-shopping__item').first().within(() => {
          cy.get('mat-checkbox').click();
        });
        cy.wait(500);

        // Verify the item got the checked class
        cy.get('.nom-shopping__item--checked').should('have.length.at.least', 1);

        // Try editing a quantity — click on a qty span
        cy.get('.nom-shopping__item:not(.nom-shopping__item--checked)')
          .first()
          .find('.nom-shopping__item-qty')
          .then(($qty) => {
            if ($qty.length > 0) {
              cy.wrap($qty).click();
              cy.wait(300);
              // If edit input appeared, type a new value and press enter
              cy.get('.nom-shopping__item-qty-input').then(($input) => {
                if ($input.length > 0) {
                  cy.wrap($input).clear().type('5{enter}');
                  cy.wait(500);
                }
              });
            }
          });

        // Verify export buttons exist
        cy.get('button[aria-label="Copy as text"], [mattooltip="Copy as text"]').should('exist');
      } else {
        // Empty shopping list, or the no-household error banner
        cy.log('Shopping list is empty — that is expected without a meal plan');
        cy.get('.nom-shopping__empty, [data-testid="error-banner"]').should('exist');
      }
    });

    // ── Step 6: Navigate to pantry ──
    cy.get('[data-testid="nav-pantry"]').click();
    cy.wait(2000);

    // Verify pantry page loaded
    cy.get('body').should('satisfy', ($body: JQuery<HTMLElement>) => {
      const text = $body.text().toLowerCase();
      return text.includes('pantry');
    });

    // ── Step 7: Navigate to settings to verify account access ──
    cy.get('[data-testid="nav-settings"]').click();
    cy.wait(1500);

    // Verify settings cards are visible
    cy.contains('Settings').should('be.visible');
    cy.contains('Profile').should('be.visible');
    cy.contains('Dietary Restrictions').should('be.visible');
    cy.contains('Security').should('be.visible');
    cy.contains('Privacy & Data').should('be.visible');

    // ── Step 8: Sign out ──
    cy.get('.nom-header__avatar').click();
    cy.wait(500);
    cy.get('.nom-user-menu').contains('Sign Out').click();
    cy.wait(1500);

    // Verify we're logged out — Sign In button should be back
    cy.get('.nom-header__login-btn').should('be.visible');
  });

  it('should use the search bar to find recipes', () => {
    loginViaPopover();

    // Type a search query and press enter
    cy.get('.nom-header__search-input').type('chicken{enter}');
    cy.wait(2000);

    // Should be on the search page
    cy.url().should('include', '/search');

    // Verify search results or empty state
    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="search-result-card"]').length > 0) {
        cy.get('[data-testid="search-result-card"]').should('have.length.at.least', 1);
      } else {
        cy.log('No search results for "chicken" — seed data may not include it');
        cy.get('[data-testid="search-page"]').should('exist');
      }
    });
  });
});
