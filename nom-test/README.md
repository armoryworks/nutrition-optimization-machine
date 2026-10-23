# NOM end-to-end tests

Cypress specs that drive the real NOM UI (Angular, served by the test stack) against the
real API and a seeded PostgreSQL database. Every spec runs its journeys at a desktop
viewport (1280x720) and a phone viewport (412x915), registers the users it needs through
the registration page, and never depends on another spec's data or order.

## Suite layout

| Spec | Journey |
| --- | --- |
| `cypress/e2e/anonymous-browse.cy.ts` | Visitor: home categories, recipe detail tabs, header search, Sign In popover, protected routes, theme toggle |
| `cypress/e2e/registration-onboarding.cy.ts` | Password rules, duplicate email, profile validation, onboarding wizard, skip and finish later |
| `cypress/e2e/user-daily-workflow.cy.ts` | Returning user signs in from the header, reads a recipe, checks plan, shopping, pantry, settings, signs out |
| `cypress/e2e/profile-settings.cy.ts` | Settings cards, metric/imperial profile, macro goals, dietary restrictions, security and privacy pages |
| `cypress/e2e/meal-plan.cy.ts` | Week calendar, add a recipe to a cell, shuffle, diet-conflict confirmation |
| `cypress/e2e/shopping-pantry.cy.ts` | Shopping list from the plan, check-off, copy/share, send-to-store, pantry add/remove and stock reduction |
| `cypress/e2e/recipes-cookbook.cy.ts` | Header search and nutrition sorts, severe-restriction filter, recipe form validation, cookbooks |
| `cypress/e2e/household.cy.ts` | Convert to a household, add a member with restrictions, food-group rules, household macro goals |
| `cypress/e2e/admin-curation-workflow.cy.ts` | Author submits recipes for review; admin requests a revision and approves; search visibility |
| `cypress/e2e/admin-sections.cy.ts` | Admin hub, every admin section, food catalog approval and USDA links |

Tests marked `it.skip` document known application bugs: each asserts the correct behaviour
and is turned back on once the bug is fixed. `cypress/tools/screenshot-walkthrough.cy.ts`
is a screenshot tour for visual review, not a regression test; it is excluded from
`npm run test:e2e` and runs with `npm run test:screenshots`.

## Running the suite locally

The suite expects the Docker test stack from `docker-compose.test.yml` at the repository
root. Create a `.env` there with `POSTGRES_TEST_DB=nom_test`, `POSTGRES_USER=nom`,
`POSTGRES_PASSWORD=test_password`, a `JWT_SECRET_KEY` of at least 32 characters,
`API_TEST_PORT=8082` and `UI_TEST_PORT=4211`. PostgreSQL defaults to host port 5433; set
`POSTGRES_TEST_PORT` if that port is taken. Then run
`docker compose -f docker-compose.test.yml up -d --build` and wait until
`http://localhost:8082/health` and `http://localhost:4211` answer. The database is
created from `db/schema.sql` and `db/seed.sql` on first start, which gives the specs the
seeded recipes and ingredients they rely on. SMTP is not configured in the stack, so
email confirmation is off and a freshly registered account can sign in straight away.

Seed the admin account exactly as CI does: register `admin@example.com` with the password
`AdminPassword123!` through `POST /api/auth/register-custom`, then insert the
`CanManageCuration` and `CanManageUserRoles` claims for that user into
`auth."AspNetUserClaims"` (the SQL is in `.github/workflows/e2e-tests.yml`). The admin
specs sign in with `CYPRESS_ADMIN_EMAIL` and `CYPRESS_ADMIN_PASSWORD`, which default to
those values. The admin sections spec registers its own user per viewport and has the
seeded admin grant it the curation and user-role claims on the Users page, so the seeded
account stays well inside the API's per-user rate limit even when the suite runs
repeatedly.

From `nom-test`, run `npm ci` once, then start the suite with
`CYPRESS_BASE_URL=http://localhost:4211 CYPRESS_API_URL=http://localhost:8082 npm run test:e2e`.
Set `CYPRESS_VIEWPORT=desktop` or `CYPRESS_VIEWPORT=phone` to run a single viewport (CI
runs one job per viewport); leave it unset to run both. A single spec runs with
`npx cypress run --spec cypress/e2e/<file>`, and `npm run test:open` opens the interactive
runner. `npm run build:types` type-checks the specs without running them.

Whenever an Angular template changes, rebuild the UI image
(`docker compose -f docker-compose.test.yml up -d --build nom-ui-test`) before running
the suite, because the stack serves a static production build. Failure screenshots land in
`cypress/screenshots`, which Cypress clears at the start of every run.

## Writing specs

Select elements by `data-testid` (see the E2E Test IDs table in the repository
`CLAUDE.md`), wait on intercepted requests or on elements rather than on fixed delays, and
create a fresh user per test with `cy.registerAndLogin('<journey>')`. The custom commands
live in `cypress/support/commands.ts` and the viewport helpers in
`cypress/support/viewports.ts`.
