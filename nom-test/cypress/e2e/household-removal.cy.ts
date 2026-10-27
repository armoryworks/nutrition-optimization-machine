import { selectedViewports } from '../support/viewports';
import { DEFAULT_TEST_PASSWORD } from '../support/commands';
import { convertKitchenAndCopyInviteCode, registerAndJoinWithCode } from '../support/household';

const viewports = selectedViewports();
let householdLoads = 0;

function loadHousehold(session?: Record<string, string>): Cypress.Chainable<Array<{ id: number; name: string }>> {
  const alias = `householdListRequest${(householdLoads += 1)}`;
  cy.intercept('GET', '**/api/Household').as(alias);
  cy.visit('/household', {
    onBeforeLoad(win) {
      Object.keys(session ?? {}).forEach((key) => win.localStorage.setItem(key, (session ?? {})[key]));
    },
  });
  cy.location('pathname').should('eq', '/household');
  return cy.wait(`@${alias}`).then(({ response }) => {
    expect(response?.statusCode).to.eq(200);
    return response?.body as Array<{ id: number; name: string }>;
  });
}

viewports.forEach((viewport) => {
  describe(`Household removal (${viewport.name} ${viewport.width}x${viewport.height})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('a removed member loses the household on their next page load, without signing in again', () => {
      const stewardName = `E2E Steward ${viewport.name}`;
      const memberName = `E2E Removed ${viewport.name}`;
      const householdName = `E2E Removal ${viewport.name} ${Date.now().toString(36)}`;
      const memberSession: Record<string, string> = {};

      cy.registerAndLogin(`removal-steward-${viewport.name}`, { fullName: stewardName }).then((steward) => {
        convertKitchenAndCopyInviteCode(householdName).then((code) => {
          cy.signOut();

          registerAndJoinWithCode(`removal-member-${viewport.name}`, memberName, code).then(() => {
            loadHousehold().then((households) => {
              expect(households.map((h) => h.name)).to.include(householdName);
            });
            cy.byTestId('household-item').should('contain.text', householdName);
            cy.window().then((win) => {
              Object.keys(win.localStorage).forEach((key) => {
                memberSession[key] = win.localStorage.getItem(key) ?? '';
              });
              expect(memberSession).to.have.property('authToken');
            });
            cy.signOut();

            cy.loginAs(steward.email, DEFAULT_TEST_PASSWORD);
            loadHousehold();
            cy.intercept('DELETE', '**/api/Household/*/member/*').as('removeMemberRequest');
            cy.byTestId('household-member-row').contains(memberName)
              .parents('[data-testid="household-member-row"]')
              .within(() => {
                cy.byTestId('household-member-remove-btn').click();
              });
            cy.wait('@removeMemberRequest').its('response.statusCode').should('eq', 204);
            cy.byTestId('household-member-row').should('not.exist');
            cy.signOut();

            loadHousehold(memberSession).then((households) => {
              expect(households.map((h) => h.name)).not.to.include(householdName);
            });
            cy.byTestId('header-avatar-btn').should('be.visible');
            cy.byTestId('household').should('be.visible').and('not.contain.text', householdName);
          });
        });
      });
    });
  });
});
