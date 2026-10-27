import { TestCredentials } from './commands';

export function convertKitchenAndCopyInviteCode(householdName: string): Cypress.Chainable<string> {
  cy.intercept('GET', '**/api/Household/*').as('householdLoadRequest');
  cy.intercept('POST', '**/api/Household/*/convert').as('convertRequest');
  cy.intercept('POST', '**/api/Household/invite-token').as('inviteTokenRequest');
  cy.navTo('nav-household');
  cy.location('pathname').should('eq', '/household');
  cy.wait('@householdLoadRequest').its('response.statusCode').should('eq', 200);
  cy.byTestId('household-personal-section').within(() => {
    cy.byTestId('household-invite-btn').click();
  });
  cy.byTestId('household-convert-dialog').should('be.visible');
  cy.byTestId('household-convert-name-input').type(householdName).should('have.value', householdName);
  cy.byTestId('household-convert-create-btn').click();
  cy.wait('@convertRequest').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('invite-dialog').should('be.visible');
  cy.wait('@inviteTokenRequest').its('response.statusCode').should('be.within', 200, 299);
  return cy.byTestId('invite-dialog-code').invoke('text').then((text) => {
    const code = text.trim();
    expect(code).not.to.be.empty;
    cy.byTestId('invite-dialog-done-btn').click();
    cy.byTestId('invite-dialog').should('not.exist');
    return cy.wrap(code, { log: false });
  });
}

export function registerAndJoinWithCode(journey: string, fullName: string, code: string): Cypress.Chainable<TestCredentials> {
  return cy.registerAndLogin(journey, { fullName, onboarding: 'none' }).then((credentials) => {
    cy.intercept('PUT', '**/api/Person/*/profile').as('saveProfileRequest');
    cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveRestrictionsRequest');
    cy.intercept('POST', '**/api/Household/join').as('joinRequest');
    cy.intercept('POST', '**/api/Person/*/onboarding').as('completeOnboardingRequest');

    cy.byTestId('profile-form').should('be.visible');
    cy.byTestId('profile-name-input').clear().type(fullName);
    cy.byTestId('profile-submit-btn').click();
    cy.wait('@saveProfileRequest').its('response.statusCode').should('be.within', 200, 299);
    cy.byTestId('restrictions').should('be.visible');
    cy.byTestId('restrictions-submit-btn').click();
    cy.wait('@saveRestrictionsRequest').its('response.statusCode').should('be.within', 200, 299);

    cy.byTestId('onboarding-household-fork').should('be.visible');
    cy.byTestId('onboarding-household-option').click();
    cy.byTestId('household-join-tab').click();
    cy.byTestId('household-join-token-input').type(code).should('have.value', code);
    cy.byTestId('household-join-btn').click();
    cy.wait('@joinRequest').its('response.statusCode').should('be.within', 200, 299);
    cy.byTestId('household-continue-btn').scrollIntoView().should('be.visible').click();

    cy.byTestId('plan-tabs').scrollIntoView().should('be.visible');
    cy.byTestId('onboarding-skip-btn').click();
    cy.wait('@completeOnboardingRequest').its('response.statusCode').should('be.within', 200, 299);
    cy.location('pathname').should('eq', '/home');
    return cy.wrap(credentials, { log: false });
  });
}
