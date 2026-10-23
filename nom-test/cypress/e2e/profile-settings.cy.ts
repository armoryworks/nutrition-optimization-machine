import { selectedViewports } from '../support/viewports';

const VIEWPORTS = selectedViewports();

const SETTINGS_CARDS: { card: string; path: string; page: string }[] = [
  { card: 'settings-profile-card', path: '/profile', page: 'profile' },
  { card: 'settings-restrictions-card', path: '/restrictions', page: 'restrictions' },
  { card: 'settings-household-card', path: '/household', page: 'household' },
  { card: 'settings-plans-card', path: '/plan', page: 'plan-calendar' },
  { card: 'settings-security-card', path: '/settings/security', page: 'security-settings' },
  { card: 'settings-api-tokens-card', path: '/settings/api-tokens', page: 'settings-api-tokens' },
  { card: 'settings-privacy-card', path: '/settings/privacy', page: 'privacy-settings' },
];

const openSettings = (phone: boolean) => {
  cy.navTo('nav-settings');
  cy.location('pathname').should('eq', '/settings');
  cy.byTestId('settings').should('be.visible');
  cy.byTestId('settings-grid').should('be.visible');
  if (phone) {
    cy.byTestId('header-nav-toggle-btn').should('be.visible');
    cy.byTestId('header-search-input').should('not.be.visible');
  }
};

const openSettingsCard = (card: string, path: string) => {
  cy.byTestId(card).scrollIntoView().should('be.visible').click();
  cy.location('pathname').should('eq', path);
};

const expectInsideViewport = (testId: string, height: number) => {
  cy.byTestId(testId).should(($el) => {
    const rect = $el[0].getBoundingClientRect();
    expect(rect.top, `${testId} top`).to.be.at.least(0);
    expect(rect.bottom, `${testId} bottom`).to.be.at.most(height);
  });
};

VIEWPORTS.forEach((vp) => {
  describe(`Profile & settings journey (${vp.name} ${vp.width}x${vp.height})`, () => {
    beforeEach(() => {
      cy.viewport(vp.width, vp.height);
    });

    it('shows every settings card and each card opens its page', () => {
      cy.registerAndLogin('profile-settings-cards');
      openSettings(vp.phone);

      SETTINGS_CARDS.forEach(({ card }) => {
        cy.byTestId(card).scrollIntoView().should('be.visible');
      });
      cy.byTestId('settings-admin-card').should('not.exist');

      SETTINGS_CARDS.forEach(({ card, path, page }) => {
        openSettingsCard(card, path);
        cy.byTestId(page).should('exist');
        openSettings(vp.phone);
      });
    });

    it('converts measurements between metric and imperial, rejects an out-of-range weight, and saves and persists valid values', () => {
      cy.registerAndLogin('profile-settings-measurements').then((creds) => {
        cy.intercept('GET', '**/api/Person/*/onboarding').as('loadProfile');
        cy.intercept('PUT', '**/api/Person/*/profile').as('saveProfile');

        openSettings(vp.phone);
        openSettingsCard('settings-profile-card', '/profile');
        cy.wait('@loadProfile');
        cy.byTestId('profile-form').should('be.visible');
        cy.byTestId('profile-name-input').should('have.value', creds.fullName);

        cy.byTestId('profile-unit-system-metric').click();
        cy.byTestId('profile-height-cm-input').clear().type('182');
        cy.byTestId('profile-weight-kg-input').clear().type('80');

        cy.byTestId('profile-unit-system-imperial').click();
        cy.byTestId('profile-height-cm-input').should('not.exist');
        cy.byTestId('profile-height-feet-input').should('have.value', '6');
        cy.byTestId('profile-height-inches-input').should('have.value', '0');
        cy.byTestId('profile-weight-lbs-input').should('have.value', '176');

        cy.byTestId('profile-unit-system-metric').click();
        cy.byTestId('profile-height-feet-input').should('not.exist');
        cy.byTestId('profile-height-cm-input').should('have.value', '183');
        cy.byTestId('profile-weight-kg-input').should('have.value', '79.8');

        cy.byTestId('profile-unit-system-imperial').click();
        cy.byTestId('profile-weight-lbs-input').clear().type('2000');
        cy.byTestId('profile-submit-btn').click();
        cy.byTestId('profile-weight-lbs-error').should('be.visible');
        cy.byTestId('profile-error').should('exist').and('contain.text', 'Please fix the highlighted fields.');
        cy.byTestId('profile-success').should('not.exist');
        cy.focused().should('have.attr', 'data-testid', 'profile-weight-lbs-input');
        cy.get('@saveProfile.all').should('have.length', 0);
        if (vp.phone) {
          expectInsideViewport('profile-weight-lbs-input', vp.height);
          expectInsideViewport('profile-submit-btn', vp.height);
        }

        cy.byTestId('profile-weight-lbs-input').clear().type('176');
        cy.byTestId('profile-weight-lbs-error').should('not.exist');
        cy.byTestId('profile-submit-btn').click();
        cy.wait('@saveProfile').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('profile-success').should('exist').and('contain.text', 'Profile saved successfully.');
        cy.byTestId('profile-error').should('not.exist');
        cy.byTestId('profile-success').scrollIntoView().should('be.visible');

        cy.reload();
        cy.wait('@loadProfile');
        cy.byTestId('profile-form').should('be.visible');
        cy.byTestId('profile-name-input').should('have.value', creds.fullName);
        cy.byTestId('profile-height-feet-input').should('have.value', '6');
        cy.byTestId('profile-height-inches-input').should('have.value', '0');
        cy.byTestId('profile-weight-lbs-input').should('have.value', '176');
      });
    });

    it.skip('BUG: the Save Profile confirmation (same for Save Preferences on /restrictions) renders at the top of the form, off-screen from the sticky Save button, so the user who clicked Save sees no confirmation without scrolling', () => {
      cy.registerAndLogin('profile-settings-confirmation');
      cy.intercept('GET', '**/api/Person/*/onboarding').as('loadProfile');
      cy.intercept('PUT', '**/api/Person/*/profile').as('saveProfile');

      openSettings(vp.phone);
      openSettingsCard('settings-profile-card', '/profile');
      cy.wait('@loadProfile');
      cy.byTestId('profile-form').should('be.visible');
      cy.byTestId('profile-submit-btn').click({ scrollBehavior: 'bottom' });
      cy.wait('@saveProfile').its('response.statusCode').should('be.within', 200, 299);
      cy.byTestId('profile-success').should('be.visible');
      expectInsideViewport('profile-success', vp.height);
    });

    it('saves personal macro goals from the profile page and shows them again after reload', () => {
      cy.registerAndLogin('profile-settings-macros');
      cy.intercept('GET', '**/api/Person/*/macro-goals').as('loadGoals');
      cy.intercept('PUT', '**/api/Person/*/macro-goals').as('saveGoals');
      cy.intercept('PUT', '**/api/Person/*/profile').as('saveProfile');

      openSettings(vp.phone);
      openSettingsCard('settings-profile-card', '/profile');
      cy.wait('@loadGoals').its('response.statusCode').should('eq', 200);
      cy.byTestId('profile-macro-goals').scrollIntoView().should('be.visible');
      cy.byTestId('macro-goal-form').should('be.visible');

      cy.byTestId('macro-goal-calories').clear().type('2200');
      cy.byTestId('macro-goal-protein').clear().type('150');
      cy.byTestId('macro-goal-carbs').clear().type('250');
      cy.byTestId('macro-goal-fat').clear().type('200');
      cy.byTestId('macro-goal-calorie-hint').should('be.visible');
      cy.byTestId('macro-goal-fat').clear().type('70');
      cy.byTestId('macro-goal-calorie-hint').should('not.exist');

      cy.byTestId('macro-goal-save-btn').click();
      cy.wait('@saveGoals').its('response.statusCode').should('eq', 200);
      cy.byTestId('macro-goal-success').scrollIntoView().should('be.visible');
      cy.byTestId('macro-goal-error').should('not.exist');
      cy.get('@saveProfile.all').should('have.length', 0);

      cy.reload();
      cy.wait('@loadGoals');
      cy.byTestId('macro-goal-calories').should('have.value', '2200');
      cy.byTestId('macro-goal-protein').should('have.value', '150');
      cy.byTestId('macro-goal-carbs').should('have.value', '250');
      cy.byTestId('macro-goal-fat').should('have.value', '70');
    });

    it('adds a dietary restriction, keeps it after reload, then removes it', () => {
      cy.registerAndLogin('profile-settings-diet');
      cy.intercept('GET', '**/api/Person/*/onboarding').as('loadRestrictions');
      cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveRestrictions');

      openSettings(vp.phone);
      openSettingsCard('settings-restrictions-card', '/restrictions');
      cy.wait('@loadRestrictions');
      cy.byTestId('restrictions-section').should('have.length.at.least', 1);
      cy.byTestId('restrictions-chip').should('not.exist');

      cy.byTestId('restrictions-search-input').first().click();
      cy.byTestId('restrictions-option').first().find('[data-testid="restrictions-option-name"]').invoke('text').then((rawName) => {
        const name = rawName.trim();
        cy.byTestId('restrictions-option').first().click();
        cy.byTestId('restrictions-chip').should('have.length', 1).first().should('contain.text', name);
        cy.byTestId('restrictions-search-input').first().should('have.value', '');

        cy.byTestId('restrictions-submit-btn').click();
        cy.wait('@saveRestrictions').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('restrictions-success').should('exist').and('contain.text', 'Dietary preferences saved.');
        cy.byTestId('restrictions-error').should('not.exist');
        cy.byTestId('restrictions-success').scrollIntoView().should('be.visible');

        cy.reload();
        cy.wait('@loadRestrictions');
        cy.byTestId('restrictions-chip').should('have.length', 1).first().should('contain.text', name);

        cy.byTestId('restrictions-chip-remove-btn').first().click();
        cy.byTestId('restrictions-chip').should('not.exist');
        cy.byTestId('restrictions-submit-btn').click();
        cy.wait('@saveRestrictions').its('response.statusCode').should('be.within', 200, 299);
        cy.byTestId('restrictions-success').should('exist').and('contain.text', 'Dietary preferences saved.');

        cy.reload();
        cy.wait('@loadRestrictions');
        cy.byTestId('restrictions-section').should('have.length.at.least', 1);
        cy.byTestId('restrictions-chip').should('not.exist');
      });
    });

    it('opens the security and privacy pages from settings without changing anything', () => {
      cy.registerAndLogin('profile-settings-security');
      cy.intercept('GET', '**/api/auth/2fa/status').as('twoFactorStatus');

      openSettings(vp.phone);
      openSettingsCard('settings-security-card', '/settings/security');
      cy.wait('@twoFactorStatus').its('response.statusCode').should('eq', 200);
      cy.byTestId('security-settings').should('be.visible');
      cy.byTestId('security-setup-btn').should('be.visible');
      cy.byTestId('security-disable-btn').should('not.exist');
      cy.byTestId('security-back-link').click();
      cy.location('pathname').should('eq', '/settings');

      openSettingsCard('settings-privacy-card', '/settings/privacy');
      cy.byTestId('privacy-settings').should('be.visible');
      cy.byTestId('privacy-export-json-btn').should('be.visible');
      cy.byTestId('privacy-export-csv-btn').should('be.visible');
      cy.byTestId('privacy-delete-btn').scrollIntoView().should('be.visible');
      cy.byTestId('privacy-back-link').scrollIntoView().click();
      cy.location('pathname').should('eq', '/settings');
      cy.byTestId('settings-grid').should('be.visible');
    });
  });
});
