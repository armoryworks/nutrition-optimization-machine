import { DEFAULT_TEST_PASSWORD } from '../support/commands';
import { selectedViewports } from '../support/viewports';

interface RecipeSearchResponse {
  results: { id: number; name: string }[];
  totalCount: number;
}

const PLAN_NAME = 'E2E Daily Plan';
const PLANNED_RECIPE = 'Spinach and Feta Omelette';

const todayIndex = () => (new Date().getDay() + 6) % 7;

const addTodaysBreakfast = (recipeName: string) => {
  cy.intercept('POST', '**/api/RecipeSearch/search').as('recipeSearch');
  cy.intercept('POST', '**/api/MealPlan').as('createMealEntry');
  cy.byTestId('plan-cell').eq(todayIndex()).scrollIntoView().click();
  cy.byTestId('recipe-search-dialog').should('be.visible');
  cy.focused().should('have.attr', 'data-testid', 'recipe-search-input');
  cy.byTestId('recipe-search-input').type(recipeName);
  cy.wait('@recipeSearch').its('response.statusCode').should('eq', 200);
  cy.byTestId('recipe-search-result-item').contains(recipeName).click();
  cy.wait('@createMealEntry').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('recipe-search-entry-item').should('have.length', 1).and('contain.text', recipeName);
  cy.byTestId('recipe-search-done-btn').click();
  cy.byTestId('recipe-search-dialog').should('not.exist');
  cy.wait('@planWeek').its('response.statusCode').should('eq', 200);
  cy.byTestId('plan-cell').eq(todayIndex()).find('[data-testid="plan-cell-entry"]').should('have.length', 1).and('contain.text', recipeName);
};

const signInAsReturningUserFromHeader = (journey: string, planName?: string) => {
  cy.registerAndLogin(journey, { fullName: 'Daily Test User', planName }).then(({ email, password }) => {
    cy.signOut();
    cy.loginViaHeaderPopover(email, password);
    cy.location('pathname').should('eq', '/home');
    cy.byTestId('dashboard').should('be.visible');
  });
};

const dailyJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  const isPhone = viewportName === 'phone';

  const searchFromHeader = (term: string) => {
    cy.intercept('POST', '**/api/RecipeSearch/search').as('search');
    if (isPhone) {
      cy.byTestId('header-search-input').should('not.be.visible');
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

  describe(`Daily user workflow (${viewportName})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('signs in from the header, reads a recipe, plans a meal, checks the shopping list, pantry and settings, then signs out', () => {
      signInAsReturningUserFromHeader(`daily-${viewportName}`, PLAN_NAME);

      if (isPhone) {
        cy.byTestId('header-nav-toggle-btn').should('be.visible');
      } else {
        cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
      }

      searchFromHeader('eggs');
      cy.get('@search').its('response.body.totalCount').should('be.greaterThan', 0);
      cy.intercept('GET', '**/api/Recipe/*').as('recipeDetail');
      cy.byTestId('search-result-card').first().click();
      cy.location('pathname').should('match', /^\/recipe\/\d+$/);
      cy.wait('@recipeDetail').its('response.statusCode').should('eq', 200);
      cy.byTestId('recipe-detail').should('be.visible');
      cy.byTestId('recipe-title').should('be.visible');
      cy.byTestId('recipe-ingredients').should('exist');
      cy.byTestId('recipe-instructions').should('exist');
      cy.byTestId('recipe-nutrition-tab').click();
      cy.byTestId('recipe-nutrition-tab').should('have.attr', 'aria-selected', 'true');
      cy.byTestId('recipe-tab-panel').within(() => {
        cy.get('[data-testid="nutrition-label"], [data-testid="nutrition-label-empty"]').should('exist');
      });
      cy.byTestId('recipe-back-link').scrollIntoView().click();
      cy.location('pathname').should('eq', '/recipes/mine');
      cy.byTestId('my-recipes').should('be.visible');

      cy.intercept('GET', '**/api/MealPlan/week*').as('planWeek');
      cy.navTo('nav-meal-plan');
      cy.location('pathname').should('eq', '/plan');
      cy.wait('@planWeek').its('response.statusCode').should('eq', 200);
      cy.byTestId('plan-calendar').should('be.visible');
      cy.byTestId('plan-day-header').should('have.length', 7);
      cy.byTestId('plan-meal-label').should('have.length', 4);
      cy.byTestId('plan-next-week-btn').click();
      cy.wait('@planWeek').its('response.statusCode').should('eq', 200);
      cy.byTestId('plan-calendar').should('be.visible');
      cy.byTestId('plan-today-btn').click();
      cy.wait('@planWeek').its('response.statusCode').should('eq', 200);
      cy.byTestId('plan-calendar').should('be.visible');
      cy.byTestId('plan-shuffle-btn').should('be.visible');
      addTodaysBreakfast(PLANNED_RECIPE);

      cy.intercept('GET', '**/api/Pantry?*').as('pantryItems');
      cy.navTo('nav-shopping');
      cy.location('pathname').should('eq', '/shopping');
      cy.byTestId('shopping').should('be.visible');
      cy.wait('@pantryItems').its('response.statusCode').should('eq', 200);
      cy.byTestId('shopping-empty').should('not.exist');
      cy.byTestId('shopping-item').should('have.length.at.least', 1);
      cy.byTestId('shopping-item-ingredient-link').filter(':contains("Eggs")').should('have.length', 1);

      cy.navTo('nav-pantry');
      cy.location('pathname').should('eq', '/pantry');
      cy.wait('@pantryItems').its('response.statusCode').should('eq', 200);
      cy.byTestId('pantry').should('be.visible');
      cy.byTestId('pantry-empty').should('be.visible');

      cy.navTo('nav-settings');
      cy.location('pathname').should('eq', '/settings');
      cy.byTestId('settings').should('be.visible');
      for (const card of ['settings-profile-card', 'settings-restrictions-card', 'settings-security-card', 'settings-privacy-card']) {
        cy.byTestId(card).scrollIntoView().should('be.visible');
      }
      cy.byTestId('settings-admin-card').should('not.exist');

      cy.signOut();
      cy.byTestId('header-avatar-btn').should('not.exist');
    });

    it('finds recipes from the header search bar', () => {
      signInAsReturningUserFromHeader(`daily-search-${viewportName}`);

      searchFromHeader('chicken');
      cy.get('@search').its('response.body').then((body: RecipeSearchResponse) => {
        expect(body.totalCount, 'seeded chicken recipes').to.be.greaterThan(0);
        expect(body.results.length, 'first page of results').to.be.greaterThan(0);
        cy.byTestId('search-empty').should('not.exist');
        cy.byTestId('search-result-card').should('have.length', body.results.length);
        cy.byTestId('search-count').should('contain.text', String(body.totalCount));
      });
    });

    it('takes a brand-new user who signs in from the header popover straight to onboarding', () => {
      cy.uniqueEmail(`daily-popover-${viewportName}`).then((email) => {
        cy.registerViaUi(email, DEFAULT_TEST_PASSWORD, 'Daily Test User');
        cy.intercept('GET', '**/api/Person/me').as('personMe');
        cy.intercept('GET', '**/api/Person/*/onboarding').as('onboardingState');
        cy.loginViaHeaderPopover(email, DEFAULT_TEST_PASSWORD);
        cy.wait('@personMe').its('response.statusCode').should('eq', 200);
        cy.wait('@onboardingState').its('response.body.isComplete').should('eq', false);
        cy.location('pathname').should('eq', '/onboarding');
        cy.byTestId('onboarding').should('be.visible');
        cy.window().its('localStorage.personId').should('be.a', 'string').and('not.be.empty');
      });
    });
  });
};

selectedViewports().forEach((viewport) => dailyJourney(viewport.name, viewport));
