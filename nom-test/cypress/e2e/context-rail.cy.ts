import { selectedViewports } from '../support/viewports';

const WIDE_VIEWPORT = { width: 1440, height: 900 };

const runsOnDesktop = selectedViewports().some((viewport) => !viewport.phone);

(runsOnDesktop ? describe : describe.skip)('Context rail on a wide desktop (1440x900)', () => {
  beforeEach(() => {
    cy.viewport(WIDE_VIEWPORT.width, WIDE_VIEWPORT.height);
  });

  it('keeps the Upcoming Meals panel on home and gives the recipe and the plan the full width', () => {
    cy.registerAndLogin('context-rail-wide', { onboarding: 'none' });
    cy.byTestId('onboarding').should('be.visible');
    cy.byTestId('context-rail').should('not.exist');
    cy.completeOnboardingSolo({ name: 'E2E context-rail-wide' });

    cy.location('pathname').should('eq', '/home');
    cy.byTestId('dashboard').should('be.visible');
    cy.byTestId('context-rail').should('be.visible');
    cy.byTestId('sidebar-upcoming-meals').should('be.visible');
    cy.byTestId('sidebar-toggle-btn').should('not.be.visible');

    cy.intercept('POST', '**/api/RecipeSearch/search').as('search');
    cy.byTestId('header-search-input').should('be.visible').type('eggs{enter}');
    cy.location('pathname').should('eq', '/search');
    cy.wait('@search').its('response.body.totalCount').should('be.greaterThan', 0);
    cy.byTestId('search-loading').should('not.exist');
    cy.byTestId('context-rail').should('be.visible');

    cy.intercept('GET', '**/api/Recipe/*').as('recipeDetail');
    cy.byTestId('search-result-card').first().click();
    cy.location('pathname').should('match', /^\/recipe\/\d+$/);
    cy.wait('@recipeDetail').its('response.statusCode').should('eq', 200);
    cy.byTestId('recipe-detail').should('be.visible');
    cy.byTestId('context-rail').should('not.exist');
    cy.byTestId('sidebar-toggle-btn').should('not.exist');
    cy.byTestId('recipe-instructions').should('be.visible').invoke('outerWidth').should('be.greaterThan', 400);

    cy.intercept('GET', '**/api/MealPlan/week*').as('planWeek');
    cy.navTo('nav-meal-plan');
    cy.location('pathname').should('eq', '/plan');
    cy.wait('@planWeek').its('response.statusCode').should('eq', 200);
    cy.byTestId('plan-calendar').should('be.visible');
    cy.byTestId('context-rail').should('not.exist');
    cy.byTestId('sidebar-toggle-btn').should('not.exist');

    cy.navTo('nav-home');
    cy.location('pathname').should('eq', '/home');
    cy.byTestId('context-rail').should('be.visible');
  });
});
