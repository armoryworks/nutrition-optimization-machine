import { DEFAULT_TEST_PASSWORD } from '../support/commands';
import { selectedViewports } from '../support/viewports';

const requestResetLink = (email: string) => {
  cy.intercept('POST', '**/api/auth/forgotPassword').as('forgotPassword');
  cy.byTestId('forgot-password-form').should('be.visible');
  cy.byTestId('forgot-password-email-input').type(email);
  cy.byTestId('forgot-password-submit-btn').click();
  return cy.wait('@forgotPassword').then(({ response }) => {
    cy.byTestId('forgot-password-sent').should('be.visible').and('contain.text', 'password reset link');
    return cy.wrap({ status: response?.statusCode, body: JSON.stringify(response?.body ?? null) }, { log: false });
  });
};

const passwordResetJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  describe(`Password reset (${viewportName})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
      cy.clearCookies();
      cy.clearLocalStorage();
    });

    it('asks for a reset link from Sign In and answers a registered and an unknown address the same way', () => {
      cy.uniqueEmail('password reset').then((email) => {
        cy.registerViaUi(email, DEFAULT_TEST_PASSWORD);

        cy.visit('/home');
        cy.byTestId('header-login-btn').click();
        cy.byTestId('login-popover').within(() => {
          cy.byTestId('login-forgot-password-link').click();
        });
        cy.location('pathname').should('eq', '/forgot-password');

        requestResetLink(email).then((registered) => {
          cy.visit('/forgot-password');
          cy.uniqueEmail('nobody').then((unknown) => {
            requestResetLink(unknown).then((unregistered) => {
              expect(unregistered.status, 'status for an unknown address').to.eq(registered.status);
              expect(unregistered.body, 'body for an unknown address').to.eq(registered.body);
            });
          });
        });

        cy.byTestId('forgot-password-sign-in-link').click();
        cy.location('pathname').should('eq', '/login');
      });
    });

    it('explains an expired or used reset link and offers a new one', () => {
      cy.intercept('POST', '**/api/auth/resetPassword').as('resetPassword');
      cy.visit('/reset-password?email=e2e.expired%40example.com&code=bm90LWEtcmVhbC1jb2Rl');

      cy.byTestId('reset-password-form').should('be.visible');
      cy.location('search').should('eq', '');
      cy.byTestId('reset-password-new-password-input').type(DEFAULT_TEST_PASSWORD, { log: false });
      cy.byTestId('reset-password-confirm-password-input').type(DEFAULT_TEST_PASSWORD, { log: false });
      cy.byTestId('reset-password-submit-btn').click();

      cy.wait('@resetPassword').then(({ request, response }) => {
        expect(request.body).to.deep.equal({
          email: 'e2e.expired@example.com',
          resetCode: 'bm90LWEtcmVhbC1jb2Rl',
          newPassword: DEFAULT_TEST_PASSWORD,
        });
        expect(response?.statusCode).to.eq(400);
      });
      cy.byTestId('reset-password-link-expired').should('be.visible')
        .and('contain.text', 'Link Expired')
        .and('contain.text', 'expired or has already been used');
      cy.byTestId('reset-password').should('not.contain.text', 'Choose a new password');
      cy.byTestId('reset-password-form').should('not.exist');
      cy.byTestId('reset-password-request-new-link-btn').click();
      cy.location('pathname').should('eq', '/forgot-password');
      cy.byTestId('forgot-password-form').should('be.visible');
    });

    it('checks the password rules under New Password before sending anything', () => {
      cy.intercept('POST', '**/api/auth/resetPassword').as('resetPassword');
      cy.visit('/reset-password?email=e2e.rules%40example.com&code=bm90LWEtcmVhbC1jb2Rl');

      cy.byTestId('reset-password-password-rules').should('be.visible');
      cy.byTestId('reset-password-new-password-input').type('longpassword', { log: false });
      cy.byTestId('reset-password-password-rule-length').should('have.class', 'nom-reset__rule--met');
      cy.byTestId('reset-password-password-rule-upper').should('not.have.class', 'nom-reset__rule--met');
      cy.byTestId('reset-password-password-rule-symbol').should('not.have.class', 'nom-reset__rule--met');
      cy.byTestId('reset-password-confirm-password-input').type('longpassword', { log: false });
      cy.byTestId('reset-password-submit-btn').click();

      cy.byTestId('reset-password-new-password-error').should('be.visible').and('contain.text', 'rules below');
      cy.byTestId('reset-password-error').should('not.exist');
      cy.get('@resetPassword.all').should('have.length', 0);
    });

    it('shows the invalid-link state when the reset code is missing', () => {
      cy.visit('/reset-password?email=e2e.missing%40example.com');

      cy.byTestId('reset-password-invalid-link').should('be.visible').and('contain.text', 'Invalid Reset Link');
      cy.byTestId('reset-password-form').should('not.exist');
      cy.byTestId('reset-password-request-link-btn').click();
      cy.location('pathname').should('eq', '/forgot-password');
    });
  });
};

selectedViewports().forEach((viewport) => passwordResetJourney(viewport.name, viewport));
