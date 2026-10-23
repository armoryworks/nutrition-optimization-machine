export interface TestCredentials {
  email: string;
  password: string;
  fullName: string;
}

export interface RegisterAndLoginOptions {
  password?: string;
  fullName?: string;
  onboarding?: 'solo' | 'none';
  planName?: string;
}

export interface OnboardingOptions {
  name?: string;
  planName?: string;
}

declare global {
  namespace Cypress {
    interface Chainable {
      byTestId(testId: string, options?: Partial<Loggable & Timeoutable & Withinable & Shadow>): Chainable<JQuery<HTMLElement>>;
      uniqueEmail(journey: string): Chainable<string>;
      registerViaUi(email: string, password: string, fullName?: string): Chainable<void>;
      loginAs(email: string, password: string): Chainable<void>;
      loginViaHeaderPopover(email: string, password: string): Chainable<void>;
      loginAsAdmin(): Chainable<void>;
      registerAndLogin(journey: string, options?: RegisterAndLoginOptions): Chainable<TestCredentials>;
      registerAdmin(journey: string): Chainable<TestCredentials>;
      grantAdminClaims(email: string): Chainable<void>;
      completeOnboardingSolo(options?: OnboardingOptions): Chainable<void>;
      signOut(): Chainable<void>;
      usePhoneViewport(): Chainable<void>;
      useDesktopViewport(): Chainable<void>;
      navTo(testId: string): Chainable<void>;
    }
  }
}

import { DESKTOP_VIEWPORT, PHONE_VIEWPORT } from './viewports';

export { DESKTOP_VIEWPORT, PHONE_VIEWPORT } from './viewports';

export const DEFAULT_TEST_PASSWORD = 'TestPassword123!';

const testId = (id: string) => `[data-testid="${id}"]`;

Cypress.Commands.add('byTestId', (id: string, options?) => {
  return cy.get(testId(id), options);
});

Cypress.Commands.add('uniqueEmail', (journey: string) => {
  const slug = journey.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  const random = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
  return cy.wrap(`e2e.${slug}.${random}@example.com`, { log: false });
});

Cypress.Commands.add('registerViaUi', (email: string, password: string, fullName?: string) => {
  cy.intercept('POST', '**/api/auth/register-custom').as('registerRequest');
  cy.visit('/register');
  cy.get(testId('register-form')).should('be.visible');
  cy.get(testId('register-email-input')).type(email);
  if (fullName) {
    cy.get(testId('register-full-name-input')).type(fullName);
  }
  cy.get(testId('register-password-input')).type(password, { log: false });
  cy.get(testId('register-confirm-password-input')).type(password, { log: false });
  cy.get(testId('register-submit-btn')).click();
  cy.wait('@registerRequest').its('response.statusCode').should('eq', 200);
  cy.get(testId('register-go-sign-in-link')).should('be.visible');
});

Cypress.Commands.add('loginAs', (email: string, password: string) => {
  cy.intercept('POST', '**/api/auth/login').as('loginRequest');
  cy.intercept('GET', '**/api/Person/me').as('personMeRequest');
  cy.intercept('GET', '**/api/Person/*/onboarding').as('onboardingStateRequest');
  cy.visit('/login');
  cy.get(testId('login')).should('be.visible');
  cy.get(testId('login-form')).within(() => {
    cy.get(testId('login-email-input')).type(email);
    cy.get(testId('login-password-input')).type(password, { log: false });
    cy.get(testId('login-submit-btn')).click();
  });
  cy.wait('@loginRequest').its('response.statusCode').should('eq', 200);
  cy.wait('@personMeRequest').its('response.statusCode').should('eq', 200);
  cy.wait('@onboardingStateRequest');
  cy.location('pathname').should('not.eq', '/login');
  cy.get(testId('header-avatar-btn')).should('be.visible');
});

Cypress.Commands.add('loginViaHeaderPopover', (email: string, password: string) => {
  cy.intercept('POST', '**/api/auth/login').as('loginRequest');
  cy.visit('/home');
  cy.get(testId('header-login-btn')).click();
  cy.get(testId('login-form')).within(() => {
    cy.get(testId('login-email-input')).type(email);
    cy.get(testId('login-password-input')).type(password, { log: false });
    cy.get(testId('login-submit-btn')).click();
  });
  cy.wait('@loginRequest').its('response.statusCode').should('eq', 200);
  cy.get(testId('header-avatar-btn')).should('be.visible');
});

Cypress.Commands.add('loginAsAdmin', () => {
  const email = Cypress.env('ADMIN_EMAIL') || 'admin@example.com';
  const password = Cypress.env('ADMIN_PASSWORD') || 'AdminPassword123!';
  cy.loginAs(email, password);
});

Cypress.Commands.add('registerAndLogin', (journey: string, options: RegisterAndLoginOptions = {}) => {
  const password = options.password ?? DEFAULT_TEST_PASSWORD;
  const fullName = options.fullName ?? `E2E ${journey}`;
  const onboarding = options.onboarding ?? 'solo';

  return cy.uniqueEmail(journey).then((email) => {
    cy.registerViaUi(email, password, fullName);
    cy.loginAs(email, password);
    cy.location('pathname').should('eq', '/onboarding');
    cy.get(testId('onboarding')).should('be.visible');
    if (onboarding === 'solo') {
      cy.completeOnboardingSolo({ name: fullName, planName: options.planName });
    }
    const credentials: TestCredentials = { email, password, fullName };
    return cy.wrap(credentials, { log: false });
  });
});

Cypress.Commands.add('grantAdminClaims', (email: string) => {
  cy.intercept('GET', '**/api/UserManagement/users').as('adminUsersRequest');
  cy.intercept('GET', '**/api/UserManagement/users/*/claims').as('adminUserClaimsRequest');
  cy.intercept('PUT', '**/api/UserManagement/claims').as('setClaimsRequest');
  cy.visit('/admin/users');
  cy.get(testId('users-admin')).should('be.visible');
  cy.wait('@adminUsersRequest').its('response.statusCode').should('eq', 200);
  cy.get(testId('users-admin-search-input')).type(email);
  cy.contains(testId('users-admin-item'), email).within(() => {
    cy.get(testId('users-admin-expand-btn')).click();
  });
  cy.wait('@adminUserClaimsRequest').its('response.statusCode').should('eq', 200);
  for (const toggle of ['users-admin-curation-toggle', 'users-admin-roles-toggle']) {
    cy.contains(testId('users-admin-item'), email).within(() => {
      cy.get(testId(toggle)).find('button[role="switch"]').should('have.attr', 'aria-checked', 'false').click();
    });
    cy.wait('@setClaimsRequest').its('response.statusCode').should('be.within', 200, 299);
    cy.contains(testId('users-admin-item'), email).within(() => {
      cy.get(testId(toggle)).find('button[role="switch"]').should('have.attr', 'aria-checked', 'true');
    });
  }
});

Cypress.Commands.add('registerAdmin', (journey: string) => {
  return cy.registerAndLogin(journey, { fullName: `E2E admin ${journey}` }).then((credentials) => {
    cy.signOut();
    cy.loginAsAdmin();
    cy.grantAdminClaims(credentials.email);
    cy.signOut();
    return cy.wrap(credentials, { log: false });
  });
});

Cypress.Commands.add('completeOnboardingSolo', (options: OnboardingOptions = {}) => {
  cy.intercept('PUT', '**/api/Person/*/profile').as('saveProfileRequest');
  cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveRestrictionsRequest');
  cy.intercept('POST', '**/api/Person/*/onboarding').as('completeOnboardingRequest');

  cy.get(testId('onboarding')).should('be.visible');
  cy.get(testId('profile-form')).should('be.visible');
  if (options.name) {
    cy.get(testId('profile-name-input')).clear().type(options.name);
  }
  cy.get(testId('profile-submit-btn')).click();
  cy.wait('@saveProfileRequest').its('response.statusCode').should('be.within', 200, 299);

  cy.get(testId('restrictions')).should('be.visible');
  cy.get(testId('restrictions-submit-btn')).click();
  cy.wait('@saveRestrictionsRequest').its('response.statusCode').should('be.within', 200, 299);

  cy.get(testId('onboarding-household-fork')).should('be.visible');
  cy.get(testId('onboarding-solo-option')).click();

  cy.get(testId('plan-tabs')).should('be.visible');
  if (options.planName) {
    cy.get(testId('plan-create-tab')).click();
    cy.get(testId('plan-name-input')).type(options.planName);
    cy.get(testId('plan-create-btn')).click();
  } else {
    cy.get(testId('onboarding-skip-btn')).click();
  }
  cy.wait('@completeOnboardingRequest').its('response.statusCode').should('be.within', 200, 299);
  cy.location('pathname').should('eq', '/home');
  cy.get(testId('header-avatar-btn')).should('be.visible');
});

Cypress.Commands.add('signOut', () => {
  cy.intercept('POST', '**/api/auth/logout').as('logoutRequest');
  cy.get(testId('header-avatar-btn')).click();
  cy.get(testId('nav-sign-out')).click();
  cy.get('@logoutRequest.all').should('have.length.at.least', 1);
  cy.get(testId('header-login-btn')).should('be.visible');
});

Cypress.Commands.add('usePhoneViewport', () => {
  cy.viewport(PHONE_VIEWPORT.width, PHONE_VIEWPORT.height);
});

Cypress.Commands.add('useDesktopViewport', () => {
  cy.viewport(DESKTOP_VIEWPORT.width, DESKTOP_VIEWPORT.height);
});

Cypress.Commands.add('navTo', (id: string) => {
  cy.get(testId(id)).should('exist');
  cy.get(testId('header-nav-toggle-btn')).then(($toggle) => {
    if ($toggle.css('display') === 'none') {
      return;
    }
    cy.get(testId('header-nav-toggle-btn')).scrollIntoView().should('be.visible');
    cy.get(testId('nav-drawer')).then(($drawer) => {
      if (!$drawer.hasClass('nom-layout__nav--open')) {
        cy.get(testId('header-nav-toggle-btn')).click();
      }
    });
    cy.get(testId('nav-drawer')).should('have.class', 'nom-layout__nav--open');
  });
  cy.get(testId(id)).should('be.visible').click();
});
