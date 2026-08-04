/**
 * NOM Anonymous Browsing E2E Test
 *
 * This test covers what an unauthenticated visitor does:
 * 1. Land on the home page and browse curated recipe categories
 * 2. Click into a recipe detail — read ingredients, steps, switch to nutrition tab
 * 3. Use the search bar to find recipes by keyword
 * 4. Verify protected pages redirect or show appropriate messaging
 * 5. Verify the Sign In / Create Account links are visible and functional
 *
 * No login required. Assumes seed data exists so the home page has recipes.
 */

describe('Anonymous Browsing', () => {

  beforeEach(() => {
    // Always start fresh — no session, no cookies
    cy.clearCookies();
    cy.clearLocalStorage();
  });

  it('should land on the home page and see curated recipe categories', () => {
    cy.visit('/home');
    cy.wait(1500);

    // Header should show "Sign In" (not an avatar)
    cy.get('.nom-header__login-btn').should('be.visible');
    cy.get('.nom-header__avatar').should('not.exist');

    // Search placeholder should say "Search recipes..." (not the logged-in variant)
    cy.get('.nom-header__search-input')
      .should('have.attr', 'placeholder')
      .and('include', 'Search recipes');

    // Home page should render
    cy.get('[data-testid="home-page"]').should('exist');

    // Check for recipe categories or empty state
    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="home-category"]').length > 0) {
        // Categories exist — verify structure
        cy.get('[data-testid="home-category"]').should('have.length.at.least', 1);

        // Each category should have a header and at least one recipe card
        cy.get('[data-testid="home-category"]').first().within(() => {
          cy.get('.nom-home__category-header').should('be.visible');
          cy.get('[data-testid="recipe-card"]').should('have.length.at.least', 1);
        });

        // Recipe cards should show name, and optionally time/rating
        cy.get('[data-testid="recipe-card"]').first().within(() => {
          cy.get('.nom-home__recipe-name').should('be.visible');
        });
      } else {
        // No seed data — empty state should render cleanly
        cy.get('.nom-home__empty').should('exist');
        cy.contains('No recipes yet').should('be.visible');
      }
    });
  });

  it('should click into a recipe detail and read all sections', () => {
    cy.visit('/home');
    cy.wait(1500);

    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="recipe-card"]').length === 0) {
        cy.log('No seed recipes — skipping detail test');
        return;
      }

      // Click the first recipe card
      cy.get('[data-testid="recipe-card"]').first().click();
      cy.wait(1500);

      // Should be on the recipe detail page
      cy.get('[data-testid="recipe-detail"]').should('exist');

      // ── Overview tab (default) ──
      cy.get('.nom-recipe__title').should('be.visible');

      // Meta section should exist (even if some fields are empty)
      cy.get('.nom-recipe__meta').should('exist');

      // ── Ingredients column ──
      cy.get('.nom-recipe__column--ingredients').should('exist');
      cy.get('.nom-recipe__column--ingredients').within(() => {
        cy.contains('Ingredients').should('be.visible');
        // Either has ingredient items or shows "No ingredients listed."
        // (cy.get('body') is invalid inside .within() — use the scoped root.)
        cy.root().then(($col) => {
          if ($col.find('.nom-recipe__ingredient').length > 0) {
            cy.get('.nom-recipe__ingredient').should('have.length.at.least', 1);
          } else {
            cy.contains('No ingredients listed').should('be.visible');
          }
        });
      });

      // ── Instructions column ──
      cy.get('.nom-recipe__column--instructions').should('exist');
      cy.get('.nom-recipe__column--instructions').within(() => {
        cy.contains('Instructions').should('be.visible');
      });

      // ── Switch to Nutrition tab ──
      cy.contains('button', 'Nutrition').click();
      cy.wait(500);

      // Nutrition label component should render (either with data or empty message)
      cy.get('.nom-nutrition-label, .nom-nutrition-label--empty').should('exist');

      // ── Switch to Diet tab ──
      cy.contains('button', 'Diet').click();
      cy.wait(300);

      // ── Back to home via breadcrumb ──
      cy.get('[data-testid="recipe-back-link"]').click();
      cy.wait(1000);

      cy.get('[data-testid="home-page"]').should('exist');
    });
  });

  it('should use the search bar to find recipes', () => {
    cy.visit('/home');
    cy.wait(1000);

    // Type a broad term and press enter
    cy.get('.nom-header__search-input').type('recipe{enter}');
    cy.wait(2000);

    // Should navigate to the search page
    cy.url().should('include', '/search');
    cy.get('[data-testid="search-page"]').should('exist');

    // Results or empty state
    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="search-result-card"]').length > 0) {
        cy.get('[data-testid="search-result-card"]').should('have.length.at.least', 1);

        // Click a result to verify it navigates to detail
        cy.get('[data-testid="search-result-card"]').first().click();
        cy.wait(1500);
        cy.get('[data-testid="recipe-detail"]').should('exist');
      } else {
        cy.log('No search results — seed data may not match "recipe"');
      }
    });
  });

  it('should show Sign In and Create Account links from the login popover', () => {
    cy.visit('/home');
    cy.wait(1000);

    // Click "Sign In" in the header
    cy.get('.nom-header__login-btn').click();
    cy.wait(500);

    // Login popover should appear
    cy.get('.nom-login-popover').should('be.visible');

    // Should have email and password fields
    cy.get('.nom-login-popover').within(() => {
      cy.get('input[formcontrolname="email"]').should('exist');
      cy.get('input[formcontrolname="password"]').should('exist');
      cy.contains('button', 'Sign In').should('exist');

      // "Forgot password?" link
      cy.contains('Forgot password?').should('be.visible');

      // "Create Account" link in the footer
      cy.contains('Create Account').should('be.visible');
    });

    // Click "Create Account" to verify it navigates to registration
    cy.get('.nom-login-popover').contains('Create Account').click();
    cy.wait(1000);

    cy.url().should('include', '/register');
    cy.contains('Create Account').should('be.visible');
    cy.get('input[formcontrolname="email"]').should('exist');
    cy.get('input[formcontrolname="password"]').should('exist');
    cy.get('input[formcontrolname="confirmPassword"]').should('exist');
  });

  it('should redirect protected routes to home or show auth prompt', () => {
    // Try accessing a protected route directly
    cy.visit('/plan');
    cy.wait(2000);

    // Auth guard should redirect to home or show login
    cy.url().then((url) => {
      // Either redirected to /home or stayed but shows login prompt
      if (url.includes('/home') || url.includes('/login')) {
        cy.log('Correctly redirected away from protected route');
      } else {
        // May have landed on /plan but with an auth prompt
        cy.get('.nom-header__login-btn').should('be.visible');
      }
    });

    // Try another protected route
    cy.visit('/recipes/mine');
    cy.wait(2000);

    cy.url().then((url) => {
      if (url.includes('/home') || url.includes('/login')) {
        cy.log('Correctly redirected away from /recipes/mine');
      } else {
        cy.get('.nom-header__login-btn').should('be.visible');
      }
    });
  });

  it('should navigate between multiple recipe details using the home page', () => {
    cy.visit('/home');
    cy.wait(1500);

    cy.get('[data-testid="recipe-card"]').then(($cards) => {
      if ($cards.length < 2) {
        cy.log('Not enough seed recipes to test multi-detail navigation');
        return;
      }

      // Visit first recipe
      cy.get('[data-testid="recipe-card"]').eq(0).click();
      cy.wait(1000);
      cy.get('.nom-recipe__title').invoke('text').then((firstTitle) => {
        // Go back
        cy.get('[data-testid="recipe-back-link"]').click();
        cy.wait(1000);

        // Visit second recipe
        cy.get('[data-testid="recipe-card"]').eq(1).click();
        cy.wait(1000);
        cy.get('.nom-recipe__title').invoke('text').then((secondTitle) => {
          // Titles should be different
          expect(firstTitle.trim()).to.not.equal(secondTitle.trim());
        });
      });
    });
  });

  it('should toggle between light and dark theme', () => {
    cy.visit('/home');
    cy.wait(1000);

    // Find the theme toggle button in the header
    cy.get('.nom-header__action-btn').first().then(($btn) => {
      // Check the current icon
      const iconText = $btn.find('mat-icon').text().trim();
      const isDark = iconText === 'light_mode';

      // Click to toggle
      cy.wrap($btn).click();
      cy.wait(500);

      // Icon should have changed
      if (isDark) {
        cy.wrap($btn).find('mat-icon').should('contain.text', 'dark_mode');
      } else {
        cy.wrap($btn).find('mat-icon').should('contain.text', 'light_mode');
      }

      // Toggle back
      cy.wrap($btn).click();
      cy.wait(300);
    });
  });
});
