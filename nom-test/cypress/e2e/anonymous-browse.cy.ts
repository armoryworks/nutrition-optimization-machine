import { selectedViewports } from '../support/viewports';

interface RecipeSearchResponse {
  results: { id: number; name: string }[];
  totalCount: number;
}

const openHome = () => {
  cy.intercept('GET', '**/api/RecipeSearch/popular*').as('popularRecipes');
  cy.visit('/home');
  cy.byTestId('home-page').should('exist');
  cy.wait('@popularRecipes').its('response.statusCode').should('eq', 200);
};

const openRecipeFromCard = (index: number) => {
  cy.intercept('GET', '**/api/Recipe/*').as('recipeDetail');
  cy.byTestId('recipe-card').eq(index).click();
  cy.location('pathname').should('match', /^\/recipe\/\d+$/);
  cy.wait('@recipeDetail').its('response.statusCode').should('eq', 200);
  cy.byTestId('recipe-detail').should('be.visible');
};

const anonymousJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  const isPhone = viewportName === 'phone';

  const searchFromHeader = (term: string) => {
    if (isPhone) {
      cy.byTestId('header-search-input').should('not.be.visible');
      cy.byTestId('header-search-toggle-btn').click();
      cy.byTestId('header-mobile-search-input').should('be.visible').type(`${term}{enter}`);
    } else {
      cy.byTestId('header-search-toggle-btn').should('not.be.visible');
      cy.byTestId('header-search-input').should('be.visible').type(`${term}{enter}`);
    }
  };

  describe(`Anonymous browsing (${viewportName})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
      cy.clearCookies();
      cy.clearLocalStorage();
    });

    it('lands on the home page with a Sign In button, a recipe search box and curated categories', () => {
      openHome();

      cy.byTestId('header-login-btn').should('be.visible');
      cy.byTestId('header-avatar-btn').should('not.exist');
      cy.byTestId('header-nav-toggle-btn').should('not.exist');

      if (isPhone) {
        cy.byTestId('header-search-toggle-btn').click();
        cy.byTestId('header-mobile-search-input').should('be.visible').and('have.attr', 'placeholder', 'Search recipes...');
        cy.byTestId('header-search-toggle-btn').click();
        cy.byTestId('header-mobile-search-input').should('not.exist');
      } else {
        cy.byTestId('header-search-input').should('be.visible').and('have.attr', 'placeholder', 'Search recipes...');
      }

      cy.byTestId('home-hero').should('be.visible');
      cy.byTestId('home-create-account-btn').should('be.visible');
      cy.byTestId('home-sign-in-btn').should('be.visible');

      cy.get('@popularRecipes').its('response.body').then((body: RecipeSearchResponse) => {
        if (body.results.length === 0) {
          cy.byTestId('home-empty').should('be.visible');
          cy.byTestId('home-category').should('not.exist');
          return;
        }
        cy.byTestId('home-empty').should('not.exist');
        cy.byTestId('home-category').should('have.length.at.least', 1);
        cy.byTestId('home-category').first().within(() => {
          cy.byTestId('home-category-title').should('be.visible').invoke('text').should('not.be.empty');
          cy.byTestId('recipe-card').should('have.length.at.least', 1);
          cy.byTestId('recipe-card-name').first().should('be.visible').invoke('text').should('not.be.empty');
        });
      });
    });

    it('opens a recipe from the home page and reads its overview, nutrition and diet tabs', () => {
      openHome();
      cy.byTestId('recipe-card').should('have.length.at.least', 1);
      openRecipeFromCard(0);

      cy.byTestId('recipe-title').should('be.visible').invoke('text').should('not.be.empty');
      cy.byTestId('recipe-meta').should('exist');
      cy.byTestId('recipe-ingredients').should('exist');
      cy.get('[data-testid="recipe-ingredient"], [data-testid="recipe-ingredients-empty"]').should('exist');
      cy.byTestId('recipe-instructions').should('exist');
      cy.byTestId('recipe-edit-btn').should('not.exist');

      cy.byTestId('recipe-nutrition-tab').click();
      cy.byTestId('recipe-nutrition-tab').should('have.attr', 'aria-selected', 'true');
      cy.byTestId('recipe-tab-panel').within(() => {
        cy.get('[data-testid="nutrition-label"], [data-testid="nutrition-label-empty"]').should('exist');
      });

      cy.byTestId('recipe-diet-tab').click();
      cy.byTestId('recipe-diet-tab').should('have.attr', 'aria-selected', 'true');
      cy.byTestId('recipe-tab-panel').should('be.visible');

      cy.byTestId('recipe-overview-tab').click();
      cy.byTestId('recipe-overview-tab').should('have.attr', 'aria-selected', 'true');
      cy.byTestId('recipe-ingredients').should('exist');

      cy.byTestId('recipe-back-link').scrollIntoView().click();
      cy.location('pathname').should('eq', '/home');
      cy.byTestId('home-page').should('exist');
    });

    it('searches recipes from the header and opens a result', () => {
      openHome();
      cy.intercept('POST', '**/api/RecipeSearch/search').as('search');

      searchFromHeader('salad');
      cy.location('pathname').should('eq', '/search');
      cy.byTestId('search-page').should('be.visible');
      cy.wait('@search').its('response.statusCode').should('eq', 200);

      cy.get('@search').its('response.body').then((body: RecipeSearchResponse) => {
        expect(body.totalCount, 'seeded salad recipes').to.be.greaterThan(0);
        expect(body.results.length, 'first page of results').to.be.greaterThan(0);
        cy.byTestId('search-empty').should('not.exist');
        cy.byTestId('search-result-card').should('have.length', body.results.length);
        cy.byTestId('search-count').should('contain.text', String(body.totalCount));
      });
      cy.intercept('GET', '**/api/Recipe/*').as('recipeDetail');
      cy.byTestId('search-result-card').first().click();
      cy.location('pathname').should('match', /^\/recipe\/\d+$/);
      cy.wait('@recipeDetail').its('response.statusCode').should('eq', 200);
      cy.byTestId('recipe-detail').should('be.visible');
    });

    it('offers password recovery and account creation from the Sign In popover', () => {
      openHome();

      cy.byTestId('header-login-btn').click();
      cy.byTestId('login-popover').should('be.visible');
      cy.byTestId('login-popover').within(() => {
        cy.byTestId('login-email-input').should('be.visible');
        cy.byTestId('login-password-input').should('be.visible');
        cy.byTestId('login-submit-btn').should('be.visible');
        cy.byTestId('login-forgot-password-link').should('be.visible').and('have.attr', 'href', '/forgot-password');
        cy.byTestId('login-create-account-link').should('be.visible').click();
      });

      cy.location('pathname').should('eq', '/register');
      cy.byTestId('login-popover').should('not.exist');
      cy.byTestId('register-form').should('be.visible');
      cy.byTestId('register-email-input').should('be.visible');
      cy.byTestId('register-password-input').should('be.visible');
      cy.byTestId('register-confirm-password-input').should('be.visible');
      cy.byTestId('register-submit-btn').scrollIntoView().should('be.visible');
    });

    it('sends a visitor who opens a protected page back to the home page', () => {
      for (const route of ['/plan', '/recipes/mine', '/shopping', '/settings', '/admin']) {
        cy.visit(route);
        cy.location('pathname').should('eq', '/home');
        cy.byTestId('header-login-btn').should('be.visible');
        cy.byTestId('home-page').should('exist');
      }
    });

    it('moves between two recipe details through the home page', () => {
      openHome();
      cy.byTestId('recipe-card').should('have.length.at.least', 2);

      openRecipeFromCard(0);
      cy.byTestId('recipe-title').invoke('text').then((firstTitle) => {
        const firstName = firstTitle.trim();
        cy.byTestId('recipe-back-link').scrollIntoView().click();
        cy.location('pathname').should('eq', '/home');
        cy.wait('@popularRecipes');
        cy.byTestId('recipe-card').should('have.length.at.least', 2);

        cy.intercept('GET', '**/api/Recipe/*').as('secondRecipe');
        cy.byTestId('recipe-card')
          .filter((_, card) => Cypress.$(card).find('[data-testid="recipe-card-name"]').text().trim() !== firstName)
          .first()
          .click();
        cy.location('pathname').should('match', /^\/recipe\/\d+$/);
        cy.wait('@secondRecipe').its('response.statusCode').should('eq', 200);
        cy.byTestId('recipe-detail').should('be.visible');
        cy.byTestId('recipe-title').invoke('text').should((secondTitle) => {
          expect(secondTitle.trim()).to.not.equal(firstName);
        });
      });
    });

    it('toggles between dark and light theme and back', () => {
      openHome();

      cy.get('body').should('have.class', 'dark-theme');
      cy.byTestId('header-theme-toggle-btn').find('mat-icon').should('have.text', 'light_mode');

      cy.byTestId('header-theme-toggle-btn').click();
      cy.get('body').should('have.class', 'light-theme').and('not.have.class', 'dark-theme');
      cy.byTestId('header-theme-toggle-btn').find('mat-icon').should('have.text', 'dark_mode');

      cy.byTestId('header-theme-toggle-btn').click();
      cy.get('body').should('have.class', 'dark-theme').and('not.have.class', 'light-theme');
      cy.byTestId('header-theme-toggle-btn').find('mat-icon').should('have.text', 'light_mode');
    });
  });
};

selectedViewports().forEach((viewport) => anonymousJourney(viewport.name, viewport));
