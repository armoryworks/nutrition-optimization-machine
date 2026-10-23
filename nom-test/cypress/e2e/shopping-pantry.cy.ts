import { selectedViewports } from '../support/viewports';

const PLANNED_RECIPE = 'Spinach and Feta Omelette';
const PLAN_NAME = 'E2E Shopping Plan';
const EGGS = 'Eggs';
const SPINACH = 'Baby Spinach';
const SHOPPING_LIST_TITLE = /^Shopping List — /;

const todayIndex = () => (new Date().getDay() + 6) % 7;

const viewports = selectedViewports();

const advertisedProviders = [
  {
    key: 'e2e-text',
    displayName: 'E2E Text Destination',
    kind: 'Text',
    requiresConnection: false,
    configured: true,
    connected: false,
    description: 'Advertised by the stubbed grocery service',
  },
  {
    key: 'e2e-cart',
    displayName: 'E2E Cart Destination',
    kind: 'Cart',
    requiresConnection: true,
    configured: false,
    connected: false,
    description: 'Not set up by the operator',
  },
];

function interceptShoppingData(): void {
  cy.intercept({ method: 'GET', url: /\/api\/Pantry\?/ }).as('pantryItems');
  cy.intercept('GET', '**/api/GroceryExport/providers').as('groceryProviders');
  cy.intercept('GET', '**/api/MealPlan/week*').as('planWeek');
}

function planTodaysMeal(recipeName: string): void {
  cy.intercept('GET', '**/api/MealPlan/week*').as('planWeek');
  cy.intercept('POST', '**/api/RecipeSearch/search').as('recipeSearch');
  cy.intercept('POST', '**/api/MealPlan').as('createMealEntry');

  cy.navTo('nav-meal-plan');
  cy.location('pathname').should('eq', '/plan');
  cy.wait('@planWeek');
  cy.byTestId('plan-calendar').should('be.visible');

  cy.byTestId('plan-cell').eq(todayIndex()).scrollIntoView().click();
  cy.byTestId('recipe-search-dialog').should('be.visible');
  cy.focused().should('have.attr', 'data-testid', 'recipe-search-input');
  cy.byTestId('recipe-search-input').type(recipeName);
  cy.wait('@recipeSearch');
  cy.byTestId('recipe-search-result-item').filter(`:contains("${recipeName}")`).first().click();
  cy.wait('@createMealEntry').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('recipe-search-entry-item').should('have.length', 1).and('contain.text', recipeName);
  cy.byTestId('recipe-search-done-btn').click();
  cy.byTestId('recipe-search-dialog').should('not.exist');
  cy.wait('@planWeek');
  cy.byTestId('plan-cell-recipe-link').filter(':visible').should('contain.text', recipeName);
}

function openShopping(): void {
  interceptShoppingData();
  cy.navTo('nav-shopping');
  cy.location('pathname').should('eq', '/shopping');
  cy.wait('@pantryItems');
  cy.wait('@groceryProviders');
  cy.byTestId('shopping').should('be.visible');
}

function openPantry(): void {
  cy.intercept({ method: 'GET', url: /\/api\/Pantry\?/ }).as('pantryItems');
  cy.navTo('nav-pantry');
  cy.location('pathname').should('eq', '/pantry');
  cy.wait('@pantryItems');
  cy.byTestId('pantry').should('be.visible');
}

function shoppingDept(name: string) {
  return cy.byTestId('shopping-dept').filter(`:contains("${name}")`);
}

function shoppingItem(ingredientName: string) {
  return cy
    .byTestId('shopping-item')
    .filter(`:has([data-testid="shopping-item-ingredient-link"]:contains("${ingredientName}"))`);
}

function addPantryItem(ingredientName: string, quantity: number, unitName: string): void {
  cy.intercept('GET', '**/api/Ingredients/search*').as('ingredientSearch');
  cy.intercept('POST', '**/api/Pantry').as('addPantryItem');

  cy.byTestId('pantry-add-toggle-btn').click();
  cy.byTestId('pantry-add-form').should('be.visible');
  cy.byTestId('pantry-add-submit-btn').should('be.disabled');

  cy.byTestId('pantry-ingredient-input').type(ingredientName);
  cy.wait('@ingredientSearch');
  cy.byTestId('pantry-ingredient-option').filter(`:contains("${ingredientName}")`).first().click();
  cy.byTestId('pantry-ingredient-input').should('have.value', ingredientName);

  cy.byTestId('pantry-quantity-input').type(`{selectall}${quantity}`);
  cy.byTestId('pantry-quantity-input').should('have.value', String(quantity));
  cy.byTestId('pantry-unit-select').click();
  cy.byTestId('pantry-unit-option').filter(`:contains("${unitName}")`).first().click();
  cy.byTestId('pantry-unit-select').should('contain.text', unitName);

  cy.byTestId('pantry-add-submit-btn').should('be.enabled').click();
  cy.wait('@addPantryItem').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('pantry-add-form').should('not.exist');
}

function stubShareTargets(): void {
  cy.window().then((win) => {
    cy.stub(win.navigator.clipboard, 'writeText').as('writeText').resolves();
    Object.defineProperty(win.navigator, 'share', {
      value: cy.stub().as('share').resolves(),
      configurable: true,
      writable: true,
    });
  });
}

function expectSingleTitleLine(text: string): void {
  const lines = text.split('\n');
  expect(lines[0]).to.match(SHOPPING_LIST_TITLE);
  expect(lines.filter((line) => SHOPPING_LIST_TITLE.test(line))).to.have.length(1);
  expect(lines).to.include('Produce:');
  expect(lines).to.include('Dairy & Eggs:');
  expect(lines.some((line) => line.startsWith('- ') && line.includes(EGGS))).to.eq(true);
}

viewports.forEach((viewport) => {
  describe(`Shopping list and pantry (${viewport.name})`, () => {
    const isPhone = viewport.name === 'phone';

    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('lists the planned recipe ingredients grouped by department', () => {
      cy.registerAndLogin('shopping-departments', { planName: PLAN_NAME });
      planTodaysMeal(PLANNED_RECIPE);
      openShopping();

      if (isPhone) {
        cy.byTestId('header-nav-toggle-btn').should('be.visible');
        cy.byTestId('header-search-input').should('not.be.visible');
      } else {
        cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
        cy.byTestId('nav-shopping').should('be.visible');
      }

      cy.byTestId('shopping-empty').should('not.exist');
      cy.byTestId('shopping-no-household').should('not.exist');
      cy.byTestId('shopping-dept').should('have.length.at.least', 2);
      shoppingDept('Produce').should('have.length', 1);
      shoppingDept('Dairy & Eggs').should('have.length', 1);

      shoppingDept('Produce').within(() => {
        cy.byTestId('shopping-item-ingredient-link').filter(`:contains("${SPINACH}")`).should('have.length', 1);
      });
      shoppingDept('Dairy & Eggs').within(() => {
        cy.byTestId('shopping-item-ingredient-link').filter(`:contains("${EGGS}")`).should('have.length', 1);
        cy.byTestId('shopping-item-ingredient-link').filter(':contains("Feta Cheese")').should('have.length', 1);
      });

      shoppingItem(EGGS).find('[data-testid="shopping-item-sources"]').should('contain.text', PLANNED_RECIPE);
      cy.byTestId('shopping-export-menu-btn').should('be.enabled');
    });

    it('recipe search dialog focuses its search field when it opens, so typing straight away searches', () => {
      cy.registerAndLogin('plan-dialog-focus', { planName: PLAN_NAME });
      cy.intercept('GET', '**/api/MealPlan/week*').as('planWeek');
      cy.intercept('POST', '**/api/RecipeSearch/search').as('recipeSearch');
      cy.navTo('nav-meal-plan');
      cy.wait('@planWeek');
      cy.byTestId('plan-calendar').should('be.visible');
      cy.byTestId('plan-cell').eq(todayIndex()).scrollIntoView().click();
      cy.byTestId('recipe-search-dialog').should('be.visible');
      cy.focused().should('have.attr', 'data-testid', 'recipe-search-input');
      cy.focused().type(`${EGGS} `);
      cy.byTestId('recipe-search-dialog').should('be.visible');
      cy.byTestId('recipe-search-input').should('have.value', `${EGGS} `);
      cy.wait('@recipeSearch').its('response.statusCode').should('eq', 200);
      cy.byTestId('recipe-search-close-btn').click();
      cy.byTestId('recipe-search-dialog').should('not.exist');
    });

    it('ticking an item marks it checked and the tick survives a reload', () => {
      cy.registerAndLogin('shopping-check', { planName: PLAN_NAME });
      planTodaysMeal(PLANNED_RECIPE);
      openShopping();

      cy.byTestId('shopping-complete-btn').should('not.exist');
      shoppingItem(EGGS).find('mat-checkbox input[type="checkbox"]').should('not.be.checked');
      shoppingItem(EGGS).find('mat-checkbox').click();
      shoppingItem(EGGS).find('mat-checkbox input[type="checkbox"]').should('be.checked');
      shoppingItem(SPINACH).find('mat-checkbox input[type="checkbox"]').should('not.be.checked');
      cy.byTestId('shopping-complete-btn').should('be.visible').and('contain.text', '(1)');

      interceptShoppingData();
      cy.reload();
      cy.wait('@pantryItems');
      cy.byTestId('shopping').should('be.visible');
      shoppingItem(EGGS).find('mat-checkbox input[type="checkbox"]').should('be.checked');
      shoppingItem(SPINACH).find('mat-checkbox input[type="checkbox"]').should('not.be.checked');
      cy.byTestId('shopping-complete-btn').should('be.visible').and('contain.text', '(1)');

      shoppingItem(EGGS).find('mat-checkbox').click();
      shoppingItem(EGGS).find('mat-checkbox input[type="checkbox"]').should('not.be.checked');
      cy.byTestId('shopping-complete-btn').should('not.exist');
    });

    it('copy and share text begin with a single Shopping List line', () => {
      cy.registerAndLogin('shopping-share', { planName: PLAN_NAME });
      planTodaysMeal(PLANNED_RECIPE);
      openShopping();
      cy.byTestId('shopping-item').should('have.length.at.least', 1);
      stubShareTargets();

      cy.byTestId('shopping-export-menu-btn').click();
      cy.byTestId('shopping-copy-btn').should('be.visible').click();
      cy.get('@writeText').should('have.been.calledOnce');
      cy.get('@writeText').then((writeText) => {
        const stub = writeText as unknown as sinon.SinonStub;
        expectSingleTitleLine(stub.firstCall.args[0] as string);
      });
      cy.get('@share').should('not.have.been.called');

      cy.byTestId('shopping-export-menu-btn').click();
      cy.byTestId('shopping-share-btn').should('be.visible').click();
      cy.get('@share').should('have.been.calledOnce');
      cy.get('@share').then((share) => {
        const stub = share as unknown as sinon.SinonStub;
        const data = stub.firstCall.args[0] as ShareData;
        expect(data).to.not.have.property('title');
        expectSingleTitleLine(data.text ?? '');
      });
      cy.get('@writeText').should('have.been.calledOnce');
    });

    it('shows the send-to-store area only for destinations the grocery service advertises', () => {
      cy.registerAndLogin('shopping-send-to', { planName: PLAN_NAME });
      planTodaysMeal(PLANNED_RECIPE);
      openShopping();
      cy.byTestId('shopping-item').should('have.length.at.least', 1);

      cy.get('@groceryProviders').its('response.body').then((providers: unknown[]) => {
        if (providers.length === 0) {
          cy.byTestId('shopping-list-send-btn').should('not.exist');
        } else {
          cy.byTestId('shopping-list-send-btn').should('be.visible').click();
          cy.byTestId('grocery-export-dialog').should('be.visible');
          cy.byTestId('grocery-export-provider').should('have.length', providers.length);
          cy.byTestId('grocery-export-close-btn').click();
          cy.byTestId('grocery-export-dialog').should('not.exist');
        }
      });

      cy.intercept('GET', '**/api/GroceryExport/providers', { statusCode: 200, body: advertisedProviders }).as(
        'stubbedProviders',
      );
      cy.byTestId('shopping-refresh-btn').should('be.enabled');
      cy.reload();
      cy.wait('@stubbedProviders');
      cy.byTestId('shopping-item').should('have.length.at.least', 1);

      cy.byTestId('shopping-list-send-btn').should('be.visible').click();
      cy.byTestId('grocery-export-dialog').should('be.visible');
      cy.byTestId('grocery-export-providers').should('be.visible');
      cy.byTestId('grocery-export-provider').should('have.length', advertisedProviders.length);
      advertisedProviders.forEach((provider) => {
        cy.byTestId('grocery-export-provider')
          .filter(`:contains("${provider.displayName}")`)
          .should('have.length', 1)
          .and(provider.configured ? 'be.enabled' : 'be.disabled');
      });
      cy.byTestId('grocery-export-provider-unconfigured').should('have.length', 1);
      cy.byTestId('grocery-export-send-btn').should('be.disabled');
      cy.byTestId('grocery-export-close-btn').click();
      cy.byTestId('grocery-export-dialog').should('not.exist');
    });

    it('pantry: adds an item, shows it, and removes it', () => {
      cy.registerAndLogin('pantry-add-remove');
      openPantry();

      cy.byTestId('pantry-no-household').should('not.exist');
      cy.byTestId('pantry-empty').should('be.visible');
      cy.byTestId('pantry-item').should('not.exist');

      cy.byTestId('pantry-add-toggle-btn').click();
      cy.byTestId('pantry-add-fields').should('have.css', 'flex-direction', isPhone ? 'column' : 'row');
      cy.byTestId('pantry-add-toggle-btn').click();
      cy.byTestId('pantry-add-form').should('not.exist');

      addPantryItem(EGGS, 3, 'Piece');
      cy.byTestId('pantry-empty').should('not.exist');
      cy.byTestId('pantry-list').should('be.visible');
      cy.byTestId('pantry-item').should('have.length', 1);
      cy.byTestId('pantry-item').first().find('[data-testid="pantry-item-ingredient-link"]').should('have.text', EGGS);
      cy.byTestId('pantry-item').first().should('contain.text', '3 pc');

      cy.intercept({ method: 'GET', url: /\/api\/Pantry\?/ }).as('pantryItems');
      cy.reload();
      cy.wait('@pantryItems');
      cy.byTestId('pantry-item').should('have.length', 1).and('contain.text', EGGS);

      cy.intercept('DELETE', '**/api/Pantry/*').as('removePantryItem');
      cy.byTestId('pantry-item-remove-btn').first().click();
      cy.wait('@removePantryItem').its('response.statusCode').should('be.within', 200, 299);
      cy.byTestId('pantry-item').should('not.exist');
      cy.byTestId('pantry-empty').should('be.visible');
    });

    it.skip('pantry Qty keeps the number typed after the field is emptied (BUG: emptying the Qty field snaps it to 0, so the next digit lands beside the 0)', () => {
      cy.registerAndLogin('pantry-qty-clear');
      openPantry();
      cy.byTestId('pantry-add-toggle-btn').click();
      cy.byTestId('pantry-quantity-input').should('have.value', '1');
      cy.byTestId('pantry-quantity-input').type('{selectall}{backspace}');
      cy.byTestId('pantry-quantity-input').should('have.value', '');
      cy.byTestId('pantry-quantity-input').type('3');
      cy.byTestId('pantry-quantity-input').should('have.value', '3');
    });

    it('pantry stock reduces the shopping list', () => {
      cy.registerAndLogin('pantry-reduces-shopping', { planName: PLAN_NAME });
      planTodaysMeal(PLANNED_RECIPE);
      openShopping();
      shoppingItem(EGGS).should('have.length', 1);
      shoppingItem(SPINACH).should('have.length', 1);

      openPantry();
      addPantryItem(EGGS, 12, 'Piece');
      cy.byTestId('pantry-item').should('have.length', 1).and('contain.text', EGGS);

      openShopping();
      cy.byTestId('shopping-dept').should('have.length.at.least', 1);
      shoppingItem(SPINACH).should('have.length', 1);
      shoppingItem(EGGS).should('have.length', 0);
      shoppingDept('Dairy & Eggs').within(() => {
        cy.byTestId('shopping-item-ingredient-link').filter(':contains("Feta Cheese")').should('have.length', 1);
      });
    });
  });
});
