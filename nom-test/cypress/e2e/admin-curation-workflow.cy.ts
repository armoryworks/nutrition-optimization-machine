import { selectedViewports } from '../support/viewports';

const unique = (prefix: string) => `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

const queueItemNamed = (name: string) => cy.contains('[data-testid="curation-item"]', name);

const openNewRecipeForm = () => {
  cy.navTo('nav-my-recipes');
  cy.byTestId('my-recipes').should('be.visible');
  cy.byTestId('my-recipes-new-btn').click();
  cy.location('pathname').should('eq', '/recipe/new');
  cy.byTestId('recipe-form').should('be.visible');
  cy.byTestId('recipe-form-fields').should('be.visible');
};

const createRecipeAndSubmitForReview = (name: string, description: string) => {
  openNewRecipeForm();
  cy.byTestId('recipe-name-input').type(name);
  cy.byTestId('recipe-description-input').type(description);

  cy.intercept('GET', '**/api/Ingredients/search*').as('ingredientSearch');
  cy.byTestId('recipe-ingredient-row').should('have.length', 1).first().within(() => {
    cy.byTestId('recipe-ingredient-qty-input').clear().type('1');
    cy.byTestId('recipe-ingredient-unit-select').click();
  });
  cy.byTestId('recipe-ingredient-unit-option').contains('Cup').click();
  cy.byTestId('recipe-ingredient-row').first().within(() => {
    cy.byTestId('recipe-ingredient-name-input').type('salt');
  });
  cy.wait('@ingredientSearch').its('response.statusCode').should('eq', 200);
  cy.byTestId('recipe-ingredient-option').contains('Salt').click();
  cy.byTestId('recipe-ingredient-row').first().within(() => {
    cy.byTestId('recipe-ingredient-name-input').invoke('val').should('match', /salt/i);
  });

  cy.byTestId('recipe-step-row').first().within(() => {
    cy.byTestId('recipe-step-input').type('Combine all ingredients and serve.');
  });

  cy.intercept('POST', '**/api/Recipe').as('createRecipe');
  cy.byTestId('recipe-submit-btn').click();
  cy.wait('@createRecipe').its('response.statusCode').should('be.within', 200, 299);
  cy.location('pathname').should('match', /^\/recipe\/\d+$/);
  cy.byTestId('recipe-detail').should('be.visible');
  cy.byTestId('recipe-title').should('have.text', name);

  cy.intercept('POST', '**/api/Curation/submit').as('submitForCuration');
  cy.byTestId('recipe-make-public-btn').scrollIntoView().click();
  cy.wait('@submitForCuration').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('recipe-pending-badge').should('be.visible');
  cy.byTestId('recipe-make-public-btn').should('not.exist');
};

const openCurationQueue = () => {
  cy.intercept('GET', '**/api/Curation/queue').as('curationQueue');
  cy.visit('/admin/curation');
  cy.byTestId('curation-queue').should('be.visible');
  cy.wait('@curationQueue').its('response.statusCode').should('eq', 200);
  cy.get('[data-testid="curation-list"], [data-testid="curation-empty"]').should('exist');
};

const refreshCurationQueue = () => {
  cy.byTestId('curation-refresh-btn').click();
  cy.wait('@curationQueue').its('response.statusCode').should('eq', 200);
  cy.get('[data-testid="curation-list"], [data-testid="curation-empty"]').should('exist');
};

const searchFromHeader = (isPhone: boolean, term: string) => {
  cy.intercept('POST', '**/api/RecipeSearch/search').as('search');
  if (isPhone) {
    cy.byTestId('header-search-toggle-btn').click();
    cy.byTestId('header-mobile-search-input').should('be.visible').type(`${term}{enter}`);
  } else {
    cy.byTestId('header-search-input').should('be.visible').type(`${term}{enter}`);
  }
  cy.location('pathname').should('eq', '/search');
  cy.byTestId('search-page').should('be.visible');
  cy.wait('@search').its('response.statusCode').should('eq', 200);
  cy.byTestId('search-loading').should('not.exist');
};

const curationJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  const isPhone = viewportName === 'phone';

  describe(`Admin curation workflow (${viewportName})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('an author submits two recipes, the admin sends one back and approves the other, and only the approved one is findable in search', () => {
      const saladName = unique('E2E Salad');
      const stewName = unique('E2E Stew');

      cy.registerAndLogin(`curation-author-${viewportName}`, { fullName: 'Recipe Author' });

      createRecipeAndSubmitForReview(saladName, 'A fresh salad with seasonal vegetables. Needs nutritional review.');
      createRecipeAndSubmitForReview(stewName, 'A hearty stew with root vegetables and herbs. Ready for curation.');

      cy.navTo('nav-my-recipes');
      cy.byTestId('my-recipes').should('be.visible');
      cy.contains('[data-testid="recipe-card"]', saladName).should('exist');
      cy.contains('[data-testid="recipe-card"]', stewName).should('exist');

      cy.signOut();

      cy.loginAsAdmin();
      openCurationQueue();

      cy.intercept('POST', '**/api/Curation/request-revision').as('requestRevision');
      queueItemNamed(saladName).within(() => {
        cy.byTestId('curation-status-badge').should('contain.text', 'Pending');
        cy.byTestId('curation-review-btn').click();
        cy.byTestId('curation-notes-input').type(
          'Please add complete nutritional information and specify serving sizes. ' +
            'The ingredient list needs measurements for all items.'
        );
        cy.byTestId('curation-revision-btn').click();
      });
      cy.wait('@requestRevision').its('response.statusCode').should('be.within', 200, 299);
      queueItemNamed(saladName).should('not.exist');

      refreshCurationQueue();
      queueItemNamed(saladName).within(() => {
        cy.byTestId('curation-status-badge').should('contain.text', 'Needs review');
      });

      cy.intercept('POST', '**/api/Curation/approve').as('approve');
      queueItemNamed(stewName).within(() => {
        cy.byTestId('curation-status-badge').should('contain.text', 'Pending');
        cy.byTestId('curation-review-btn').click();
        cy.byTestId('curation-blocking-ingredients').should('not.exist');
        cy.byTestId('curation-notes-input').type('Looks great. Approved for the public catalog.');
        cy.byTestId('curation-approve-btn').click();
      });
      cy.wait('@approve').its('response.statusCode').should('be.within', 200, 299);
      cy.get('.mat-mdc-snack-bar-container').should('be.visible').and('contain.text', `Approved "${stewName}"`);
      queueItemNamed(stewName).should('not.exist');

      refreshCurationQueue();
      queueItemNamed(stewName).should('not.exist');
      queueItemNamed(saladName).should('exist');

      cy.visit('/home');
      cy.byTestId('header-avatar-btn').should('be.visible');

      searchFromHeader(isPhone, stewName);
      cy.contains('[data-testid="search-result-card"]', stewName).should('be.visible').click();
      cy.location('pathname').should('match', /^\/recipe\/\d+$/);
      cy.byTestId('recipe-detail').should('be.visible');
      cy.byTestId('recipe-title').should('have.text', stewName);

      cy.visit('/home');
      cy.byTestId('header-avatar-btn').should('be.visible');
      searchFromHeader(isPhone, saladName);
      cy.contains('[data-testid="search-result-card"]', saladName).should('not.exist');
      cy.get('[data-testid="search-empty"], [data-testid="search-results-list"]').should('exist');

      cy.signOut();
    });
  });
};

selectedViewports().forEach((viewport) => curationJourney(viewport.name, viewport));
