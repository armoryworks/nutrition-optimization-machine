# nom-ui (Old Frontend) Archive

**Archive file:** `archive/nom-ui-old.tar.gz` (547 KB)
**Archived on:** 2026-03-05
**Reason:** Replaced by rebuilt frontend (formerly `nom-ui-new/`, now `nom-ui/`)

---

## What This Archive Contains

The original Angular frontend for the Nutrition Optimization Machine (NOM). This was a fully functional, feature-complete application that was superseded by a rebuilt frontend using the same framework but with a cleaner architecture, simplified dependency tree, and direct Angular Material 3 usage (no AMW wrapper).

To restore: `tar -xzf nom-ui-old.tar.gz` then `npm install` inside the extracted directory.

---

## Framework & Versions

| Dependency | Version |
|-----------|---------|
| Angular | 21.0.6 |
| Angular Material | 21.0.5 (M3) |
| angular-material-wrap (AMW) | 0.1.0-beta.14 |
| TypeScript | 5.9.3 |
| RxJS | 7.8.0 |
| Vitest | 4.0.16 |
| Cypress | 15.9.0 |
| Playwright | 1.58.0 |

**Key difference from replacement:** This project used `angular-material-wrap` (AMW) as an intermediary between the app and Angular Material. The new frontend uses Angular Material 3 directly.

---

## Architecture

- **Standalone components** (no NgModules, except one legacy measurement module)
- **Zoneless change detection** (`provideZonelessChangeDetection`)
- **Lazy-loaded routes** via `loadComponent()` / `loadChildren()`
- **Feature-based directory structure** with isolated domains
- **Base component pattern** (`async-component-base.ts`, `form-component-base.ts`)
- **Event bus** for cross-component communication
- **SCSS** with a comprehensive design system (variables, stereotypes, mixins)

---

## File Statistics

| Metric | Count |
|--------|-------|
| Source files (src/) | ~604 |
| TypeScript files | 383 |
| HTML templates | 99 |
| SCSS stylesheets | 116 |
| Test files (.spec.ts) | 8 |
| Services | 50 |
| Models/interfaces | 144 |
| Source code size | ~3 MB |
| Archive size | ~22 MB |

**Excluded from archive:** `node_modules/` (~418 MB), `dist/`, `.angular/` cache.

---

## Feature Modules & Components

### Auth (11 components)
- Login, Registration, Confirm Email, Forgot Password, Reset Password
- Send Confirmation Email, Update Info, Update Two-Factor
- Login Popover (header dropdown)

### Recipe (20+ components)
- Recipe Search, Edit, Form, Detail (public view)
- Ingredient Search, Create, Edit, Form, Details
- Recipe Assets (image upload), Categories, Comments, Ratings
- Recipe Notes, Scraping (URL import), Share Token, Suggestions, Tags
- Timeline Events, Description Dialog

### Meal Plan (10 components)
- Dashboard, Detail, Create, Edit, Form
- Calendar view, Nutrition summary, Print view
- Recipe Selection dialog, Rules editor
- Meal Plan to Shopping List converter

### Shopping (12 components)
- Dashboard, Detail, Create, Edit, List view
- Bulk Editor, Category Management
- Item Editor, Item Form
- List Export, List Share, Recipe Integration

### Household (8 components)
- Dashboard, Detail, Create, Edit
- Invite, Invite (refactored), Join, Settings

### Cookbook (4 components)
- Dashboard, Detail, Create, Edit

### Communication / Messaging (3 components)
- Inbox, Thread Detail, Compose

### Onboarding (5 components)
- Workflow, Wizard, Invitation Code
- Additional Participants, Restriction Scope

### Person / Profile (4 components)
- Person Creation, Edit
- Health Edit, Profile Edit

### Restriction (4 components)
- Medical, Personal Preference, Societal, Edit

### Plan (3 components)
- Curated Plans, Plan Edit, Plan Name

### Measurement (5 components)
- Category Form, Category List, Converter, Form, List

### Admin & System
- User Management (1), Curation Queue (1)
- Webhook Dashboard (1), Label Dashboard (1)
- Privacy Analytics (1), Recipe Author Dashboard (1)

### Shared / Layout (11 components)
- Context Sidebar, Error State, Loading State
- Password Requirements, Validation Tooltip Overlay
- Sidebar: Household Context, Quick Actions, Restrictions, Shopping Lists, Upcoming Meals

---

## Services (50 total)

**Core:** auth, theme, notification, event-bus, user-info, auth-manager, nom-config

**Feature services:**
- recipe (9): recipe, recipe-advanced, recipe-advanced-search, recipe-assets, recipe-bulk-operations, recipe-categories, recipe-import, recipe-scraping, recipe-search, recipe-suggestion, recipe-tags
- shopping (5): shopping, shopping-list, shopping-list-category, shopping-reference, smart-shopping-list
- meal-plan (2): meal-plan, meal-plan-reference
- household (1), cookbook (1), communication (2), messaging (1)
- person (1), invitation (1), plan (1), restriction (1)
- measurement (2), curation (1), webhook (1), label (1)
- admin/user-management (1), privacy (2)
- common (3): configuration, reference, reference-data
- shared (2): validation-message, validation-tooltip

---

## Routing Structure

```
/                         -> Home (public browse / logged-in dashboard)
/recipe/search            -> Recipe search
/recipe/:id               -> Recipe detail (public)
/recipe/edit/:id          -> Recipe edit (auth)
/recipes/mine             -> My recipes dashboard

/household/*              -> Household CRUD + invite/join
/meal-plan/*              -> Meal plan CRUD + calendar + print
/shopping/*               -> Shopping list CRUD + bulk edit + export
/cookbook/*                -> Cookbook CRUD

/onboarding/*             -> New user workflow
/person/*                 -> Profile + health editing
/restriction/*            -> Dietary restrictions

/communication/*          -> Messaging inbox/thread/compose
/curation/*               -> Curation queue (admin)
/webhook/*                -> Webhook management
/admin/*                  -> User management
/label/*                  -> Label management
/measurement/*            -> Unit measurement management

/auth/login               -> Login
/auth/register            -> Registration
/auth/confirm-email       -> Email confirmation
/auth/forgot-password     -> Password reset request
/auth/reset-password      -> Password reset

/plan/*                   -> Curated plans
/user/privacy             -> Privacy settings
/user/recipes             -> Author dashboard
```

---

## Styling System

**SCSS design system with:**
- 4pt spacing scale (`$spacing-1` through `$spacing-16`)
- M3 color tokens via CSS custom properties (`--mat-sys-*`)
- 7 page stereotypes: form, dashboard, search, detail, master-detail, calendar, landing, wizard, modal
- Responsive breakpoints: mobile (768px), tablet (1024px), desktop (1280px, 1600px, 1920px)
- 5-level elevation system
- Component-scoped styles per component

**Key style files:**
- `_variables.scss` - Design tokens
- `_styles.scss` - Global utilities (42KB)
- `_amw-overrides.scss` - AMW component overrides (27KB)
- `_stereotype-*.scss` - Page layout patterns (7 files)
- `_mixins.scss` - Reusable patterns

---

## Configuration

**Proxy configs:**
- `proxy.config.json` - Production (target: `http://localhost:7053`)
- `proxy.config.development.json` - Docker dev (target: `http://api-dev:8080`)

**Environment files:**
- `environment.ts` - Dev: `{ production: false, apiUrl: '/api' }`
- `environment.prod.ts` - Prod: `{ production: true, apiUrl: '/api' }`

**Docker:**
- `Dockerfile` - Production image
- `Dockerfile.dev` - Development image
- `nginx.conf` - SPA serving configuration

**Build budgets:**
- Initial bundle: 1.8 MB warning / 2.5 MB error
- Component style: 20 KB warning / 45 KB error

---

## Testing

- **Unit tests:** 8 `.spec.ts` files (Vitest)
- **E2E:** Cypress (`cypress/e2e/`) + Playwright (`playwright/`)
- Primary E2E strategy: screenshot-all-pages for visual regression

---

## Why It Was Replaced

1. **AMW dependency** - The `angular-material-wrap` library added an abstraction layer that complicated M3 customization and increased bundle size
2. **Architecture simplification** - The new frontend has a flatter structure with fewer intermediary abstractions
3. **Direct M3 usage** - Angular Material 3 is used directly, making theming and component customization more straightforward
4. **Reduced service count** - Many granular services were consolidated (e.g., 9 recipe services -> fewer)
5. **Cleaner routing** - Single flat route file vs. 12 feature route files
6. **Modern patterns** - Signals, `inject()`, computed properties used consistently throughout
