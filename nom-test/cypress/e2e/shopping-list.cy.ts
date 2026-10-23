/**
 * NOM Shopping List E2E Test — household items and label verification
 *
 * Covers the Release A shopping lane end to end through the real UI:
 * 1. Register and sign in (header popover), make sure a household exists
 * 2. Add a non-food staple (no catalog food is created, no question asked)
 * 3. Add a brand-new food the catalog does not know
 * 4. Tick it — the verification sheet offers Scan label / Scan barcode / Not a food / Skip
 * 5. Skip for a month, re-tick: not asked again this session
 * 6. Add a second new food, tick it, photograph a blank "label": the confirm
 *    sheet lets the values be typed, Save leads to the measurements sheet, Save there
 * 7. Add an existing item again: the list says it is already there
 */

const BLANK_LABEL_PNG = 'iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAAAS0lEQVR42u3PMQ0AAAwDoPo33UrYvQQckD4XAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAYHLAMpT0sIcNbcEAAAAAElFTkSuQmCC';

describe('Shopping list — household items and label verification', () => {
  const testPassword = Cypress.env('TEST_PASSWORD') || 'TestPassword123!';
  const stamp = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  const userEmail = `shop-${stamp}@example.com`;
  const firstFood = `Granola ${stamp}`;
  const secondFood = `Crackers ${stamp}`;

  before(() => {
    cy.visit('/register');
    cy.get('input[formcontrolname="email"]').type(userEmail);
    cy.get('input[formcontrolname="fullName"]').type('Shopping Test User');
    cy.get('input[formcontrolname="password"]').type(testPassword);
    cy.get('input[formcontrolname="confirmPassword"]').type(testPassword);
    cy.contains('button', 'Create Account').click();
    cy.wait(3000);
  });

  const loginViaPopover = () => {
    cy.visit('/home');
    cy.wait(1000);
    cy.get('.nom-header__login-btn').click();
    cy.wait(500);
    cy.get('.nom-login-popover').within(() => {
      cy.get('input[formcontrolname="email"]').type(userEmail);
      cy.get('input[formcontrolname="password"]').type(testPassword);
      cy.contains('button', 'Sign In').click();
    });
    cy.get('.nom-header__avatar', { timeout: 10000 }).should('be.visible');
  };

  const addItem = (name: string) => {
    cy.get('[data-testid="shopping-add-item-input"]').clear().type(name);
    cy.get('[data-testid="shopping-add-item-submit-btn"]').click();
    cy.contains('[data-testid="shopping-item"]', name, { timeout: 10000 }).should('exist');
  };

  const tick = (name: string) => {
    cy.contains('[data-testid="shopping-item"]', name).within(() => {
      cy.get('[data-testid="shopping-item-checkbox"]').click();
    });
  };

  it('adds items, asks about an unverified food on tick, skips, records a label and measurements', () => {
    loginViaPopover();
    cy.visit('/shopping');
    cy.get('[data-testid="shopping"]', { timeout: 10000 }).should('exist');

    cy.get('body').then(($body) => {
      if ($body.find('[data-testid="no-household-cta"]').length > 0) {
        cy.get('[data-testid="no-household-solo-btn"]').click();
        cy.wait(2000);
      }
    });
    cy.get('[data-testid="shopping-add-item-input"]', { timeout: 15000 }).should('exist');

    addItem('Paper towels');
    cy.contains('.nom-shopping__dept-name', 'Household').should('exist');
    tick('Paper towels');
    cy.wait(1000);
    cy.get('[data-testid="verification-sheet"]').should('not.exist');
    cy.contains('[data-testid="shopping-item"]', 'Paper towels').should('have.class', 'nom-shopping__item--checked');

    addItem(firstFood);
    tick(firstFood);
    cy.get('[data-testid="verification-sheet"]', { timeout: 10000 }).should('be.visible');
    cy.get('[data-testid="verification-sheet-scan-label-btn"]').should('be.visible');
    cy.get('[data-testid="verification-sheet-scan-barcode-btn"]').should('be.visible');
    cy.get('[data-testid="verification-sheet-not-food-btn"]').should('be.visible');
    cy.get('[data-testid="verification-sheet-skip-btn"]').click();
    cy.get('[data-testid="verification-sheet"]').should('not.exist');
    cy.contains('[data-testid="shopping-item"]', firstFood).should('have.class', 'nom-shopping__item--checked');

    tick(firstFood);
    cy.wait(500);
    tick(firstFood);
    cy.wait(1000);
    cy.get('[data-testid="verification-sheet"]').should('not.exist');

    addItem(secondFood);
    tick(secondFood);
    cy.get('[data-testid="verification-sheet"]', { timeout: 10000 }).should('be.visible');
    cy.get('[data-testid="verification-sheet-label-input"]').selectFile(
      { contents: Cypress.Buffer.from(BLANK_LABEL_PNG, 'base64'), fileName: 'label.png', mimeType: 'image/png' },
      { force: true },
    );
    cy.get('[data-testid="label-confirm-sheet"]', { timeout: 30000 }).should('be.visible');
    cy.get('[data-testid="label-confirm-basis"]').should('contain.text', 'per serving');
    cy.get('[data-testid="label-confirm-serving-grams-input"]').clear().type('30');
    cy.get('[data-testid="label-confirm-calories-input"]').clear().type('120');
    cy.get('[data-testid="label-confirm-nutrient-fat"]').clear().type('4');
    cy.get('[data-testid="label-confirm-save-btn"]').click();

    cy.get('[data-testid="measurements-sheet"]', { timeout: 15000 }).should('be.visible');
    cy.get('[data-testid="measurements-package-size-input"]').type('500');
    cy.get('[data-testid="measurements-pieces-input"]').type('24');
    cy.get('[data-testid="measurements-save-btn"]').click();
    cy.get('[data-testid="measurements-sheet"]').should('not.exist');
    cy.contains('[data-testid="shopping-item"]', secondFood).should('have.class', 'nom-shopping__item--checked');

    cy.get('[data-testid="shopping-add-item-input"]').clear().type('Paper towels');
    cy.get('[data-testid="shopping-add-item-submit-btn"]').click();
    cy.contains('is already on your list', { timeout: 10000 }).should('exist');
    cy.get('[data-testid="shopping-item"]').filter(':contains("Paper towels")').should('have.length', 1);
    cy.contains('[data-testid="shopping-item"]', 'Paper towels').should('not.have.class', 'nom-shopping__item--checked');
  });
});
