import { selectedViewports } from '../support/viewports';

const VIEWPORTS = selectedViewports();
const CHECKED_CLASS = 'nom-shopping__item--checked';
const unique = (base: string) => `${base} ${Date.now().toString(36)}`;

const shoppingItem = (name: string) => cy.byTestId('shopping-item').filter(`:contains("${name}")`);

const addItem = (name: string, outcome: 'created' | 'already-listed') => {
  cy.byTestId('shopping-add-item-input').clear().type(name);
  cy.byTestId('shopping-add-item-submit-btn').click();
  cy.wait('@addItem').its('response.statusCode').should('eq', outcome === 'created' ? 201 : 200);
  shoppingItem(name).should('have.length', 1);
};

const tick = (name: string) => {
  shoppingItem(name).find('[data-testid="shopping-item-checkbox"]').click();
  cy.wait('@check').its('response.statusCode').should('eq', 200);
};

VIEWPORTS.forEach((vp) => {
  describe(`Shopping list: household items and label verification (${vp.name} ${vp.width}x${vp.height})`, () => {
    beforeEach(() => {
      cy.viewport(vp.width, vp.height);
    });

    it('adds a staple without a question, asks about a new food on tick, skips, then records a label and measurements', () => {
      const granola = unique('Granola');
      const crackers = unique('Crackers');
      cy.registerAndLogin(`shopping list ${vp.name}`);

      cy.intercept('POST', '**/api/Shopping/items').as('addItem');
      cy.intercept('POST', '**/api/Shopping/checks').as('check');
      cy.intercept('POST', '**/api/Shopping/verification/*/skip').as('skipVerification');
      cy.intercept('POST', '**/api/Shopping/verification/*/label').as('label');
      cy.intercept('PUT', '**/api/Shopping/verification/*').as('confirm');
      cy.intercept('POST', '**/api/Shopping/verification/*/measurements').as('measurements');

      cy.navTo('nav-shopping');
      cy.byTestId('shopping').should('be.visible');
      cy.byTestId('shopping-add-item-input').should('be.visible');

      addItem('Paper towels', 'created');
      cy.byTestId('shopping-dept').filter(':contains("Household")').should('have.length', 1);
      tick('Paper towels');
      cy.byTestId('verification-sheet').should('not.exist');
      shoppingItem('Paper towels').should('have.class', CHECKED_CLASS);

      addItem(granola, 'created');
      tick(granola);
      cy.byTestId('verification-sheet').should('be.visible');
      cy.byTestId('verification-sheet-scan-label-btn').should('be.visible');
      cy.byTestId('verification-sheet-scan-barcode-btn').should('be.visible');
      cy.byTestId('verification-sheet-not-food-btn').should('be.visible');
      cy.byTestId('verification-sheet-skip-btn').should('be.visible').click();
      cy.wait('@skipVerification').its('response.statusCode').should('eq', 204);
      cy.byTestId('verification-sheet').should('not.exist');
      shoppingItem(granola).should('have.class', CHECKED_CLASS);

      tick(granola);
      shoppingItem(granola).should('not.have.class', CHECKED_CLASS);
      tick(granola);
      shoppingItem(granola).should('have.class', CHECKED_CLASS);
      cy.byTestId('verification-sheet').should('not.exist');

      addItem(crackers, 'created');
      tick(crackers);
      cy.byTestId('verification-sheet').should('be.visible');
      cy.byTestId('verification-sheet-label-input').selectFile('cypress/fixtures/nutrition-label.jpg', { force: true });
      cy.wait('@label', { timeout: 60000 }).its('response.statusCode').should('eq', 200);
      cy.byTestId('label-confirm-sheet').should('be.visible');
      cy.byTestId('label-confirm-basis').should('contain.text', 'per serving');
      cy.byTestId('label-confirm-serving-grams-input').clear().type('30');
      cy.byTestId('label-confirm-calories-input').clear().type('120');
      cy.byTestId('label-confirm-nutrient-fat').clear().type('4');
      cy.byTestId('label-confirm-save-btn').click();
      cy.wait('@confirm').its('response.statusCode').should('eq', 200);

      cy.byTestId('measurements-sheet').should('be.visible');
      cy.byTestId('measurements-package-size-input').type('500');
      cy.byTestId('measurements-pieces-input').type('24');
      cy.byTestId('measurements-save-btn').click();
      cy.wait('@measurements').its('response.statusCode').should('eq', 200);
      cy.byTestId('measurements-sheet').should('not.exist');
      shoppingItem(crackers).should('have.class', CHECKED_CLASS);

      addItem('Paper towels', 'already-listed');
      cy.contains('is already on your list').should('be.visible');
      shoppingItem('Paper towels').should('not.have.class', CHECKED_CLASS);
    });
  });
});
