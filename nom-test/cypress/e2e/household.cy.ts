import { selectedViewports } from '../support/viewports';

const viewports = selectedViewports();

const MEMBER_NAME = 'E2E Household Member';
const RESTRICTION_NAME = 'Vegetarian';

const memberDialog = () => cy.byTestId('add-member-dialog').parents('mat-dialog-container');

function waitForDialogFocus(): void {
  cy.focused().should(($focused) => {
    expect($focused.closest('mat-dialog-container')).to.have.length(1);
  });
}

function openHouseholdPage(phone: boolean): void {
  cy.intercept('GET', '**/api/Household/*').as('householdLoadRequest');
  if (phone) {
    cy.byTestId('header-nav-toggle-btn').should('be.visible');
    cy.byTestId('nav-household').should('not.be.visible');
  } else {
    cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
    cy.byTestId('nav-household').should('be.visible');
  }
  cy.navTo('nav-household');
  cy.location('pathname').should('eq', '/household');
  cy.wait('@householdLoadRequest').its('response.statusCode').should('eq', 200);
  cy.byTestId('household').should('be.visible');
}

function convertPersonalKitchenToHousehold(householdName: string): void {
  cy.intercept('POST', '**/api/Household/*/convert').as('convertRequest');
  cy.intercept('POST', '**/api/Household/invite-token').as('inviteTokenRequest');
  cy.intercept('GET', '**/api/Household/*').as('householdAfterConvertRequest');

  cy.byTestId('household-personal-badge').should('be.visible');
  cy.byTestId('household-member-grid').should('not.exist');
  cy.byTestId('household-personal-section').within(() => {
    cy.byTestId('household-invite-btn').click();
  });

  cy.byTestId('household-convert-dialog').should('be.visible');
  cy.byTestId('household-convert-name-input').should('be.visible');
  waitForDialogFocus();
  cy.byTestId('household-convert-create-btn').click();
  cy.byTestId('household-convert-name-error').should('be.visible');
  cy.byTestId('household-convert-dialog').should('be.visible');
  cy.get('@convertRequest.all').should('have.length', 0);

  cy.byTestId('household-convert-name-input').type(householdName).should('have.value', householdName);
  cy.byTestId('household-convert-name-error').should('not.exist');
  cy.byTestId('household-convert-create-btn').click();
  cy.wait('@convertRequest').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('household-convert-dialog').should('not.exist');

  cy.byTestId('invite-dialog').should('be.visible');
  cy.wait('@inviteTokenRequest').its('response.statusCode').should('be.within', 200, 299);
  cy.byTestId('invite-dialog-code').invoke('text').invoke('trim').should('not.be.empty');
  cy.byTestId('invite-dialog-done-btn').click();
  cy.byTestId('invite-dialog').should('not.exist');

  cy.wait('@householdAfterConvertRequest');
  cy.byTestId('household-personal-badge').should('not.exist');
  cy.byTestId('household-member-grid').scrollIntoView().should('be.visible');
  cy.byTestId('household-add-person-btn').should('be.visible');
  cy.byTestId('household-member-row').should('not.exist');
}

function addMemberThroughDialog(phone: boolean): void {
  cy.intercept('PUT', '**/api/Person/*/profile').as('saveMemberProfileRequest');
  cy.intercept('PUT', '**/api/Person/*/restrictions').as('saveMemberRestrictionsRequest');
  cy.intercept('GET', '**/api/Household/*').as('householdAfterMemberRequest');

  cy.byTestId('household-add-person-btn').click();
  memberDialog().should('be.visible');
  if (phone) {
    memberDialog().find('mat-dialog-content').should('have.css', 'overflow-y', 'auto');
  }

  waitForDialogFocus();
  memberDialog().within(() => {
    cy.byTestId('profile-form').should('exist');
    cy.byTestId('profile-name-input').should('be.visible').clear().type(MEMBER_NAME).should('have.value', MEMBER_NAME);
    cy.byTestId('profile-unit-system-imperial').click();
    cy.byTestId('profile-height-feet-input').clear().type('5');
    cy.byTestId('profile-height-inches-input').clear().type('12');
    cy.byTestId('profile-weight-lbs-input').clear().type('150');
    cy.byTestId('add-member-save-btn').click();
    cy.byTestId('profile-height-inches-error').scrollIntoView().should('be.visible');
    cy.byTestId('profile-form').should('exist');
    cy.byTestId('add-member-save-btn').should('be.visible');
    cy.byTestId('restrictions').should('not.exist');
  });
  cy.get('@saveMemberProfileRequest.all').should('have.length', 0);

  memberDialog().within(() => {
    cy.byTestId('profile-height-inches-input').clear().type('10');
    cy.byTestId('profile-height-inches-error').should('not.exist');
    cy.byTestId('add-member-save-btn').click();
  });
  cy.wait('@saveMemberProfileRequest').then(({ request, response }) => {
    expect(response?.statusCode).to.be.within(200, 299);
    expect(request.body.name).to.eq(MEMBER_NAME);
  });

  memberDialog().within(() => {
    cy.byTestId('profile-form').should('not.exist');
    cy.byTestId('restrictions').should('exist');
    cy.byTestId('restrictions-search-input').first().scrollIntoView().should('be.visible').click().type(RESTRICTION_NAME);
  });
  cy.byTestId('restrictions-option').contains('span', new RegExp(`^${RESTRICTION_NAME}$`)).click();
  memberDialog().within(() => {
    cy.byTestId('restrictions-chip').should('have.length', 1).and('contain.text', RESTRICTION_NAME);
    cy.byTestId('add-member-continue-btn').click();
  });
  cy.wait('@saveMemberRestrictionsRequest').then(({ request, response }) => {
    expect(response?.statusCode).to.be.within(200, 299);
    expect(request.body).to.have.length(1);
  });
  cy.byTestId('add-member-dialog').should('not.exist');

  cy.wait('@householdAfterMemberRequest').its('response.body.members').should('have.length', 2);
  cy.byTestId('household-member-row').should('have.length', 1).and('contain.text', MEMBER_NAME);
  cy.byTestId('household-member-row').within(() => {
    cy.byTestId('household-member-profile-btn').should('have.class', 'nom-household__status-badge--done');
    cy.byTestId('household-member-dietary-btn').should('have.class', 'nom-household__status-badge--done');
  });
}

viewports.forEach((viewport) => {
  describe(`Household journey (${viewport.name} ${viewport.width}x${viewport.height})`, () => {
    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
    });

    it('turns the personal kitchen into a household and the dashboard stops asking about one', () => {
      cy.registerAndLogin(`household-convert-${viewport.name}`, { fullName: 'E2E Household Owner' });
      cy.byTestId('dashboard').should('be.visible');
      cy.byTestId('dashboard-empty').should('not.exist');

      openHouseholdPage(viewport.phone);
      convertPersonalKitchenToHousehold(`E2E Kitchen ${viewport.name}`);

      cy.byTestId('household-primary-profile-link').should('be.visible');
      cy.byTestId('household-primary-dietary-link').should('be.visible');
      cy.byTestId('household-dietary-scope-toggle').scrollIntoView().should('be.visible');
      cy.byTestId('household-macro-goals').should('exist');
      cy.byTestId('household-food-group-rules').should('exist');

      cy.intercept('GET', '**/api/Household').as('householdsRequest');
      cy.visit('/home');
      cy.wait('@householdsRequest').its('response.body').should('have.length', 1);
      cy.location('pathname').should('eq', '/home');
      cy.byTestId('dashboard').should('be.visible');
      cy.byTestId('dashboard-empty').should('not.exist');
      cy.byTestId('no-household-cta').should('not.exist');
      cy.byTestId('onboarding-household-fork').should('not.exist');
      cy.byTestId('dashboard-shuffle-btn').should('be.visible');

      cy.signOut();
    });

    it('focuses the household name when the convert dialog opens, so typing straight away names it', () => {
      cy.registerAndLogin(`household-focus-${viewport.name}`, { fullName: 'E2E Household Owner' });
      openHouseholdPage(viewport.phone);
      cy.byTestId('household-personal-section').within(() => {
        cy.byTestId('household-invite-btn').click();
      });
      cy.byTestId('household-convert-dialog').should('be.visible');
      waitForDialogFocus();
      cy.focused().should('have.attr', 'data-testid', 'household-convert-name-input');
      cy.focused().type('Kitchen ');
      cy.byTestId('household-convert-dialog').should('be.visible');
      cy.byTestId('household-convert-name-input').should('have.value', 'Kitchen ');
      cy.byTestId('household-convert-close-btn').click();
      cy.byTestId('household-convert-dialog').should('not.exist');

      cy.signOut();
    });

    it('adds a member through the dialog, sets their restriction and removes them from the roster', () => {
      cy.registerAndLogin(`household-member-${viewport.name}`, { fullName: 'E2E Household Owner' });
      openHouseholdPage(viewport.phone);
      convertPersonalKitchenToHousehold(`E2E Family ${viewport.name}`);

      addMemberThroughDialog(viewport.phone);

      cy.byTestId('household-member-row').should('have.css', 'flex-wrap', viewport.phone ? 'wrap' : 'nowrap');

      cy.intercept('DELETE', '**/api/Household/*/member/*').as('removeMemberRequest');
      cy.intercept('GET', '**/api/Household/*').as('householdAfterRemoveRequest');
      cy.byTestId('household-member-row').within(() => {
        cy.byTestId('household-member-remove-btn').click();
      });
      cy.wait('@removeMemberRequest').its('response.statusCode').should('be.within', 200, 299);
      cy.wait('@householdAfterRemoveRequest');
      cy.byTestId('household-member-row').should('not.exist');
      cy.byTestId('household-member-grid').scrollIntoView().should('be.visible');
      cy.byTestId('household-add-person-btn').should('be.visible');

      cy.signOut();
    });

    it('reopens a member\'s profile and dietary preferences from the roster and shows their restrictions to lock', () => {
      cy.registerAndLogin(`household-member-edit-${viewport.name}`, { fullName: 'E2E Household Owner' });
      openHouseholdPage(viewport.phone);
      convertPersonalKitchenToHousehold(`E2E Edit ${viewport.name}`);
      addMemberThroughDialog(viewport.phone);

      cy.intercept('GET', '**/api/Person/*/onboarding').as('memberStateRequest');
      cy.byTestId('household-member-row').within(() => {
        cy.byTestId('household-member-profile-btn').click();
      });
      cy.wait('@memberStateRequest').its('response.statusCode').should('eq', 200);
      memberDialog().within(() => {
        cy.byTestId('add-member-error').should('not.exist');
        cy.byTestId('restrictions').should('not.exist');
        cy.byTestId('profile-name-input').should('have.value', MEMBER_NAME);
        cy.byTestId('add-member-close-btn').click();
      });
      cy.byTestId('add-member-dialog').should('not.exist');

      cy.byTestId('household-member-row').within(() => {
        cy.byTestId('household-member-dietary-btn').click();
      });
      cy.wait('@memberStateRequest').its('response.statusCode').should('eq', 200);
      memberDialog().within(() => {
        cy.byTestId('add-member-error').should('not.exist');
        cy.byTestId('profile-form').should('not.exist');
        cy.byTestId('restrictions').should('exist');
        cy.byTestId('restrictions-chip').should('have.length', 1).and('contain.text', RESTRICTION_NAME);
        cy.byTestId('add-member-close-btn').click();
      });
      cy.byTestId('add-member-dialog').should('not.exist');

      cy.byTestId('household-member-policies').scrollIntoView().should('be.visible');
      cy.byTestId('member-policy-member-select').click();
      cy.byTestId('member-policy-member-option').contains(MEMBER_NAME).click();
      cy.wait('@memberStateRequest').its('response.statusCode').should('eq', 200);
      cy.byTestId('member-policy-panel').within(() => {
        cy.byTestId('member-policy-restrictions-error').should('not.exist');
        cy.byTestId('member-policy-restrictions-empty').should('not.exist');
        cy.byTestId('member-policy-restriction-item').should('have.length', 1).and('contain.text', RESTRICTION_NAME);
        cy.byTestId('member-policy-lock-btn').should('be.visible');
      });

      cy.signOut();
    });

    it('validates food-group requirements and saves household macro goals', () => {
      cy.registerAndLogin(`household-rules-${viewport.name}`, { fullName: 'E2E Household Owner' });
      openHouseholdPage(viewport.phone);
      convertPersonalKitchenToHousehold(`E2E Rules ${viewport.name}`);

      cy.intercept('PUT', '**/api/MealPlan/food-group-rule').as('upsertRuleRequest');
      cy.intercept('DELETE', '**/api/MealPlan/food-group-rule/*').as('deleteRuleRequest');
      cy.intercept('GET', '**/api/MealPlan/household/*/food-group-rules').as('rulesRequest');

      cy.byTestId('household-food-group-rules').scrollIntoView();
      cy.byTestId('food-group-rules-empty').should('be.visible');
      cy.byTestId('food-group-rules-form').within(() => {
        cy.byTestId('food-group-rules-add-btn').click();
        cy.byTestId('food-group-rules-group-error').should('be.visible');
      });
      cy.get('@upsertRuleRequest.all').should('have.length', 0);
      cy.byTestId('food-group-rules-item').should('not.exist');

      cy.byTestId('food-group-rules-group').click();
      cy.byTestId('food-group-rules-group-option').first().invoke('text').then((text) => {
        cy.wrap(text.trim(), { log: false }).as('groupName');
      });
      cy.byTestId('food-group-rules-group-option').first().click();
      cy.byTestId('food-group-rules-form').within(() => {
        cy.byTestId('food-group-rules-group-error').should('not.exist');
        cy.byTestId('food-group-rules-servings').clear().type('3');
        cy.byTestId('food-group-rules-add-btn').click();
      });
      cy.wait('@upsertRuleRequest').then(({ request, response }) => {
        expect(response?.statusCode).to.be.within(200, 299);
        expect(request.body.minServings).to.eq(3);
        expect(request.body.timeframe).to.eq('PerDay');
      });
      cy.wait('@rulesRequest');
      cy.get<string>('@groupName').then((groupName) => {
        cy.byTestId('food-group-rules-item').should('have.length', 1)
          .and('contain.text', '3')
          .and('contain.text', groupName)
          .and('contain.text', 'per day');
      });
      cy.byTestId('food-group-rules-empty').should('not.exist');

      cy.byTestId('food-group-rules-item').within(() => {
        cy.byTestId('food-group-rules-remove-btn').click();
      });
      cy.wait('@deleteRuleRequest').its('response.statusCode').should('be.within', 200, 299);
      cy.byTestId('food-group-rules-item').should('not.exist');
      cy.byTestId('food-group-rules-empty').should('be.visible');

      cy.intercept('PUT', '**/api/Household/*/macro-goals').as('saveHouseholdGoalRequest');
      cy.intercept('GET', '**/api/Household/*/macro-goals').as('householdGoalRequest');
      cy.byTestId('household-macro-goals').scrollIntoView();
      cy.byTestId('macro-goal-form').within(() => {
        cy.byTestId('macro-goal-calories').clear().type('2000');
        cy.byTestId('macro-goal-protein').clear().type('120');
        cy.byTestId('macro-goal-carbs').clear().type('220');
        cy.byTestId('macro-goal-fat').clear().type('70');
        cy.byTestId('macro-goal-calorie-hint').should('not.exist');
        cy.byTestId('macro-goal-save-btn').click();
      });
      cy.wait('@saveHouseholdGoalRequest').then(({ request, response }) => {
        expect(response?.statusCode).to.be.within(200, 299);
        expect(request.body.caloriesTarget).to.eq(2000);
        expect(request.body.proteinGramsTarget).to.eq(120);
        expect(response?.body.caloriesTarget).to.eq(2000);
      });
      cy.byTestId('macro-goal-success').scrollIntoView().should('be.visible');
      cy.byTestId('macro-goal-error').should('not.exist');

      cy.reload();
      cy.wait('@householdGoalRequest').its('response.body.caloriesTarget').should('eq', 2000);
      cy.byTestId('household-macro-goals').scrollIntoView();
      cy.byTestId('macro-goal-calories').should('have.value', '2000');
      cy.byTestId('macro-goal-protein').should('have.value', '120');

      cy.signOut();
    });
  });
});
