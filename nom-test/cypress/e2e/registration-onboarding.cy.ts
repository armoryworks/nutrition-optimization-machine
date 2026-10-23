import { selectedViewports } from '../support/viewports';

const PASSWORD = 'TestPassword123!';
const PASSWORD_RULE_KEYS = ['length', 'upper', 'lower', 'digit', 'symbol'];
const MET_CLASS = 'nom-register__rule--met';

const VIEWPORTS = selectedViewports();

const PROFILE_SAVE = '**/api/Person/*/profile';
const RESTRICTIONS_SAVE = '**/api/Person/*/restrictions';
const ONBOARDING_COMPLETE = '**/api/Person/*/onboarding';

VIEWPORTS.forEach((viewport) => {
  const isPhone = viewport.name === 'phone';

  describe(`Registration and onboarding (${viewport.name})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('ticks password rules off, explains a mismatch, creates the account, and refuses a duplicate email', () => {
      cy.intercept('POST', '**/api/auth/register-custom').as('registerRequest');

      cy.uniqueEmail('registration').then((email) => {
        cy.visit('/register');
        cy.byTestId('register-form').should('be.visible');
        cy.byTestId('register-email-input').type(email);
        cy.byTestId('register-full-name-input').type('E2E Registration');

        cy.byTestId('register-password-input').type('abc');
        cy.byTestId('register-password-rule-lower').should('have.class', MET_CLASS);
        cy.byTestId('register-password-rule-length').should('not.have.class', MET_CLASS);
        cy.byTestId('register-password-rule-upper').should('not.have.class', MET_CLASS);
        cy.byTestId('register-password-rule-digit').should('not.have.class', MET_CLASS);
        cy.byTestId('register-password-rule-symbol').should('not.have.class', MET_CLASS);

        cy.byTestId('register-password-input').clear().type(PASSWORD, { log: false });
        PASSWORD_RULE_KEYS.forEach((key) => {
          cy.byTestId(`register-password-rule-${key}`).should('have.class', MET_CLASS);
        });

        cy.byTestId('register-confirm-password-input').type('Different123!', { log: false }).blur();
        cy.byTestId('register-confirm-password-error').should('be.visible').and('contain.text', 'Passwords do not match');
        cy.byTestId('register-submit-btn').click();
        cy.byTestId('register-confirm-password-error').should('be.visible');
        cy.get('@registerRequest.all').should('have.length', 0);

        cy.byTestId('register-confirm-password-input').clear().type(PASSWORD, { log: false });
        cy.byTestId('register-confirm-password-error').should('not.exist');
        cy.byTestId('register-submit-btn').click();
        cy.wait('@registerRequest').its('response.statusCode').should('eq', 200);
        cy.byTestId('register-form').should('not.exist');
        cy.byTestId('register-resend-btn').should('be.visible');
        cy.byTestId('register-go-sign-in-link').should('be.visible');

        cy.visit('/register');
        cy.byTestId('register-email-input').type(email);
        cy.byTestId('register-password-input').type(PASSWORD, { log: false });
        cy.byTestId('register-confirm-password-input').type(PASSWORD, { log: false });
        cy.byTestId('register-submit-btn').click();
        cy.wait('@registerRequest').its('response.statusCode').should('be.within', 400, 409);
        cy.byTestId('register-error').scrollIntoView().should('be.visible').and('contain.text', 'already exists');
        cy.byTestId('register-form').should('be.visible');
      });
    });

    it('rejects bad profile input without saving, then onboards solo with a plan and shows the saved profile after signing back in', () => {
      const fullName = 'E2E Onboarding';
      const planName = `E2E Plan ${Date.now().toString(36)}`;

      cy.intercept('PUT', PROFILE_SAVE).as('saveProfileRequest');
      cy.intercept('PUT', RESTRICTIONS_SAVE).as('saveRestrictionsRequest');
      cy.intercept('POST', ONBOARDING_COMPLETE).as('completeOnboardingRequest');
      cy.intercept('GET', ONBOARDING_COMPLETE).as('onboardingStateRequest');

      cy.registerAndLogin('onboarding', { fullName, onboarding: 'none' }).then((credentials) => {
        cy.location('pathname').should('eq', '/onboarding');
        cy.byTestId('onboarding-stepper').should('be.visible');
        cy.byTestId('onboarding-step-1').should('have.attr', 'aria-current', 'step');
        cy.byTestId('profile-form').should('be.visible');

        if (isPhone) {
          cy.byTestId('header-nav-toggle-btn').should('be.visible');
          cy.byTestId('header-search-input').should('not.be.visible');
          cy.byTestId('onboarding-step-1').find('[data-testid="onboarding-step-label"]').should('not.be.visible');
        } else {
          cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
          cy.byTestId('header-search-input').should('be.visible');
          cy.byTestId('onboarding-step-1').find('[data-testid="onboarding-step-label"]').should('be.visible');
        }

        cy.byTestId('profile-name-input').should('have.value', fullName);
        cy.byTestId('profile-unit-system-imperial').click();
        cy.byTestId('profile-height-feet-input').should('be.visible');

        cy.byTestId('profile-dob-input').type('22/10/1980').blur();
        cy.byTestId('profile-height-inches-input').type('12').blur();
        cy.byTestId('profile-name-input').clear().blur();
        cy.byTestId('profile-submit-btn').click();

        cy.focused().should('have.attr', 'data-testid', 'profile-name-input');
        cy.byTestId('profile-name-error').should('be.visible').and('contain.text', 'Name is required');
        if (isPhone) {
          cy.byTestId('profile-name-input').should(($input) => {
            const rect = $input[0].getBoundingClientRect();
            expect(rect.top, 'name field below the top edge').to.be.at.least(0);
            expect(rect.top, 'name field in the top quarter').to.be.at.most(viewport.height / 4 + rect.height);
          });
        }
        cy.byTestId('profile-dob-error').scrollIntoView().should('be.visible').and('contain.text', 'MM/DD/YYYY');
        cy.byTestId('profile-height-inches-error').scrollIntoView().should('be.visible').and('contain.text', 'Between 0 and 11');
        cy.byTestId('onboarding-step-invalid').scrollIntoView().should('be.visible').and('contain.text', 'Fix the highlighted fields');
        cy.byTestId('onboarding-step-invalid').should('have.css', 'text-align', isPhone ? 'left' : 'right');
        cy.byTestId('onboarding-step-1').should('have.attr', 'aria-current', 'step');
        cy.get('@saveProfileRequest.all').should('have.length', 0);

        cy.byTestId('profile-name-input').type(fullName);
        cy.byTestId('profile-name-error').should('not.exist');
        cy.byTestId('profile-dob-input').clear().type('10/22/1980').blur();
        cy.byTestId('profile-dob-error').should('not.exist');
        cy.byTestId('profile-height-feet-input').type('5');
        cy.byTestId('profile-height-inches-input').clear().type('10').blur();
        cy.byTestId('profile-height-inches-error').should('not.exist');
        cy.byTestId('profile-weight-lbs-input').type('180');
        cy.byTestId('profile-activity-level-select').click();
        cy.byTestId('profile-activity-level-option').contains('Moderately Active').click();
        cy.byTestId('profile-health-goal-select').click();
        cy.byTestId('profile-health-goal-option').first().click();
        cy.byTestId('profile-health-goal-option').should('not.exist');

        cy.byTestId('profile-submit-btn').click();
        cy.wait('@saveProfileRequest').then((interception) => {
          expect(interception.response.statusCode).to.be.within(200, 299);
          expect(interception.request.body.name).to.eq(fullName);
          const values = interception.request.body.attributes.map((a: { value: string }) => a.value);
          expect(values).to.include('177.8');
          expect(values).to.include('81.6');
          expect(values).to.include('1980-10-22');
          expect(values).to.include('imperial');
        });
        cy.byTestId('onboarding-step-invalid').should('not.exist');
        cy.byTestId('onboarding-step-2').should('have.attr', 'aria-current', 'step');

        cy.byTestId('restrictions').should('be.visible');
        cy.byTestId('restrictions').find('[data-testid="restrictions-search-input"]').first().type('vegetarian');
        cy.contains('[data-testid="restrictions-option-name"]', /^Vegetarian$/).click();
        cy.byTestId('restrictions-chip').should('have.length', 1).and('contain.text', 'Vegetarian');
        cy.byTestId('restrictions-submit-btn').click();
        cy.wait('@saveRestrictionsRequest').then((interception) => {
          expect(interception.response.statusCode).to.be.within(200, 299);
          expect(interception.request.body).to.have.length(1);
        });

        cy.byTestId('onboarding-step-3').should('have.attr', 'aria-current', 'step');
        cy.byTestId('onboarding-household-fork').should('be.visible');
        cy.byTestId('onboarding-solo-option').click();

        cy.byTestId('onboarding-step-4').should('have.attr', 'aria-current', 'step');
        cy.byTestId('plan-tabs').should('be.visible');
        cy.byTestId('onboarding-skip-btn').should('contain.text', 'Finish Later');
        cy.byTestId('plan-create-tab').click();
        cy.byTestId('plan-name-input').type(planName);
        cy.byTestId('plan-create-btn').click();
        cy.wait('@completeOnboardingRequest').then((interception) => {
          expect(interception.response.statusCode).to.be.within(200, 299);
          expect(interception.request.body.planName).to.eq(planName);
          expect(interception.request.body.createSoloHousehold).to.eq(true);
        });

        cy.location('pathname').should('eq', '/home');
        cy.byTestId('dashboard').should('be.visible');
        cy.byTestId('dashboard-empty').should('not.exist');
        cy.byTestId('no-household-cta').should('not.exist');
        cy.byTestId('onboarding-household-fork').should('not.exist');

        cy.visit('/onboarding');
        cy.wait('@onboardingStateRequest');
        cy.location('pathname').should('eq', '/home');
        cy.byTestId('dashboard').should('be.visible');

        cy.signOut();
        cy.loginAs(credentials.email, credentials.password);
        cy.location('pathname').should('eq', '/home');

        cy.visit('/profile');
        cy.wait('@onboardingStateRequest');
        cy.byTestId('profile-form').should('be.visible');
        cy.byTestId('profile-name-input').should('have.value', fullName);
        cy.byTestId('profile-unit-system-imperial').should('have.class', 'mat-button-toggle-checked');
        cy.byTestId('profile-height-feet-input').should('have.value', '5');
        cy.byTestId('profile-height-inches-input').should('have.value', '10');
        cy.byTestId('profile-weight-lbs-input').should('have.value', '180');
        cy.byTestId('profile-dob-input').should('have.value', '10/22/1980');
      });
    });

    it('lets a new user skip the profile step, move between steps from the stepper, and finish later', () => {
      cy.intercept('PUT', PROFILE_SAVE).as('saveProfileRequest');
      cy.intercept('PUT', RESTRICTIONS_SAVE).as('saveRestrictionsRequest');
      cy.intercept('POST', ONBOARDING_COMPLETE).as('completeOnboardingRequest');

      cy.registerAndLogin('onboarding-skip', { onboarding: 'none' }).then(() => {
        cy.byTestId('profile-form').should('be.visible');
        cy.byTestId('onboarding-back-btn').should('not.exist');
        cy.byTestId('onboarding-skip-btn').should('contain.text', 'Skip for Now').click();

        cy.byTestId('restrictions').should('be.visible');
        cy.byTestId('profile-form').should('not.exist');
        cy.byTestId('onboarding-step-2').should('have.attr', 'aria-current', 'step');
        cy.byTestId('onboarding-back-btn').scrollIntoView().should('be.visible');
        cy.get('@saveProfileRequest.all').should('have.length', 0);

        cy.byTestId('onboarding-step-1').click();
        cy.byTestId('onboarding-step-1').should('have.attr', 'aria-current', 'step');
        cy.byTestId('profile-form').should('be.visible');

        cy.byTestId('onboarding-step-3').click();
        cy.byTestId('onboarding-step-3').should('have.attr', 'aria-current', 'step');
        cy.byTestId('onboarding-household-fork').should('be.visible');
        cy.byTestId('onboarding-solo-option').should('be.visible');
        cy.byTestId('onboarding-household-option').should('be.visible');

        cy.byTestId('onboarding-step-4').click();
        cy.byTestId('onboarding-step-4').should('have.attr', 'aria-current', 'step');
        cy.byTestId('plan-tabs').should('be.visible');
        cy.byTestId('onboarding-skip-btn').should('contain.text', 'Finish Later');

        cy.byTestId('onboarding-back-btn').click();
        cy.byTestId('onboarding-step-3').should('have.attr', 'aria-current', 'step');
        cy.byTestId('onboarding-household-fork').should('be.visible');

        cy.byTestId('onboarding-step-4').click();
        cy.byTestId('onboarding-skip-btn').click();
        cy.wait('@completeOnboardingRequest').then((interception) => {
          expect(interception.response.statusCode).to.be.within(200, 299);
          expect(interception.request.body.createSoloHousehold).to.eq(false);
          expect(interception.request.body.planName).to.be.null;
        });
        cy.get('@saveProfileRequest.all').should('have.length', 0);
        cy.get('@saveRestrictionsRequest.all').should('have.length', 0);

        cy.location('pathname').should('eq', '/home');
        cy.byTestId('dashboard').should('be.visible');
        cy.byTestId('dashboard-empty').should('be.visible');
        cy.byTestId('no-household-cta').should('be.visible');
      });
    });
  });
});
