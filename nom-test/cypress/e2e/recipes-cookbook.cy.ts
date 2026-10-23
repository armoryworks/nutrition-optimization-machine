import { selectedViewports } from '../support/viewports';

const VIEWPORTS = selectedViewports();

const SEARCH_TERM = 'black beans';
const SEEDED_BLACK_BEAN_RECIPE = 'Black Bean and Corn Salad';
const CONFLICT_PILL_LABEL = 'Hiding conflicts with my restrictions';

const unique = (prefix: string) => `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

const searchFromHeader = (phone: boolean, query: string, alias = 'searchRequest') => {
  cy.intercept('POST', '**/api/RecipeSearch/search').as(alias);
  if (phone) {
    cy.byTestId('header-search-input').should('not.be.visible');
    cy.byTestId('header-search-toggle-btn').click();
    cy.byTestId('header-mobile-search-input').should('be.visible').type(`${query}{enter}`);
  } else {
    cy.byTestId('header-search-input').should('be.visible').clear().type(`${query}{enter}`);
  }
  cy.location('pathname').should('eq', '/search');
  cy.location('search').should((search) => {
    expect(decodeURIComponent(search)).to.contain(`q=${query}`);
  });
  cy.wait(`@${alias}`).its('response.statusCode').should('eq', 200);
  cy.byTestId('search-page').should('be.visible');
  cy.byTestId('search-loading').should('not.exist');
};

const resultNames = () =>
  cy.byTestId('search-result-card').then(($cards) =>
    $cards.map((_, card) => Cypress.$(card).find('[data-testid="search-result-name"]').text().trim()).get().sort());

const openNewRecipeForm = () => {
  cy.intercept('GET', '**/api/Measurement/all').as('measurementsRequest');
  cy.navTo('nav-my-recipes');
  cy.byTestId('my-recipes').should('be.visible');
  cy.byTestId('my-recipes-new-btn').click();
  cy.location('pathname').should('eq', '/recipe/new');
  cy.byTestId('recipe-form').should('be.visible');
  cy.wait('@measurementsRequest');
  cy.byTestId('recipe-form-fields').should('be.visible');
};

const fillMeasuredRowWithoutIngredient = () => {
  cy.byTestId('recipe-ingredient-row').should('have.length', 1).first().within(() => {
    cy.byTestId('recipe-ingredient-qty-input').clear().type('2');
    cy.byTestId('recipe-ingredient-unit-select').click();
  });
  cy.byTestId('recipe-ingredient-unit-option').contains('Cup').click();
};

const pickIngredient = (typed: string, optionName: string) => {
  cy.intercept('GET', '**/api/Ingredients/search*').as('ingredientSearch');
  cy.byTestId('recipe-ingredient-row').first().within(() => {
    cy.byTestId('recipe-ingredient-name-input').clear().type(typed);
  });
  cy.wait('@ingredientSearch').its('response.statusCode').should('eq', 200);
  cy.byTestId('recipe-ingredient-option').contains(optionName).click();
  cy.byTestId('recipe-ingredient-row').first().within(() => {
    cy.byTestId('recipe-ingredient-name-input').should('have.value', optionName);
  });
};

const submitAndOpenDetail = (recipeName: string) => {
  cy.intercept('POST', '**/api/Recipe').as('createRecipe');
  cy.byTestId('recipe-submit-btn').click();
  cy.wait('@createRecipe').its('response.statusCode').should('be.within', 200, 299);
  cy.location('pathname').should('match', /^\/recipe\/\d+$/);
  cy.byTestId('recipe-detail').should('be.visible');
  cy.byTestId('recipe-tab-panel').should('contain.text', recipeName);
  return cy.location('pathname').then((pathname) => Number(pathname.split('/').pop()));
};

const createBlackBeanRecipe = (recipeName: string) => {
  openNewRecipeForm();
  cy.byTestId('recipe-name-input').type(recipeName);
  cy.byTestId('recipe-description-input').type('Two cups of black beans with no gram weight, for the nutrition label.');
  fillMeasuredRowWithoutIngredient();
  pickIngredient('black beans', 'Black Beans');
  cy.byTestId('recipe-step-row').first().within(() => {
    cy.byTestId('recipe-step-input').type('Rinse the beans and warm them through.');
  });
  return submitAndOpenDetail(recipeName);
};

const addSevereRestriction = (sectionLabel: string, typed: string, restrictionName: string) => {
  cy.intercept('POST', '**/api/Reference/bulk').as('restrictionGroups');
  cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveRestrictions');
  cy.visit('/restrictions');
  cy.byTestId('restrictions').should('be.visible');
  cy.wait('@restrictionGroups');
  cy.byTestId('restrictions-section').filter(`:contains("${sectionLabel}")`).first().within(() => {
    cy.byTestId('restrictions-search-input').type(typed);
  });
  cy.byTestId('restrictions-option').contains(restrictionName).click();
  cy.byTestId('restrictions-chip').should('contain.text', restrictionName);
  cy.byTestId('restrictions-submit-btn').click();
  cy.wait('@saveRestrictions').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('restrictions-success').scrollIntoView().should('be.visible');
};

VIEWPORTS.forEach((viewport) => {
  describe(`Recipes and cookbooks (${viewport.name})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('finds black bean recipes from the header search, offers nutrition sorts and shows a sourced nutrition label', () => {
      cy.visit('/home');
      searchFromHeader(viewport.phone, SEARCH_TERM);

      cy.byTestId('search-results-list').should('be.visible');
      cy.byTestId('search-result-card').should('have.length.greaterThan', 0);
      cy.byTestId('search-result-card').contains(SEEDED_BLACK_BEAN_RECIPE).should('exist');
      cy.byTestId('search-count').invoke('text').should('match', /\d+ recipes? found/);

      cy.byTestId('search-sort-select').click();
      cy.byTestId('search-sort-option-protein').scrollIntoView().should('be.visible').and('contain.text', 'Most protein');
      cy.byTestId('search-sort-option-calories').scrollIntoView().should('be.visible').and('contain.text', 'Fewest calories');
      cy.intercept('POST', '**/api/RecipeSearch/search', (req) => {
        if (req.body?.sortBy === 'protein') req.alias = 'proteinSearch';
      });
      cy.byTestId('search-sort-option-protein').scrollIntoView().click();
      cy.wait('@proteinSearch').its('response.statusCode').should('eq', 200);
      cy.location('search').should('contain', 'sort=protein');
      cy.byTestId('search-sort-select').should('contain.text', 'Most protein');
      cy.byTestId('search-result-card').should('have.length.greaterThan', 0);

      cy.intercept('GET', '**/api/Recipe/*').as('recipeRequest');
      cy.byTestId('search-result-card').contains(SEEDED_BLACK_BEAN_RECIPE).click();
      cy.wait('@recipeRequest').its('response.statusCode').should('eq', 200);
      cy.location('pathname').should('match', /^\/recipe\/\d+$/);
      cy.byTestId('recipe-detail').should('be.visible');
      cy.byTestId('recipe-nutrition-tab').click();
      cy.byTestId('recipe-tab-panel').find('[data-testid="nutrition-label"]').should('be.visible');
      cy.byTestId('nutrition-label-source').scrollIntoView().should('be.visible').invoke('text').should('match', /^Nutrition /);
      cy.byTestId('nutrition-label-nutrient-item').should('have.length.greaterThan', 0);

      if (viewport.phone) {
        cy.byTestId('recipe-tab-nav').then(($nav) => {
          expect($nav.outerWidth()).to.be.greaterThan($nav.outerHeight()!);
        });
      }
    });

    it.skip('BUG: recipe search is a whole-phrase substring match on name/description, so "canned black" and "beans black" find nothing while "black beans" finds 7 recipes', () => {
      cy.visit('/home');
      searchFromHeader(viewport.phone, SEARCH_TERM);
      resultNames().then((expected) => {
        expect(expected).to.include(SEEDED_BLACK_BEAN_RECIPE);
        searchFromHeader(viewport.phone, 'beans black', 'reorderedSearch');
        resultNames().should('deep.equal', expected);
        searchFromHeader(viewport.phone, 'canned black', 'cannedSearch');
        cy.byTestId('search-result-card').contains(SEEDED_BLACK_BEAN_RECIPE).should('exist');
      });
    });

    it('hides conflicting recipes with a filter chip once the user has a severe restriction', () => {
      cy.registerAndLogin('recipes cookbook restrictions');

      searchFromHeader(viewport.phone, SEARCH_TERM);
      cy.byTestId('search-result-card').should('have.length.greaterThan', 0);
      cy.byTestId('search-filter-pill').should('not.exist');

      addSevereRestriction('Allergies & Intolerances', 'Peanut', 'Peanut Allergy');

      searchFromHeader(viewport.phone, SEARCH_TERM, 'restrictedSearch');
      cy.get('@restrictedSearch').its('response.body.dietConflictsHidden').should('eq', true);
      cy.byTestId('search-filter-pills').should('be.visible');
      cy.byTestId('search-filter-pill').should('have.length', 1).and('contain.text', CONFLICT_PILL_LABEL);
      cy.byTestId('search-filter-count').should('have.text', '1');

      cy.byTestId('search-filter-btn').click();
      cy.byTestId('search-filter-panel').should('be.visible');
      cy.byTestId('search-filter-conflicts-toggle').should('be.visible').find('button[role="switch"]').should('have.attr', 'aria-checked', 'true');
      cy.byTestId('search-filter-close-btn').click();
      cy.byTestId('search-filter-pill').should('contain.text', CONFLICT_PILL_LABEL);
    });

    it('refuses a measured ingredient row with no ingredient, then saves once it is picked and flags the uncounted line', () => {
      const recipeName = unique('E2E Black Bean Bowl');
      cy.registerAndLogin('recipes cookbook form');

      openNewRecipeForm();
      cy.byTestId('recipe-name-input').type(recipeName);
      cy.byTestId('recipe-description-input').type('Two cups of black beans with no gram weight, for the nutrition label.');
      fillMeasuredRowWithoutIngredient();
      cy.byTestId('recipe-step-row').first().within(() => {
        cy.byTestId('recipe-step-input').type('Rinse the beans and warm them through.');
      });

      if (viewport.phone) {
        cy.byTestId('recipe-ingredient-row').first().then(($row) => {
          const rowWidth = $row.innerWidth()!;
          const nameWidth = $row.find('[data-testid="recipe-ingredient-name-input"]').closest('mat-form-field').outerWidth()!;
          expect(nameWidth, 'ingredient name field spans the row on a phone').to.be.greaterThan(rowWidth * 0.9);
        });
      }

      cy.intercept('POST', '**/api/Recipe').as('createRecipe');
      cy.byTestId('recipe-submit-btn').click();
      cy.byTestId('recipe-ingredient-required-error').should('be.visible').and('contain.text', 'Ingredient is required');
      cy.byTestId('recipe-form-submit-error').scrollIntoView().should('be.visible');
      cy.location('pathname').should('eq', '/recipe/new');
      cy.get('@createRecipe.all').should('have.length', 0);

      pickIngredient('black beans', 'Black Beans');
      cy.byTestId('recipe-ingredient-required-error').should('not.exist');

      submitAndOpenDetail(recipeName);
      cy.byTestId('recipe-edit-btn').should('be.visible');
      cy.byTestId('recipe-nutrition-tab').click();
      cy.byTestId('recipe-tab-panel').find('[data-testid="nutrition-label-empty"]').should('be.visible');
      cy.byTestId('nutrition-label-uncounted').should('be.visible');
      cy.byTestId('nutrition-label-uncounted-item').should('have.length', 1).and('contain.text', 'Black Beans').and('contain.text', '2 Cup');
    });

    it('collects the new recipe into a cookbook and removes it again', () => {
      const recipeName = unique('E2E Cookbook Beans');
      const cookbookName = unique('E2E Weeknight');
      cy.registerAndLogin('recipes cookbook collect');

      createBlackBeanRecipe(recipeName).then((recipeId) => {
        cy.intercept('POST', '**/api/Cookbook').as('createCookbook');
        cy.navTo('nav-cookbooks');
        cy.byTestId('cookbook-list').should('be.visible');
        cy.byTestId('cookbooks-no-household').should('not.exist');
        cy.byTestId('cookbook-list-empty').should('be.visible');
        cy.byTestId('cookbook-list-create-btn').click();
        cy.byTestId('cookbook-form-dialog').should('be.visible');
        cy.byTestId('cookbook-name-input').type(cookbookName);
        cy.byTestId('cookbook-description-input').type('Quick dinners collected by the E2E suite.');
        cy.byTestId('cookbook-save-btn').click();
        cy.wait('@createCookbook').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('cookbook-form-dialog').should('not.exist');
        cy.byTestId('cookbook-card').should('have.length', 1).and('contain.text', cookbookName).and('contain.text', '0 recipes');

        cy.intercept('GET', /\/api\/Cookbook\?householdId=\d+$/).as('myCookbooks');
        cy.intercept('POST', '**/api/Cookbook/*/recipe/*').as('addToCookbook');
        cy.visit(`/recipe/${recipeId}`);
        cy.byTestId('recipe-detail').should('be.visible');
        cy.byTestId('recipe-cookbook-chip').should('not.exist');
        cy.byTestId('recipe-add-to-cookbook-btn').click();
        cy.wait('@myCookbooks');
        cy.byTestId('recipe-cookbook-menu-item').should('have.length', 1).and('contain.text', cookbookName).click();
        cy.wait('@addToCookbook').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('recipe-cookbook-chip').should('have.length', 1).and('contain.text', cookbookName);

        cy.intercept('GET', '**/api/Cookbook/*/recipes').as('cookbookRecipes');
        cy.intercept('DELETE', '**/api/Cookbook/*/recipe/*').as('removeFromCookbook');
        cy.byTestId('recipe-cookbook-chip').click();
        cy.location('pathname').should('match', /^\/cookbook\/\d+$/);
        cy.wait('@cookbookRecipes');
        cy.byTestId('cookbook-detail').should('be.visible').and('contain.text', cookbookName);
        cy.byTestId('cookbook-recipe-item').should('have.length', 1).and('contain.text', recipeName);
        cy.byTestId('cookbook-recipe-remove-btn').click();
        cy.wait('@removeFromCookbook').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('cookbook-recipe-item').should('not.exist');
        cy.byTestId('cookbook-detail-empty').should('be.visible');

        cy.visit(`/recipe/${recipeId}`);
        cy.byTestId('recipe-detail').should('be.visible');
        cy.byTestId('recipe-cookbook-chip').should('not.exist');
      });
    });
  });
});

export {};
