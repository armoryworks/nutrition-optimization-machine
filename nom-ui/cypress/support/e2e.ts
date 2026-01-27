// Cypress E2E Support File

// Custom command to wait for Angular to be ready
Cypress.Commands.add('waitForAngular', () => {
  cy.window().then((win) => {
    return new Cypress.Promise((resolve) => {
      const checkAngular = () => {
        if ((win as any).getAllAngularTestabilities) {
          const testabilities = (win as any).getAllAngularTestabilities();
          const allStable = testabilities.every((t: any) => t.isStable());
          if (allStable) {
            resolve();
          } else {
            setTimeout(checkAngular, 100);
          }
        } else {
          setTimeout(checkAngular, 100);
        }
      };
      checkAngular();
    });
  });
});

// Custom command to screenshot a page
Cypress.Commands.add('screenshotPage', (name: string) => {
  cy.wait(500); // Allow animations to settle
  cy.screenshot(name, { capture: 'viewport' });
});

declare global {
  namespace Cypress {
    interface Chainable {
      waitForAngular(): Chainable<void>;
      screenshotPage(name: string): Chainable<void>;
    }
  }
}

export {};
