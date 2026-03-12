/**
 * NOM Screenshot Walkthrough — Comprehensive
 *
 * Two-actor walkthrough that exercises every page, dialog, and workflow in the
 * application, taking named screenshots at each step for visual review.
 *
 * Actors:
 *   User A  — regular user: registers, onboards, creates content, submits for curation
 *   User B  — admin user: reviews curation queue, approves / denies items
 *
 * Run:
 *   npm run test:screenshots            (headless)
 *   npm run test:screenshots:headed      (watch it run)
 *
 * Screenshots land in: nom-test/cypress/screenshots/screenshot-walkthrough.cy.ts/
 */

describe('Screenshot Walkthrough', () => {
  // ── Shared config ─────────────────────────────────────────────
  const PASSWORD = 'WalkthroughTest123!';
  const ts = Date.now();
  const userA = { email: `walkthrough-a-${ts}@example.com`, name: 'Alice Walkthrough' };
  const userB = { email: `walkthrough-b-${ts}@example.com`, name: 'Bob Admin' };
  const apiUrl = Cypress.env('apiUrl') || 'http://localhost:8080';

  /** Wait for Angular animations / data to settle before capturing. */
  const settle = (ms = 1500) => cy.wait(ms);

  /** Navigate via the user-menu dropdown. */
  const navigateViaMenu = (testId: string) => {
    cy.get('[data-testid="header-avatar-btn"]').click();
    settle(500);
    cy.get(`[data-testid="${testId}"]`).click();
    settle(2000);
  };

  /** Log in via the header popover. */
  const loginViaPopover = (email: string) => {
    cy.visit('/home');
    settle();
    cy.get('[data-testid="header-login-btn"]').click();
    settle(500);
    cy.get('[data-testid="login-popover"]').within(() => {
      cy.get('input[formcontrolname="email"]').type(email);
      cy.get('input[formcontrolname="password"]').type(PASSWORD);
      cy.get('[data-testid="login-submit-btn"]').click();
    });
    cy.get('[data-testid="header-avatar-btn"]', { timeout: 15000 }).should('be.visible');
    settle();
  };

  /** Sign out via the user menu. */
  const signOut = () => {
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="header-avatar-btn"]').length) {
        cy.get('[data-testid="header-avatar-btn"]').click();
        settle(500);
        cy.get('[data-testid="nav-sign-out"]').click();
        settle();
      }
    });
  };

  /** Register a new user account. */
  const registerUser = (email: string, name: string) => {
    cy.visit('/register');
    settle();
    cy.get('[data-testid="register"] input[formcontrolname="email"]').type(email);
    cy.get('[data-testid="register"] input[formcontrolname="fullName"]').type(name);
    cy.get('[data-testid="register"] input[formcontrolname="password"]').type(PASSWORD);
    cy.get('[data-testid="register"] input[formcontrolname="confirmPassword"]').type(PASSWORD);
    cy.get('[data-testid="register-submit-btn"]').click();
    settle(3000);
  };

  // ═════════════════════════════════════════════════════════════
  //  PART 1 — Public / unauthenticated pages
  // ═════════════════════════════════════════════════════════════

  it('01 - Landing page (logged out)', () => {
    cy.visit('/home');
    settle();
    cy.screenshot('01-landing-page', { capture: 'fullPage' });
  });

  it('02 - Registration form', () => {
    cy.visit('/register');
    settle();
    cy.screenshot('02-register-empty', { capture: 'fullPage' });

    cy.get('[data-testid="register"] input[formcontrolname="email"]').type(userA.email);
    cy.get('[data-testid="register"] input[formcontrolname="fullName"]').type(userA.name);
    cy.get('[data-testid="register"] input[formcontrolname="password"]').type(PASSWORD);
    cy.get('[data-testid="register"] input[formcontrolname="confirmPassword"]').type(PASSWORD);
    cy.screenshot('02-register-filled', { capture: 'fullPage' });

    cy.get('[data-testid="register-submit-btn"]').click();
    settle(3000);
    cy.screenshot('02-register-result', { capture: 'fullPage' });
  });

  it('03 - Forgot password', () => {
    cy.visit('/forgot-password');
    settle();
    cy.screenshot('03-forgot-password', { capture: 'fullPage' });

    cy.get('[data-testid="forgot-password"]').then(($el) => {
      if ($el.find('input[formcontrolname="email"]').length) {
        cy.get('[data-testid="forgot-password"] input[formcontrolname="email"]').type('demo@example.com');
        cy.screenshot('03-forgot-password-filled', { capture: 'fullPage' });
      }
    });
  });

  it('04 - Confirm email page', () => {
    cy.visit('/confirm-email');
    settle();
    cy.get('[data-testid="confirm-email"]').should('exist');
    cy.screenshot('04-confirm-email', { capture: 'fullPage' });
  });

  // ═════════════════════════════════════════════════════════════
  //  PART 2 — User A: full authenticated journey
  // ═════════════════════════════════════════════════════════════

  it('05–50 - User A walkthrough', () => {
    // ── Register User B (admin) first so they exist ──
    registerUser(userB.email, userB.name);

    // ── Register User A ──
    registerUser(userA.email, userA.name);

    // ── Login as User A ──
    loginViaPopover(userA.email);
    cy.screenshot('05-logged-in-landing', { capture: 'fullPage' });

    // ── User menu open state ──
    cy.get('[data-testid="header-avatar-btn"]').click();
    settle(500);
    cy.get('[data-testid="user-menu"]').should('be.visible');
    cy.screenshot('05-user-menu-open', { capture: 'fullPage' });
    // close menu by clicking elsewhere
    cy.get('body').click(10, 10);
    settle(300);

    // ────────────────────────────────────────────────────────────
    //  ONBOARDING — fill out all steps with real data
    // ────────────────────────────────────────────────────────────

    cy.visit('/onboarding');
    settle();
    cy.get('[data-testid="onboarding"]').should('exist');
    cy.screenshot('06-onboarding-step1-profile', { capture: 'fullPage' });

    // Fill profile
    cy.get('body').then(($b) => {
      if ($b.find('input[formcontrolname="name"]').length) {
        cy.get('input[formcontrolname="name"]').clear().type(userA.name);
      }
      if ($b.find('input[formcontrolname="dateOfBirth"]').length) {
        cy.get('input[formcontrolname="dateOfBirth"]').type('1990-06-15');
      }
      if ($b.find('mat-select[formcontrolname="gender"]').length) {
        cy.get('mat-select[formcontrolname="gender"]').click();
        settle(300);
        cy.get('mat-option').first().click();
        settle(300);
      }
      // Imperial height/weight if present
      if ($b.find('input[formcontrolname="heightFeet"]').length) {
        cy.get('input[formcontrolname="heightFeet"]').clear().type('5');
        cy.get('input[formcontrolname="heightInches"]').clear().type('9');
      }
      if ($b.find('input[formcontrolname="weightLbs"]').length) {
        cy.get('input[formcontrolname="weightLbs"]').clear().type('165');
      }
      // Metric fallback
      if ($b.find('input[formcontrolname="heightCm"]').length) {
        cy.get('input[formcontrolname="heightCm"]').clear().type('175');
      }
      if ($b.find('input[formcontrolname="weightKg"]').length) {
        cy.get('input[formcontrolname="weightKg"]').clear().type('75');
      }
      if ($b.find('mat-select[formcontrolname="activityLevel"]').length) {
        cy.get('mat-select[formcontrolname="activityLevel"]').click();
        settle(300);
        cy.get('mat-option').eq(1).click();
        settle(300);
      }
      if ($b.find('mat-select[formcontrolname="healthGoal"]').length) {
        cy.get('mat-select[formcontrolname="healthGoal"]').click();
        settle(300);
        cy.get('mat-option').first().click();
        settle(300);
      }
    });
    cy.screenshot('06-onboarding-profile-filled', { capture: 'fullPage' });

    // Advance to restrictions
    cy.get('body').then(($b) => {
      if ($b.find('button:contains("Continue"), button:contains("Next")').length) {
        cy.contains('button', /Continue|Next/).first().click();
        settle();
        cy.screenshot('07-onboarding-step2-restrictions', { capture: 'fullPage' });
      }
    });

    // Advance to household
    cy.get('body').then(($b) => {
      if ($b.find('button:contains("Continue"), button:contains("Next"), button:contains("Skip for Now")').length) {
        cy.contains('button', /Continue|Next|Skip for Now/).first().click();
        settle();
        cy.screenshot('08-onboarding-step3-household', { capture: 'fullPage' });
      }
    });

    // Advance to plan
    cy.get('body').then(($b) => {
      if ($b.find('button:contains("Continue"), button:contains("Next"), button:contains("Skip for Now")').length) {
        cy.contains('button', /Continue|Next|Skip for Now/).first().click();
        settle();
        cy.screenshot('09-onboarding-step4-plan', { capture: 'fullPage' });
      }
    });

    // Finish onboarding
    cy.get('body').then(($b) => {
      if ($b.find('button:contains("Finish"), button:contains("Done"), button:contains("Skip for Now"), button:contains("Finish Later")').length) {
        cy.contains('button', /Finish|Done|Skip for Now|Finish Later/).first().click();
        settle();
      }
    });

    // ────────────────────────────────────────────────────────────
    //  DASHBOARD
    // ────────────────────────────────────────────────────────────

    cy.visit('/home');
    settle();
    cy.screenshot('10-dashboard', { capture: 'fullPage' });

    // ── Theme toggle (dark mode) ──
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="header-theme-toggle-btn"]').length) {
        cy.get('[data-testid="header-theme-toggle-btn"]').click({ force: true });
        settle();
        cy.screenshot('10-dashboard-dark-theme', { capture: 'fullPage' });
        // Toggle back to light
        cy.get('[data-testid="header-theme-toggle-btn"]').click({ force: true });
        settle();
      }
    });

    // ────────────────────────────────────────────────────────────
    //  INGREDIENTS — create, view list
    // ────────────────────────────────────────────────────────────

    cy.visit('/ingredient/new');
    settle();
    cy.screenshot('11-ingredient-form-empty', { capture: 'fullPage' });

    cy.get('[data-testid="ingredient-form"] input[formcontrolname="name"]').type('Chicken Breast');
    cy.get('[data-testid="ingredient-form"]').then(($form) => {
      if ($form.find('input[formcontrolname="pluralName"]').length) {
        cy.get('[data-testid="ingredient-form"] input[formcontrolname="pluralName"]').type('Chicken Breasts');
      }
      if ($form.find('textarea[formcontrolname="description"]').length) {
        cy.get('[data-testid="ingredient-form"] textarea[formcontrolname="description"]').type('Boneless, skinless chicken breast. High in protein, low in fat.');
      }
    });
    cy.screenshot('11-ingredient-form-filled', { capture: 'fullPage' });

    cy.get('[data-testid="ingredient-submit-btn"]').click();
    settle(2000);
    cy.screenshot('11-ingredient-created', { capture: 'fullPage' });

    // Create a second ingredient for recipe use
    cy.visit('/ingredient/new');
    settle();
    cy.get('[data-testid="ingredient-form"] input[formcontrolname="name"]').type('Brown Rice');
    cy.get('[data-testid="ingredient-form"]').then(($form) => {
      if ($form.find('textarea[formcontrolname="description"]').length) {
        cy.get('[data-testid="ingredient-form"] textarea[formcontrolname="description"]').type('Whole grain brown rice, a complex carbohydrate source.');
      }
    });
    cy.get('[data-testid="ingredient-submit-btn"]').click();
    settle(2000);

    cy.visit('/ingredients/mine');
    settle();
    cy.get('[data-testid="my-ingredients"]').should('exist');
    cy.screenshot('12-my-ingredients-list', { capture: 'fullPage' });

    // ────────────────────────────────────────────────────────────
    //  RECIPES — create, view detail, comments, rating, import
    // ────────────────────────────────────────────────────────────

    cy.visit('/recipe/new');
    settle();
    cy.screenshot('13-recipe-form-empty', { capture: 'fullPage' });

    cy.get('[data-testid="recipe-form"] input[formcontrolname="name"]').type('Grilled Chicken & Rice Bowl');
    cy.get('[data-testid="recipe-form"]').then(($form) => {
      if ($form.find('textarea[formcontrolname="description"]').length) {
        cy.get('[data-testid="recipe-form"] textarea[formcontrolname="description"]').type('A simple, protein-packed grilled chicken bowl served over brown rice with fresh vegetables.');
      }
    });

    // Add ingredient if the form supports it
    cy.get('[data-testid="recipe-form"]').then(($form) => {
      if ($form.find('input[formcontrolname="searchText"]').length) {
        cy.get('[data-testid="recipe-form"] input[formcontrolname="searchText"]').first().type('Chicken');
        settle(1000);
        cy.get('body').then(($b2) => {
          if ($b2.find('mat-option').length) {
            cy.get('mat-option').first().click();
            settle(500);
          }
        });
      }
    });

    // Add a step if the form supports it
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="recipe-add-step-btn"]').length) {
        cy.get('[data-testid="recipe-add-step-btn"]').click();
        settle(300);
        cy.get('[data-testid="recipe-form"] textarea[formcontrolname="description"]').last().type('Season chicken with salt, pepper, and garlic powder. Grill 6 minutes per side.');
      }
    });

    cy.screenshot('13-recipe-form-filled', { capture: 'fullPage' });

    cy.get('[data-testid="recipe-submit-btn"]').click();
    settle(2000);
    cy.screenshot('13-recipe-created', { capture: 'fullPage' });

    // ── My Recipes list ──
    cy.visit('/recipes/mine');
    settle();
    cy.get('[data-testid="my-recipes"]').should('exist');
    cy.screenshot('14-my-recipes-list', { capture: 'fullPage' });

    // ── Recipe detail ──
    cy.get('body').then(($b) => {
      const cards = $b.find('[data-testid="recipe-card"]');
      if (cards.length) {
        cy.get('[data-testid="recipe-card"]').first().click();
        settle();
        cy.screenshot('15-recipe-detail-overview', { capture: 'fullPage' });

        // Nutrition tab
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Nutrition")').length) {
            cy.contains('button', 'Nutrition').click();
            settle();
            cy.screenshot('15-recipe-detail-nutrition', { capture: 'fullPage' });
          }
        });

        // Diet tab
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Diet")').length) {
            cy.contains('button', 'Diet').click();
            settle();
            cy.screenshot('15-recipe-detail-diet', { capture: 'fullPage' });
          }
        });

        // Back to overview for comments & rating
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Overview")').length) {
            cy.contains('button', 'Overview').click();
            settle();
          }
        });

        // Add a comment
        cy.get('body').then(($b2) => {
          if ($b2.find('[data-testid="recipe-comments-form"]').length) {
            cy.get('[data-testid="recipe-comments-form"] textarea').first().type('This recipe turned out great! The chicken was perfectly seasoned.');
            cy.screenshot('15-recipe-detail-comment', { capture: 'fullPage' });
            if ($b2.find('[data-testid="recipe-comments-submit-btn"]').length) {
              cy.get('[data-testid="recipe-comments-submit-btn"]').click();
              settle();
            }
          }
        });
      }
    });

    // ── Recipe import from URL ──
    cy.visit('/recipe/import');
    settle();
    cy.get('[data-testid="recipe-import"]').should('exist');
    cy.screenshot('16-recipe-import-empty', { capture: 'fullPage' });

    cy.get('[data-testid="recipe-import"]').then(($el) => {
      if ($el.find('input[formcontrolname="url"], input[type="url"]').length) {
        cy.get('[data-testid="recipe-import"] input[formcontrolname="url"], [data-testid="recipe-import"] input[type="url"]').first()
          .type('https://www.example.com/recipes/sample-recipe');
        cy.screenshot('16-recipe-import-url-entered', { capture: 'fullPage' });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  MEAL PLAN — calendar, shuffle, cell click, rules, curated
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-meal-plan');
    cy.screenshot('17-meal-plan-calendar', { capture: 'fullPage' });

    // Click a meal cell to open recipe search dialog
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="plan-cell"]').length) {
        cy.get('[data-testid="plan-cell"]').first().click();
        settle();
        cy.get('[data-testid="recipe-search-dialog"]').should('exist');
        cy.screenshot('17-meal-plan-recipe-search-dialog', { capture: 'fullPage' });

        // Close dialog
        cy.get('body').then(($b2) => {
          if ($b2.find('[data-testid="recipe-search-done-btn"]').length) {
            cy.get('[data-testid="recipe-search-done-btn"]').click();
            settle();
          } else if ($b2.find('[mat-dialog-close]').length) {
            cy.get('[mat-dialog-close]').first().click({ force: true });
            settle();
          } else {
            cy.get('body').type('{esc}');
            settle();
          }
        });
      }
    });

    // Shuffle button
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="plan-shuffle-btn"]').length) {
        cy.get('[data-testid="plan-shuffle-btn"]').click();
        settle();
        cy.get('[data-testid="shuffle-dialog"]').should('exist');
        cy.screenshot('17-meal-plan-shuffle-dialog', { capture: 'fullPage' });

        // Close shuffle dialog without acting
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Cancel")').length) {
            cy.contains('button', 'Cancel').click();
            settle();
          } else {
            cy.get('body').type('{esc}');
            settle();
          }
        });
      }
    });

    // Print button (just screenshot the tooltip, don't actually trigger print)
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="plan-print-btn"]').length) {
        cy.get('[data-testid="plan-print-btn"]').trigger('mouseenter');
        settle(500);
        cy.screenshot('17-meal-plan-print-tooltip', { capture: 'viewport' });
        cy.get('[data-testid="plan-print-btn"]').trigger('mouseleave');
      }
    });

    // ── Plan rules ──
    cy.visit('/plan/rules');
    settle();
    cy.get('[data-testid="plan-rules"]').should('exist');
    cy.screenshot('18-plan-rules', { capture: 'fullPage' });

    // Fill a rule if form is present
    cy.get('[data-testid="plan-rules"]').then(($el) => {
      if ($el.find('[data-testid="plan-rules-form"]').length) {
        cy.get('[data-testid="plan-rules-form"] input[formcontrolname="queryFilter"]').type('chicken');
        if ($el.find('mat-select[formcontrolname="mealTypeId"]').length) {
          cy.get('[data-testid="plan-rules-form"] mat-select[formcontrolname="mealTypeId"]').click();
          settle(300);
          cy.get('mat-option').first().click();
          settle(300);
        }
        cy.screenshot('18-plan-rules-filled', { capture: 'fullPage' });
      }
    });

    // ── Curated plans ──
    cy.visit('/plan/curated');
    settle();
    cy.screenshot('19-curated-plans', { capture: 'fullPage' });

    // Clone plan dialog
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="clone-plan-clone-btn"]').length) {
        cy.get('[data-testid="clone-plan-clone-btn"]').first().click();
        settle();
        cy.get('[data-testid="clone-plan-dialog"]').should('exist');
        cy.screenshot('19-curated-plans-clone-dialog', { capture: 'fullPage' });
        // Cancel clone
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Cancel")').length) {
            cy.contains('button', 'Cancel').click();
            settle();
          } else {
            cy.get('body').type('{esc}');
            settle();
          }
        });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  SHOPPING LIST — interactions
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-shopping');
    cy.get('[data-testid="shopping"]').should('exist');
    cy.screenshot('20-shopping-list', { capture: 'fullPage' });

    // Check off an item if any exist
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="shopping-item"]').length) {
        cy.get('[data-testid="shopping-item"]').first().within(() => {
          cy.get('mat-checkbox').click();
        });
        settle();
        cy.screenshot('20-shopping-item-checked', { capture: 'fullPage' });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  PANTRY — view, add item
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-pantry');
    cy.get('[data-testid="pantry"]').should('exist');
    cy.screenshot('21-pantry', { capture: 'fullPage' });

    // Open add form
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="pantry-add-toggle-btn"]').length) {
        cy.get('[data-testid="pantry-add-toggle-btn"]').click();
        settle();
        cy.get('[data-testid="pantry-add-form"]').should('exist');
        cy.screenshot('21-pantry-add-form', { capture: 'fullPage' });

        // Fill the add form
        cy.get('[data-testid="pantry-add-form"]').then(($form) => {
          if ($form.find('input[matautocomplete], input').length) {
            cy.get('[data-testid="pantry-add-form"] input').first().type('Chicken');
            settle(1000);
            cy.get('body').then(($b2) => {
              if ($b2.find('mat-option').length) {
                cy.get('mat-option').first().click();
                settle(500);
              }
            });
            cy.screenshot('21-pantry-add-form-filled', { capture: 'fullPage' });
          }
        });

        // Cancel instead of saving
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Cancel")').length) {
            cy.contains('button', 'Cancel').click();
            settle();
          }
        });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  COOKBOOKS — list, create dialog, detail
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-cookbooks');
    cy.screenshot('22-cookbooks-list', { capture: 'fullPage' });

    // New cookbook dialog
    cy.get('body').then(($b) => {
      if ($b.find('button:contains("New Cookbook")').length) {
        cy.contains('button', 'New Cookbook').click();
        settle();
        cy.screenshot('22-cookbook-create-dialog', { capture: 'fullPage' });

        // Fill dialog
        cy.get('body').then(($b2) => {
          if ($b2.find('input[formcontrolname="name"], [data-testid="cookbook-name-input"] input').length) {
            cy.get('input[formcontrolname="name"], [data-testid="cookbook-name-input"] input').first().type('Weeknight Dinners');
          }
          if ($b2.find('textarea[formcontrolname="description"], [data-testid="cookbook-description-input"] textarea').length) {
            cy.get('textarea[formcontrolname="description"], [data-testid="cookbook-description-input"] textarea').first().type('Quick and easy meals for busy weeknights.');
          }
          cy.screenshot('22-cookbook-create-dialog-filled', { capture: 'fullPage' });

          // Save
          if ($b2.find('button:contains("Save")').length) {
            cy.contains('button', 'Save').click();
            settle(2000);
          } else {
            cy.get('body').type('{esc}');
            settle();
          }
        });
      }
    });

    // Cookbook detail (click first cookbook if one exists)
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="cookbook-card"]').length) {
        cy.get('[data-testid="cookbook-card"]').first().click();
        settle();
        cy.screenshot('22-cookbook-detail', { capture: 'fullPage' });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  MESSAGING — inbox, compose, thread
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-messages');
    cy.get('[data-testid="inbox"]').should('exist');
    cy.screenshot('23-messages-inbox', { capture: 'fullPage' });

    // Compose message
    cy.visit('/messages/new');
    settle();
    cy.get('[data-testid="compose"]').should('exist');
    cy.screenshot('23-messages-compose-empty', { capture: 'fullPage' });

    cy.get('body').then(($b) => {
      if ($b.find('input[formcontrolname="subject"]').length) {
        cy.get('input[formcontrolname="subject"]').type('Meal plan for next week');
      }
      if ($b.find('textarea[formcontrolname="message"]').length) {
        cy.get('textarea[formcontrolname="message"]').type('Hey! Have you checked out the new curated plans? Some great options for our household.');
      }
      if ($b.find('mat-select[formcontrolname="recipientIds"]').length) {
        cy.get('mat-select[formcontrolname="recipientIds"]').click();
        settle(300);
        cy.get('body').then(($b2) => {
          if ($b2.find('mat-option').length) {
            cy.get('mat-option').first().click();
            settle(300);
            cy.get('body').click(10, 10); // close dropdown
            settle(300);
          }
        });
      }
      cy.screenshot('23-messages-compose-filled', { capture: 'fullPage' });
    });

    // ── Search ──
    cy.visit('/search');
    settle();
    cy.screenshot('24-search-empty', { capture: 'fullPage' });

    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="header-search-input"]').length) {
        cy.get('[data-testid="header-search-input"]').type('chicken{enter}');
        settle();
        cy.screenshot('24-search-results', { capture: 'fullPage' });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  HOUSEHOLD — manage, add member dialog
    // ────────────────────────────────────────────────────────────

    navigateViaMenu('nav-household');
    cy.get('[data-testid="household"]').should('exist');
    cy.screenshot('25-household', { capture: 'fullPage' });

    // Create household if not already in one
    cy.get('[data-testid="household"]').then(($el) => {
      if ($el.find('[data-testid="household-create-form"]').length) {
        cy.get('[data-testid="household-create-form"] input[formcontrolname="name"]').type('Walkthrough Family');
        if ($el.find('textarea[formcontrolname="description"]').length) {
          cy.get('[data-testid="household-create-form"] textarea[formcontrolname="description"]').type('Our test household for the screenshot walkthrough.');
        }
        cy.screenshot('25-household-create-form', { capture: 'fullPage' });
        cy.get('[data-testid="household-create-btn"]').click();
        settle(2000);
        cy.screenshot('25-household-created', { capture: 'fullPage' });
      }
    });

    // Add member dialog
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="household-add-person-btn"]').length) {
        cy.get('[data-testid="household-add-person-btn"]').click();
        settle();
        cy.get('[data-testid="add-member-dialog"]').should('exist');
        cy.screenshot('25-household-add-member-dialog', { capture: 'fullPage' });

        // Fill member name if visible
        cy.get('[data-testid="add-member-dialog"]').then(($dialog) => {
          if ($dialog.find('input[formcontrolname="name"]').length) {
            cy.get('[data-testid="add-member-dialog"] input[formcontrolname="name"]').type('Junior Walkthrough');
            cy.screenshot('25-household-add-member-filled', { capture: 'fullPage' });
          }
        });

        // Close dialog
        cy.get('body').then(($b2) => {
          if ($b2.find('button:contains("Cancel")').length) {
            cy.contains('button', 'Cancel').click();
            settle();
          } else {
            cy.get('body').type('{esc}');
            settle();
          }
        });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  SETTINGS — edit profile, restrictions, security, privacy
    // ────────────────────────────────────────────────────────────

    // ── Restrictions (standalone, editing) ──
    navigateViaMenu('nav-restrictions');
    cy.get('[data-testid="restrictions"]').should('exist');
    cy.screenshot('26-restrictions', { capture: 'fullPage' });

    // ── Profile (standalone, editing) ──
    navigateViaMenu('nav-profile');
    cy.get('[data-testid="profile"]').should('exist');
    cy.screenshot('27-profile', { capture: 'fullPage' });

    // Edit profile fields
    cy.get('[data-testid="profile-form"]').then(($form) => {
      if ($form.find('input[formcontrolname="name"]').length) {
        cy.get('[data-testid="profile-form"] input[formcontrolname="name"]').clear().type('Alice W. Tester');
        cy.screenshot('27-profile-edited', { capture: 'fullPage' });
      }
    });

    // ── Settings hub ──
    navigateViaMenu('nav-settings');
    cy.get('[data-testid="settings"]').should('exist');
    cy.screenshot('28-settings-hub', { capture: 'fullPage' });

    // Security settings
    cy.get('[data-testid="settings"]').then(($el) => {
      if ($el.find('a[href*="security"]').length) {
        cy.get('[data-testid="settings"] a[href*="security"]').click();
        settle();
        cy.get('[data-testid="security-settings"]').should('exist');
        cy.screenshot('28-settings-security', { capture: 'fullPage' });
        cy.go('back');
        settle();
      }
    });

    // Privacy settings
    cy.get('[data-testid="settings"]').then(($el) => {
      if ($el.find('a[href*="privacy"]').length) {
        cy.get('[data-testid="settings"] a[href*="privacy"]').click();
        settle();
        cy.get('[data-testid="privacy-settings"]').should('exist');
        cy.screenshot('28-settings-privacy', { capture: 'fullPage' });
        cy.go('back');
        settle();
      }
    });

    // ────────────────────────────────────────────────────────────
    //  CURATION SUBMISSION — submit recipe + ingredient via API
    // ────────────────────────────────────────────────────────────

    // We need to call the API to submit items for curation since there's
    // no explicit UI button yet. First, get the auth token from the app.
    cy.window().then((win) => {
      const token = win.localStorage.getItem('access_token') || win.sessionStorage.getItem('access_token');
      if (token) {
        // Submit the recipe for curation
        cy.request({
          method: 'POST',
          url: `${apiUrl}/api/Curation/submit`,
          headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
          body: { entityType: 'Recipe', entityId: 1 },
          failOnStatusCode: false,
        });

        // Submit an ingredient for curation
        cy.request({
          method: 'POST',
          url: `${apiUrl}/api/Curation/submit`,
          headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
          body: { entityType: 'Ingredient', entityId: 1 },
          failOnStatusCode: false,
        });
      }
    });

    // ────────────────────────────────────────────────────────────
    //  ADMIN — dashboard, curation queue, webhooks
    // ────────────────────────────────────────────────────────────

    cy.visit('/admin');
    settle();
    cy.get('[data-testid="admin"]').should('exist');
    cy.screenshot('29-admin-dashboard', { capture: 'fullPage' });

    // Curation queue
    cy.visit('/admin/curation');
    settle();
    cy.get('[data-testid="curation-queue"]').should('exist');
    cy.screenshot('29-admin-curation-queue', { capture: 'fullPage' });

    // Expand first item to show review controls
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="curation-review-btn"]').length) {
        cy.get('[data-testid="curation-review-btn"]').first().click();
        settle();
        cy.screenshot('29-admin-curation-expanded', { capture: 'fullPage' });

        // Fill feedback notes
        cy.get('body').then(($b2) => {
          if ($b2.find('textarea').length) {
            cy.get('[data-testid="curation-queue"] textarea').first().type('Looks good! Meets our quality standards.');
            cy.screenshot('29-admin-curation-feedback', { capture: 'fullPage' });
          }
        });
      }
    });

    // Webhooks
    cy.visit('/admin/webhooks');
    settle();
    cy.get('[data-testid="webhooks"]').should('exist');
    cy.screenshot('29-admin-webhooks', { capture: 'fullPage' });

    // ── Sign out User A ──
    signOut();
    cy.screenshot('30-signed-out', { capture: 'fullPage' });

    // ════════════════════════════════════════════════════════════
    //  PART 3 — User B (admin): curation approval & denial flow
    // ════════════════════════════════════════════════════════════

    loginViaPopover(userB.email);
    cy.screenshot('31-admin-user-logged-in', { capture: 'fullPage' });

    cy.visit('/admin/curation');
    settle();
    cy.get('[data-testid="curation-queue"]').should('exist');
    cy.screenshot('32-admin-curation-queue-userB', { capture: 'fullPage' });

    // ── Approve the first item ──
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="curation-review-btn"]').length) {
        cy.get('[data-testid="curation-review-btn"]').first().click();
        settle();
        cy.screenshot('33-admin-curation-review-item1', { capture: 'fullPage' });

        // Add approval feedback
        cy.get('body').then(($b2) => {
          if ($b2.find('textarea').length) {
            cy.get('[data-testid="curation-queue"] textarea').first().type('Approved — high quality content, well described.');
          }
        });
        cy.screenshot('33-admin-curation-approve-feedback', { capture: 'fullPage' });

        // Click approve
        cy.get('body').then(($b2) => {
          if ($b2.find('[data-testid="curation-approve-btn"]').length) {
            cy.get('[data-testid="curation-approve-btn"]').first().click();
            settle(2000);
            cy.screenshot('33-admin-curation-approved', { capture: 'fullPage' });
          }
        });
      }
    });

    // ── Request revision on the next item ──
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="curation-review-btn"]').length) {
        cy.get('[data-testid="curation-review-btn"]').first().click();
        settle();
        cy.screenshot('34-admin-curation-review-item2', { capture: 'fullPage' });

        cy.get('body').then(($b2) => {
          if ($b2.find('textarea').length) {
            cy.get('[data-testid="curation-queue"] textarea').first().type('Needs more detail — please add nutrient information and a longer description.');
          }
        });
        cy.screenshot('34-admin-curation-revision-feedback', { capture: 'fullPage' });

        cy.get('body').then(($b2) => {
          if ($b2.find('[data-testid="curation-revision-btn"]').length) {
            cy.get('[data-testid="curation-revision-btn"]').first().click();
            settle(2000);
            cy.screenshot('34-admin-curation-revision-sent', { capture: 'fullPage' });
          }
        });
      }
    });

    // ── Reject flow (demonstrate with any remaining item) ──
    cy.get('body').then(($b) => {
      if ($b.find('[data-testid="curation-review-btn"]').length) {
        cy.get('[data-testid="curation-review-btn"]').first().click();
        settle();
        cy.screenshot('35-admin-curation-review-item3', { capture: 'fullPage' });

        cy.get('body').then(($b2) => {
          if ($b2.find('textarea').length) {
            cy.get('[data-testid="curation-queue"] textarea').first().type('Rejected — duplicate content that already exists in the curated library.');
          }
        });
        cy.screenshot('35-admin-curation-reject-feedback', { capture: 'fullPage' });

        cy.get('body').then(($b2) => {
          if ($b2.find('[data-testid="curation-reject-btn"]').length) {
            cy.get('[data-testid="curation-reject-btn"]').first().click();
            settle(2000);
            cy.screenshot('35-admin-curation-rejected', { capture: 'fullPage' });
          }
        });
      }
    });

    // Final state of queue after all actions
    cy.screenshot('36-admin-curation-queue-final', { capture: 'fullPage' });

    // ── User B: browse settings pages to verify admin has same views ──
    navigateViaMenu('nav-settings');
    cy.get('[data-testid="settings"]').should('exist');
    cy.screenshot('37-settings-hub-admin', { capture: 'fullPage' });

    // ── Sign out User B ──
    signOut();
    cy.screenshot('38-final-signed-out', { capture: 'fullPage' });
  });
});
