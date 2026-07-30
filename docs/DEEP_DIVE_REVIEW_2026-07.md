# NOM Deep-Dive Review — July 2026

Three parallel code reviews (Angular UI, .NET API, testing/docs) performed 2026-07-29/30.
This document consolidates the findings into one prioritized backlog. Items marked
**[FIXED 2026-07-30]** were remediated as part of this review.

---

## Fixed during this review

### Navigation & admin gating (UI + API)
- **Persistent left nav rail** (`nom-ui/src/app/layout/nav/`) — grouped destinations
  (Plan / Cook / People), active-route highlighting, collapsible (persisted), off-canvas
  drawer behind a header hamburger on mobile. Avatar menu reduced to account items
  (Profile, Restrictions, Sign Out); Quick Actions reduced to true actions.
- **Admin is role-gated**: `GET /api/User/self` now populates `IsAdmin`/`CanManage` from
  *stored* user claims; new `adminGuard` protects `/admin` routes; Admin nav entry and the
  Settings "Administration" card render only for admins.
- **Real 404 page** replaces the silent `** → home` redirect.

### Security fixes (API)
- **Privilege escalation severed**: `CustomClaimsPrincipalFactory` no longer derives the
  global `CanManageCuration`/`CanManageUserRoles` claims from household/plan admin roles.
  Every household creator was previously becoming a system-wide user admin. Global claims
  now come only from stored user claims (`_GrantInitialAdminClaims.sql` / user management).
- **IDOR closed**: `PUT /api/User/{userId}` now requires self-or-`CanManageUserRoles`
  (any authenticated user could previously change any user's email → account takeover).
- **Shopping lists tenant-scoped**: all list and item operations (read, update, delete,
  add-item, recipe add/remove) now verify the caller is the author or a member of the
  list's household. `GET /api/ShoppingList` previously returned every list in the database,
  and update allowed reassigning lists into arbitrary households.
- **Hardcoded JWT signing key removed**: the dead custom-JWT login path
  (`AuthenticateUserAsync`/`GenerateJwtTokenAsync`, zero callers, placeholder key in git)
  was deleted outright.
- **Password reset token no longer written to logs**.
- `_GrantInitialAdminClaims.sql` now uses `ORDER BY "Id"` (previously non-deterministic
  `LIMIT 1` without ordering).

---

## P0 — Remaining security issues (fix before any deployment)

1. ~~No lockout on login~~ **[FIXED 2026-07-30]** — the lockout-bypassing custom login path
   was deleted; the built-in Identity login enforces lockout, now configured explicitly
   (5 attempts / 15 min).
2. ~~API tokens are issue-only~~ **[FIXED 2026-07-30]** — `ApiTokenAuthenticationHandler`
   authenticates `X-Api-Key` requests (hash match against active tokens, `LastUsedDate`
   stamped, full owner claims via the claims factory) through a selector scheme in
   `Program.cs`. Tokens act as the owning user; scopes/expiry remain future work.
3. ~~JwtBearer scheme is vestigial~~ **[FIXED 2026-07-30]** — scheme removed from
   `Program.cs`, preview `JwtBearer` package dropped, the 5 unsatisfiable policies removed,
   and the unread `JWT_*` env vars stripped from all compose files and `.env.example`.
   The never-connected Redis services were also removed from all compose files.
4. ~~CORS fails open~~ **[FIXED 2026-07-30]** — accepts `;` or `,` delimiters and fails
   closed outside Development.
5. ~~User image upload~~ **[FIXED 2026-07-30]** — 5 MB cap + JPEG/PNG/WebP magic-byte
   validation, honest file extension, `UseStaticFiles()` added so the returned URL serves.
   *(Still open: files live outside the mounted data volume, so they vanish on redeploy.)*
6. ~~`SECURITY_INVENTORY.md` is fiction~~ **[FIXED 2026-07-30]** — rewritten from code with
   verified statuses.
7. ~~Rate limiting memory leak~~ **[FIXED 2026-07-30]** — the leak-prone dictionary was
   dead code (limits actually use expiring `IMemoryCache` entries); removed. *(Still
   per-process; move to the built-in .NET 9 rate limiter for multi-replica deployments.)*

## P1 — Correctness bugs (user-visible)

1. ~~2FA broken~~ **[FIXED 2026-07-30]** — interceptor rewritten with an explicit
   unauthenticated allowlist; 2FA and manage endpoints now get the bearer token.
2. ~~UTC date bug~~ **[FIXED 2026-07-30]** — `core/utils/local-date.ts` added; all 9 UTC
   date usages replaced.
3. ~~Own-message styling~~ **[FIXED 2026-07-30]** — thread component now uses
   `AuthService.personId()`.
4. ~~OCR fabricates recipes~~ **[FIXED 2026-07-30]** — failure now throws instead of
   returning fake content; Dockerfile installs `tesseract-ocr` + `eng.traineddata` with
   soname symlinks. *(Verify with a container smoke test before relying on OCR.)*
5. ~~Search race~~ **[FIXED 2026-07-30]** — URL-driven queries now flow through a
   `switchMap`, cancelling in-flight requests.
6. ~~Interceptor refresh races~~ **[FIXED 2026-07-30]** — concurrent 401s share one
   refresh via a `ReplaySubject` queue and all retry with the new token.
7. **Divergent `GetCurrentPersonId()` (API)** — 8 copies with different fallbacks. The
   audit-service hardcoded `1` now uses `SystemConstants.SystemPersonId` **[2026-07-30]**,
   but the real fix — one `ICurrentUserContext` — is still pending.
8. ~~`db/` workflow edges~~ **[FIXED 2026-07-30]** — `--single-transaction` apply,
   `--allow-destructive` gate, FK-dependency-ordered new tables, changed views dropped
   before column changes, stale Atlas comments removed. *(Still open: dev compose mounts
   schema into `initdb.d`, which only runs on first volume init — run `./db/apply.sh`
   after schema changes.)*

## P2 — Architecture & hygiene

> **Progress 2026-07-30 (overnight session):** dead backend infrastructure deleted;
> `ICurrentUserService` consolidation done (also fixed three GetCurrentUserId copies
> that threw for every authenticated user); shopping domain logic extracted to
> `core/domain/shopping` and adopted by pantry (divergent classifier eliminated);
> `HouseholdStore` caches the household list across 10 former fetch sites;
> `ShuffleFlowService` deduplicates the plan/dashboard shuffle flow; basic focus
> management + real Escape handling landed. Test counts: backend 17, frontend 21.
> Second pass: form-validation pattern fixed (submit stays enabled; invalid submit
> marks controls touched), shared `nom-error-banner` (role=alert + retry) adopted in
> ten components. The "split plan calendar/wizard" recommendation is WITHDRAWN:
> wizard mode is a deliberate shared pattern — Profile, Restrictions, Household, and
> Plan all embed as `mode="wizard"` steps in onboarding and the add-member dialog;
> splitting one would break the symmetry and risks onboarding regressions.
> Still open: loading-convention unification (documented as a convention instead),
> CancellationToken threading, responsive pass.

- **Delete ~9,500 LOC of dead backend infrastructure** (`Nom.Api/Core|Factories|DI|Events`,
  `Nom.Data/CustomMigration`) — never DI-registered, latently broken (`_BaseRepository`
  query can't be translated by EF). Also: `test_ollama_integration.cs` (doesn't compile),
  `swagger_temp.json`, `refresh_db_and_migration.{sh,bat}` + `start-dev.sh` (recreate the
  deleted EF-migrations workflow), `Nom.Import` (not in the solution — add or delete).
- **UI dead code**: all 5 shared pipes (re-implemented inline where needed), 5 model files,
  `loginAfterRegister`, `.dark-theme` (no rules), `meal-composition.config.ts`.
- **Introduce a `HouseholdStore`** — `getHouseholds()` is fetched from 11 call sites with 5
  divergent "current household" derivations.
- **Decompose `shopping.component.ts` (1,124 lines)** — extract unit conversion, department
  classification (currently two *divergent* regex classifiers vs pantry), shelf-life
  defaults (duplicated), and the 215-line `departments` computed.
- **Split `plan.component` calendar vs wizard**; extract the shuffle flow duplicated
  verbatim in `dashboard.component`.
- **One error/loading convention**: global HTTP error interceptor + shared error banner
  (`role="alert"`, retry affordance); pick LoadingService or local spinners (currently
  ~50/50 split, some both).
- **EF query hygiene**: `AsNoTracking` on reads (24/171 today), `AsSplitQuery` on
  multi-collection Includes (recipe search cartesian explosion), transactions around
  multi-`SaveChanges` flows, fix bulk-ops N+1-with-per-row-commit loops, thread
  `CancellationToken` (1 of 292 endpoints today).
- **Build guardrails**: `Directory.Build.props` (`TreatWarningsAsErrors`, analyzers),
  move `JwtBearer` off the preview package, drop ASP.NET Core 2.x shim packages.

## P3 — Accessibility & UX polish (UI)

- 46 axe nodes from 3 root causes: footer GitHub icon `aria-label` (clears 32),
  6 icon-buttons with `matTooltip` but no `aria-label` (critical), `mat-error` contrast +
  10 templates missing `touched` guards (errors show red on pristine forms).
- Zero focus management app-wide: popovers don't trap/restore focus; the
  `role="button" tabindex="-1"` backdrop pattern makes Escape-to-close unreachable;
  skip-link target lacks `tabindex="-1"`.
- Disabled-submit pattern on all 13 forms with `markAllAsTouched` used nowhere — invalid
  forms show a dead button with no explanation.
- 36 of 46 component stylesheets have no responsive rules; search results silently truncate
  at 50 with no pagination.

## P4 — Testing & documentation truth

- **Tests**: 6 of 12 Cypress specs are assertion-free stubs (two visit routes that don't
  exist); the CI "Primary Regression" spec has 7 assertions in 1,141 lines and targets a
  `data-cy` scheme with zero occurrences in the app; `commands.ts` (24 custom commands) is
  100% dead; the admin spec can't authenticate in CI (no admin seed — run
  `_GrantInitialAdminClaims.sql` in `e2e-tests.yml`); Angular unit coverage is one
  `toBeTruthy()`; .NET coverage is 11 measurement tests; `run-integration-tests.sh` dies on
  filters matching zero classes yet prints hardcoded success checkmarks.
- **Docs**: most of `docs/` describes the pre-rewrite UI archived in commit `696db43`
  (AMW component library, base-list, EF migrations, Angular 17/.NET 8 — none exist).
  `docs/development/conventions.md` will make an AI agent write non-compiling code.
  Mark ARCHIVED or rewrite: `ENHANCEMENT_SUMMARY`, `COMPACT_HEADER_IMPLEMENTATION_TODOS`,
  `DESKTOP_UI_VIEWPORT_MIGRATION_PLAN`, `LINTING_PROGRESS_README`,
  `COMPREHENSIVE_MIGRATION_ANALYSIS`, `requirements/implementation-status.md`,
  `nom-test/README.md`, `development/smoke-testing.md`, `SECURITY_INVENTORY.md`.
- **Backend-only features with no UI**: user management, labels, smart shopping list,
  recipe bulk ops — plus many endpoints returning mock data (smart-shopping prices,
  nutrition, suggestion analytics). Decide: build UI, or cut.

---

*Generated from three parallel deep-dive reviews on 2026-07-29/30. File/line evidence for
every claim exists in the session transcripts; spot-check anything before acting on it.*
