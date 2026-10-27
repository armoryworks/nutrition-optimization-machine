import { selectedViewports } from '../support/viewports';
import { DEFAULT_TEST_PASSWORD } from '../support/commands';
import { convertKitchenAndCopyInviteCode, registerAndJoinWithCode } from '../support/household';

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
