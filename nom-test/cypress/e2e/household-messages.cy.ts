import { selectedViewports } from '../support/viewports';
import { DEFAULT_TEST_PASSWORD, TestCredentials } from '../support/commands';

const viewports = selectedViewports();

function openMessages(): void {
  cy.intercept('GET', '**/api/Messaging/threads').as('threadsRequest');
  cy.navTo('nav-messages');
  cy.location('pathname').should('eq', '/messages');
  cy.wait('@threadsRequest').its('response.statusCode').should('eq', 200);
  cy.byTestId('inbox').should('be.visible');
}

function openCompose(): void {
  cy.byTestId('inbox-empty-compose-btn').click();
  cy.location('pathname').should('eq', '/messages/new');
  cy.byTestId('compose').should('be.visible');
}

function convertKitchenAndCopyInviteCode(householdName: string): Cypress.Chainable<string> {
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

function registerAndJoinWithCode(journey: string, fullName: string, code: string): Cypress.Chainable<TestCredentials> {
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

    cy.byTestId('plan-tabs').should('be.visible');
    cy.byTestId('onboarding-skip-btn').click();
    cy.wait('@completeOnboardingRequest').its('response.statusCode').should('be.within', 200, 299);
    cy.location('pathname').should('eq', '/home');
    return cy.wrap(credentials, { log: false });
  });
}

viewports.forEach((viewport) => {
  describe(`Household messages (${viewport.name} ${viewport.width}x${viewport.height})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('sends a message to another household member, who sees the thread', () => {
      const senderName = `E2E Sender ${viewport.name}`;
      const recipientName = `E2E Recipient ${viewport.name}`;
      const message = `Tacos tonight? ${Date.now().toString(36)}`;

      cy.registerAndLogin(`messages-sender-${viewport.name}`, { fullName: senderName }).then((sender) => {
        openMessages();
        openCompose();
        cy.byTestId('compose-empty').should('be.visible');
        cy.byTestId('compose-empty-household-btn').should('be.visible');
        cy.byTestId('compose-form').should('not.exist');

        convertKitchenAndCopyInviteCode(`E2E Messages ${viewport.name}`).then((code) => {
          cy.signOut();

          registerAndJoinWithCode(`messages-recipient-${viewport.name}`, recipientName, code).then((recipient) => {
            cy.signOut();

            cy.loginAs(sender.email, DEFAULT_TEST_PASSWORD);
            openMessages();
            openCompose();
            cy.byTestId('compose-empty').should('not.exist');
            cy.byTestId('compose-recipients-select').click();
            cy.byTestId('compose-recipient-option').should('have.length', 1).and('contain.text', recipientName).click();
            cy.focused().type('{esc}');
            cy.byTestId('compose-recipient-option').should('not.exist');
            cy.byTestId('compose-recipients-select').should('contain.text', recipientName).and('not.contain.text', senderName);

            cy.intercept('POST', '**/api/Messaging/threads').as('createThreadRequest');
            cy.byTestId('compose-message-input').type(message).should('have.value', message);
            cy.byTestId('compose-send-btn').click();
            cy.wait('@createThreadRequest').then(({ request, response }) => {
              expect(response?.statusCode).to.eq(201);
              expect(request.body.participantIds).to.have.length(1);
              expect(request.body.initialMessage).to.eq(message);
              cy.location('pathname').should('eq', `/messages/${response?.body.threadId}`);
            });
            cy.byTestId('compose-error').should('not.exist');
            cy.byTestId('thread').should('be.visible');
            cy.byTestId('thread-message').should('have.length', 1).and('contain.text', message);
            cy.signOut();

            cy.loginAs(recipient.email, DEFAULT_TEST_PASSWORD);
            openMessages();
            cy.byTestId('inbox-thread').should('have.length', 1)
              .and('contain.text', senderName)
              .and('contain.text', message)
              .click();
            cy.byTestId('thread').should('be.visible');
            cy.byTestId('thread-message').should('have.length', 1)
              .and('contain.text', message)
              .and('contain.text', senderName);
            cy.signOut();
          });
        });
      });
    });
  });
});
