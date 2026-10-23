import { selectedViewports } from '../support/viewports';

const MEAL_TYPES = ['Breakfast', 'Lunch', 'Dinner', 'Snacks'];
const FRIENDLY_TITLE = /^(Breakfast|Lunch|Dinner|Snacks) — [A-Z][a-z]{2}, [A-Z][a-z]{2} \d{1,2}$/;
const ISO_DATE = /\d{4}-\d{2}-\d{2}/;
const GLUTEN_FREE_RECIPE = 'Veggie Scrambled Eggs';
const GLUTEN_RECIPE = 'Spaghetti Bolognese';

const todayIndex = () => (new Date().getDay() + 6) % 7;

const openPlan = () => {
  cy.intercept('GET', '**/api/MealPlan/week*').as('weekRequest');
  cy.intercept('GET', '**/api/Plan/curated').as('curatedRequest');
  cy.navTo('nav-meal-plan');
  cy.location('pathname').should('eq', '/plan');
  cy.wait('@weekRequest').its('response.statusCode').should('eq', 200);
  cy.byTestId('plan-calendar').should('be.visible');
};

const openCellDialog = (cellIndex: number) => {
  cy.byTestId('plan-cell').eq(cellIndex).scrollIntoView().click();
  cy.byTestId('recipe-search-dialog').should('be.visible');
  cy.focused().should('have.attr', 'data-testid', 'recipe-search-input');
  return cy.byTestId('recipe-search-dialog-title').invoke('text').then((text) => text.trim());
};

const addRecipeFromDialog = (recipeName: string) => {
  cy.intercept('POST', '**/api/RecipeSearch/search').as('recipeSearchRequest');
  cy.intercept('GET', '**/api/Recipe/*/diet').as('dietMatchesRequest');
  cy.intercept('POST', '**/api/MealPlan').as('createEntryRequest');
  cy.byTestId('recipe-search-input').type(recipeName);
  cy.wait('@recipeSearchRequest').its('response.statusCode').should('eq', 200);
  cy.byTestId('recipe-search-result-item').contains(recipeName).click();
  cy.wait('@dietMatchesRequest');
};

const addCeliacRestriction = () => {
  cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveRestrictionsRequest');
  cy.navTo('nav-settings');
  cy.byTestId('settings-restrictions-card').scrollIntoView().click();
  cy.location('pathname').should('eq', '/restrictions');
  cy.byTestId('restrictions-section').contains('Medical Conditions').closest('[data-testid="restrictions-section"]').within(() => {
    cy.byTestId('restrictions-search-input').type('celiac');
  });
  cy.byTestId('restrictions-option').contains('Celiac Disease').click();
  cy.byTestId('restrictions-chip').should('contain.text', 'Celiac Disease');
  cy.byTestId('restrictions-submit-btn').click();
  cy.wait('@saveRestrictionsRequest').its('response.statusCode').should('be.within', 200, 299);
};

const mealPlanJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  describe(`Meal plan (${viewportName})`, () => {
    const isPhone = viewportName === 'phone';

    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('renders the current week with seven days, the print entry and the toolbar', () => {
      cy.registerAndLogin(`meal-plan-week-${viewportName}`, { planName: 'Week Plan' });
      openPlan();

      cy.byTestId('plan-day-header').should('have.length', 7);
      cy.byTestId('plan-meal-label').should('have.length', MEAL_TYPES.length);
      MEAL_TYPES.forEach((meal, index) => {
        cy.byTestId('plan-meal-label').eq(index).should('have.text', meal);
      });
      cy.byTestId('plan-cell').should('have.length', 7 * MEAL_TYPES.length);
      cy.byTestId('plan-cell-entry').should('not.exist');
      cy.byTestId('plan-day-nutrition').should('have.length', 7);

      cy.byTestId('plan-print-btn').should('exist').and('be.enabled');
      cy.byTestId('plan-print').should('exist').and('not.be.visible');
      cy.byTestId('plan-print-day').should('have.length', 7);
      cy.byTestId('plan-print-meal').should('have.length', 7 * MEAL_TYPES.length);

      cy.byTestId('plan-rules-link').should('exist');
      cy.byTestId('plan-shuffle-btn').should('be.enabled');
      cy.byTestId('plan-shuffle-timeframe-btn').should('be.enabled');
      cy.byTestId('plan-today-btn').should('be.disabled');
      cy.wait('@curatedRequest').then(({ response }) => {
        expect(response?.statusCode).to.eq(200);
        if ((response?.body as unknown[]).length === 0) {
          cy.byTestId('plan-curated-link').should('not.exist');
        } else {
          cy.byTestId('plan-curated-link').should('exist');
        }
      });

      if (isPhone) {
        cy.byTestId('header-nav-toggle-btn').should('be.visible');
        cy.byTestId('plan-day-strip').should('be.visible');
        cy.byTestId('plan-day-chip').should('have.length', 7);
        cy.byTestId('plan-day-chip').eq(todayIndex()).should('have.attr', 'aria-selected', 'true');
        cy.byTestId('plan-day-header').filter(':visible').should('have.length', 1);
        cy.byTestId('plan-cell').filter(':visible').should('have.length', MEAL_TYPES.length);
        cy.byTestId('plan-day-nutrition').filter(':visible').should('have.length', 1);

        const otherDay = todayIndex() === 0 ? 1 : 0;
        cy.byTestId('plan-day-chip').eq(otherDay).click();
        cy.byTestId('plan-day-chip').eq(otherDay).should('have.attr', 'aria-selected', 'true');
        cy.byTestId('plan-day-header').filter(':visible').should('have.length', 1);
        cy.byTestId('plan-day-header').eq(otherDay).should('be.visible');
      } else {
        cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
        cy.byTestId('plan-day-strip').should('not.be.visible');
        cy.byTestId('plan-day-header').filter(':visible').should('have.length', 7);
      }

      cy.byTestId('plan-next-week-btn').click();
      cy.wait('@weekRequest').its('response.statusCode').should('eq', 200);
      cy.byTestId('plan-today-btn').should('be.enabled').click();
      cy.wait('@weekRequest').its('response.statusCode').should('eq', 200);
      cy.byTestId('plan-today-btn').should('be.disabled');
    });

    it("adds a recipe to today's breakfast from the cell dialog and shows the day's nutrition totals", () => {
      cy.registerAndLogin(`meal-plan-add-${viewportName}`, { planName: 'Add Plan' });
      openPlan();

      const todayCell = () => cy.byTestId('plan-cell').eq(todayIndex());
      openCellDialog(todayIndex()).then((title) => {
        expect(title).to.match(FRIENDLY_TITLE);
        expect(title).not.to.match(ISO_DATE);
        expect(title.startsWith('Breakfast')).to.eq(true);
      });
      cy.byTestId('recipe-search-entry-item').should('not.exist');

      addRecipeFromDialog(GLUTEN_FREE_RECIPE);
      cy.byTestId('diet-conflict-dialog').should('not.exist');
      cy.wait('@createEntryRequest').its('response.statusCode').should('be.within', 200, 299);
      cy.byTestId('recipe-search-entry-item').should('have.length', 1).and('contain.text', GLUTEN_FREE_RECIPE);
      cy.byTestId('recipe-search-done-btn').click();

      cy.wait('@weekRequest').its('response.statusCode').should('eq', 200);
      cy.byTestId('recipe-search-dialog').should('not.exist');
      todayCell().within(() => {
        cy.byTestId('plan-cell-entry').should('have.length', 1).and('contain.text', GLUTEN_FREE_RECIPE);
        cy.byTestId('plan-cell-recipe-link').should('exist');
        cy.byTestId('plan-cell-complete-btn').should('exist');
        cy.byTestId('plan-portions-btn').should('exist');
      });
      cy.byTestId('plan-cell-entry').should('have.length', 1);
      cy.byTestId('plan-day-nutrition').eq(todayIndex()).scrollIntoView().within(() => {
        cy.byTestId('plan-day-calories').invoke('text').should('match', /^\s*[1-9]\d* cal\s*$/);
      });
      cy.byTestId('plan-day-calories').should('have.length', 1);
      cy.byTestId('plan-print-item').should('have.length', 1).and('contain.text', GLUTEN_FREE_RECIPE);
    });

    it('shuffles the next 7 days, fills every remaining cell and asks before reshuffling filled meals', () => {
      cy.registerAndLogin(`meal-plan-shuffle-${viewportName}`, { planName: 'Shuffle Plan' });
      openPlan();
      cy.intercept('POST', '**/api/MealPlan/shuffle').as('shuffleRequest');

      cy.byTestId('plan-cell-entry').should('not.exist');
      cy.byTestId('plan-shuffle-timeframe-btn').click();
      cy.byTestId('plan-shuffle-days-7').should('be.visible').click();
      cy.wait('@shuffleRequest').then(({ request, response }) => {
        expect(response?.statusCode).to.be.within(200, 299);
        expect(request.body.replaceExisting).to.eq(false);
        expect(response?.body.created).to.be.greaterThan(0);
      });

      const firstRemaining = todayIndex();
      const remainingDays = 7 - firstRemaining;
      cy.byTestId('plan-cell-entry').should('have.length.at.least', remainingDays);
      MEAL_TYPES.forEach((_, row) => {
        for (let day = firstRemaining; day < 7; day++) {
          cy.byTestId('plan-cell').eq(row * 7 + day).find('[data-testid="plan-cell-entry"]').should('have.length.at.least', 1);
        }
      });
      cy.byTestId('plan-day-calories').should('have.length.at.least', remainingDays);
      cy.byTestId('plan-print-item').should('have.length.at.least', remainingDays);
      if (isPhone) {
        cy.byTestId('plan-cell').filter(':visible').each(($cell) => {
          cy.wrap($cell).find('[data-testid="plan-cell-entry"]').should('have.length.at.least', 1);
        });
      }

      cy.byTestId('plan-shuffle-btn').click();
      cy.byTestId('shuffle-dialog').should('be.visible');
      cy.byTestId('shuffle-empty-btn').should('be.visible');
      cy.byTestId('shuffle-replace-btn').should('be.visible');
      cy.byTestId('shuffle-cancel-btn').click();
      cy.byTestId('shuffle-dialog').should('not.exist');
      cy.get('@shuffleRequest.all').should('have.length', 1);
    });

    it('asks before adding a recipe that conflicts with celiac disease, with Cancel focused, and Cancel adds nothing', () => {
      cy.registerAndLogin(`meal-plan-celiac-${viewportName}`, { planName: 'Celiac Plan' });
      addCeliacRestriction();
      openPlan();

      openCellDialog(todayIndex()).should('match', FRIENDLY_TITLE);

      addRecipeFromDialog(GLUTEN_RECIPE);
      cy.byTestId('recipe-search-result-conflict').should('exist');
      cy.byTestId('diet-conflict-dialog').should('be.visible');
      cy.byTestId('diet-conflict-message').should('contain.text', 'Celiac Disease');
      cy.byTestId('diet-conflict-add-btn').should('be.visible');
      cy.byTestId('diet-conflict-cancel-btn').should('have.focus');
      cy.byTestId('diet-conflict-cancel-btn').click();
      cy.byTestId('diet-conflict-dialog').should('not.exist');

      cy.byTestId('recipe-search-entry-item').should('not.exist');
      cy.get('@createEntryRequest.all').should('have.length', 0);
      cy.byTestId('recipe-search-done-btn').click();
      cy.byTestId('recipe-search-dialog').should('not.exist');
      cy.byTestId('plan-cell-entry').should('not.exist');
      cy.byTestId('plan-cell-diet-conflict').should('not.exist');
      cy.get('@createEntryRequest.all').should('have.length', 0);
    });
  });
};

selectedViewports().forEach((viewport) => mealPlanJourney(viewport.name, viewport));
