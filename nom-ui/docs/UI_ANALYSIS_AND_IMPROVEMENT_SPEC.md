# NOM UI Analysis and Improvement Specification

## Executive Summary

Based on automated screenshot capture of 26 authenticated pages across 9 feature areas, this document categorizes screens by type, identifies inconsistencies, and proposes sweeping changes for cleanup, standardization, and UI flow improvements.

> **Note:** For a detailed, per-screenshot A/B analysis comparing the current implementation against the foundational design principles (8pt Grid, Major Third Type Scale, 60-30-10 Color, Z-Pattern IA, Micro-interactions), see [DESIGN-SPECIFICATION.md](./DESIGN-SPECIFICATION.md), Appendix F.

---

## Part 1: Screen Categorization

### Category A: Dashboard/List Pages
Pages that display collections of items with search, filtering, and stats.

| Screen | Path | Key Elements |
|--------|------|--------------|
| User Dashboard | `/user/dashboard` | Dual search panels (Recipes/Ingredients), stats badges, recipe list |
| Recipe Dashboard | `/recipes` | Same as User Dashboard (appears to be same component) |
| Household Dashboard | `/household` | Stats pill, empty state card |
| Shopping Dashboard | `/shopping` | Search input, stats pill, empty state card |
| Meal Plan Dashboard | `/meal-plan` | View toggle (Week/Month), search, stats badges, empty state |
| Curation Queue | `/curation` | Two-panel layout (queue list + detail), status pills |
| Admin User Management | `/admin/user-management` | User list table |

### Category B: Create/Edit Forms
Pages for creating or editing entities.

| Screen | Path | Key Elements |
|--------|------|--------------|
| Household Create | `/household/create` | Card-wrapped form, text input, textarea, action buttons |
| Shopping Create | `/shopping/create` | Form fields for shopping list |
| Meal Plan Create | `/meal-plan/create` | Multi-step form |
| Recipe New | `/recipes/new` | Recipe creation form |
| Communication Compose | `/communication/new` | Message composition form |

### Category C: Settings/Configuration Pages
Pages for managing preferences and settings.

| Screen | Path | Key Elements |
|--------|------|--------------|
| Privacy Settings | `/user/privacy-settings` | Section headers, toggle switches, grouped content |
| Edit Profile | `/edit-profile` | Profile form fields |
| Update Info | `/update-info` | Account info fields |
| Update Two-Factor | `/update-two-factor` | 2FA configuration |

### Category D: Specialized Views
Unique layouts for specific features.

| Screen | Path | Key Elements |
|--------|------|--------------|
| Meal Plan Calendar | `/meal-plan/calendar` | Calendar grid view |
| Meal Plan Rules | `/meal-plan/rules` | Rule configuration |
| Recipe Selection | `/meal-plan/recipe-selection` | Recipe picker |
| Shopping Categories | `/shopping/categories` | Category management |
| Ingredient Search | `/ingredient-search` | Search interface |

### Category E: Onboarding/Wizard
Step-by-step guided flows.

| Screen | Path | Key Elements |
|--------|------|--------------|
| Onboarding Wizard | `/onboarding` | Step indicator, content area, action buttons |
| Curated Plans | `/curated-plans` | Plan selection cards |

### Category F: Communication
Messaging features.

| Screen | Path | Key Elements |
|--------|------|--------------|
| Messaging Inbox | `/communication` | Thread list, preview text |

---

## Part 2: Identified Issues and Inconsistencies

### P1 - Critical (User Experience Impact)

1. **Faint/Invisible Action Buttons**
   - Location: Household Create form (Cancel/Submit buttons barely visible)
   - Issue: Button icons appear as very light gray, nearly invisible
   - Impact: Users may not see how to submit or cancel forms

2. **Inconsistent Empty States**
   - User Dashboard: Has partial empty state for ingredients section only
   - Household/Shopping: Use centered card with icon
   - Meal Plan: Different layout, no card
   - Impact: Inconsistent user experience across features

3. **Footer Overlap on Settings Pages**
   - Location: Privacy Settings
   - Issue: Mealie attribution footer overlaps content when scrolling
   - Impact: Content may be obscured

### P2 - High (Visual Consistency)

4. **Page Header Variations**
   - User Dashboard: "My Dashboard" with stats badges on right
   - Household: "Households" with subtitle, stats pill centered
   - Shopping: "Shopping Lists" with search input inline
   - Meal Plan: "Meal Plans" with stats badges and view toggle
   - Issue: No consistent header pattern

5. **Stats Display Inconsistency**
   - Some use icon + count badges (User Dashboard: "1 Recipes")
   - Some use pills with icon (Household: "0 households")
   - Issue: Different visual treatment for same type of information

6. **Text Contrast Issues**
   - Subtitles and descriptions appear too light
   - Location: Page subtitles ("Manage your household groups...")
   - Impact: Reduced readability, accessibility concerns

### P3 - Medium (Polish)

7. **Curation Queue Layout**
   - Issue: Two-panel layout doesn't match other dashboard patterns
   - Left panel too narrow for content

8. **Empty State Messaging**
   - Some have CTAs ("Create your first...")
   - Some don't have clear next steps
   - Missing + icons on some empty state cards

9. **Search Input Styling**
   - User Dashboard: Has outlined fieldset style
   - Shopping: Has simpler floating label style
   - Issue: Different input treatments

### P4 - Low (Nice to Have)

10. **Footer Attribution**
    - "Powered by Mealie" appears on every page
    - Consider: Move to About page only

11. **Onboarding Visual Polish**
    - "Join Plan" button appears partially faded
    - Step indicator not visible in current state

---

## Part 3: Standardization Proposals

> **Cross-reference:** These proposals must align with the foundational design principles (DESIGN-SPECIFICATION.md Section 1). Specifically:
> - All spacing must follow the 8pt Grid (Section 1.1)
> - Typography must use the Major Third (1.25) scale (Section 1.2)
> - Colors must follow the 60-30-10 rule (Section 1.3) - blue ONLY for interactive elements
> - All cards must have elevation shadows (Section 14.4)
> - All interactive elements must have 44px minimum touch targets and explicit hover/focus/active/disabled states (Section 1.5)
> - Border-radius: 4px for buttons/inputs, 8px for cards. No pills >10px.

### 3.1 Unified Page Header Component

Create a standardized `<nom-page-header>` component:

```html
<nom-page-header
  title="Shopping Lists"
  subtitle="Manage your shopping lists and track your purchases"
  icon="shopping_cart"
  [stats]="[
    { icon: 'shopping_cart', count: 5, label: 'lists' },
    { icon: 'check', count: 12, label: 'completed' }
  ]"
  [showSearch]="true"
  [showRefresh]="true"
  [showAdd]="true"
  (search)="onSearch($event)"
  (refresh)="onRefresh()"
  (add)="onCreate()">
</nom-page-header>
```

**Visual Spec:**
- Title: 24px, font-weight-medium, left-aligned
- Subtitle: 14px, on-surface-variant color, left-aligned below title
- Stats: Right-aligned, pill style with icon + count + label
- Actions: Icon buttons for refresh/add on far right

### 3.2 Unified Empty State Component

Create a standardized `<nom-empty-state>` component:

```html
<nom-empty-state
  icon="shopping_cart"
  title="No Shopping Lists Found"
  description="Create your first shopping list to get started."
  actionLabel="Create List"
  (action)="onCreate()">
</nom-empty-state>
```

**Visual Spec:**
- Centered in available space
- Icon: 64px, on-surface-variant at 50% opacity
- Title: 20px, font-weight-medium
- Description: 14px, on-surface-variant
- Action button: Primary filled button with + icon

### 3.3 Form Button Visibility Fix

**Problem:** Action buttons using icon-only style with low contrast

**Solution:** Update button styling in form actions:
```scss
.form-actions {
  amw-button {
    // Ensure text buttons have sufficient contrast
    --amw-button-text-color: var(--mat-sys-on-surface);

    // For icon-only, use filled or tonal variants
    &.icon-only {
      --amw-button-container-color: var(--mat-sys-surface-container-high);
    }
  }
}
```

### 3.4 Consistent Stats Pills

**Standard Design:**
```scss
.stats-pill {
  display: inline-flex;
  align-items: center;
  gap: vars.$spacing-2;
  padding: vars.$spacing-1 vars.$spacing-3;
  background-color: var(--mat-sys-surface-container);
  border-radius: vars.$spacing-4;
  font-size: vars.$font-size-sm;

  &__icon {
    color: var(--mat-sys-primary);
    font-size: 16px;
  }

  &__count {
    font-weight: vars.$font-weight-medium;
  }

  &__label {
    color: var(--mat-sys-on-surface-variant);
  }
}
```

### 3.5 Text Contrast Improvements

Update subtitle/description colors:
```scss
// Current (too light)
.subtitle {
  color: var(--mat-sys-on-surface-variant);
  opacity: 0.6;
}

// Proposed (sufficient contrast)
.subtitle {
  color: var(--mat-sys-on-surface-variant);
  // Remove opacity, the variant color already provides appropriate contrast
}
```

---

## Part 4: UI Flow and Behavior Improvements

### 4.1 Unified Navigation Flow

**Current State:**
- Some features redirect to dashboard after create
- Some stay on created item
- Back buttons inconsistent

**Proposed Flow:**
1. Create action → Navigate to new item detail view
2. Cancel action → Return to previous page (browser back)
3. Delete action → Return to list view with success toast

### 4.2 Loading States

**Current:** Mix of progress bars and spinners

**Proposed Standard:**
- Full page load: Centered spinner with "Loading..." text
- Section load: Inline progress bar
- Button action: Button loading state (spinner in button)

### 4.3 Error Handling

**Current:** Some pages show error text, some show nothing

**Proposed Standard:**
- API errors: Toast notification with retry action
- Validation errors: Inline field errors + tooltip on submit button (already implemented)
- Not found: Dedicated error page with back navigation

### 4.4 Empty State Actions

**Ensure all empty states have:**
1. Relevant icon
2. Clear title (what's missing)
3. Helpful description (what to do)
4. Primary action button (how to fix it)

### 4.5 Onboarding Improvements

**Issues Identified:**
- Button contrast issues
- Step progress not clearly visible
- "Join Plan" button appears disabled/faded

**Proposed:**
- Add visible step indicator (1/4, 2/4, etc.)
- Ensure all buttons have proper contrast
- Add skip option for optional steps

---

## Part 5: Implementation Priority

### Phase 1: Critical Fixes (Week 1)
- [ ] Fix action button visibility in forms
- [ ] Fix footer overlap on settings pages
- [ ] Standardize text contrast for subtitles

### Phase 2: Component Standardization (Week 2-3)
- [ ] Create `nom-page-header` component
- [ ] Create `nom-empty-state` component
- [ ] Update all dashboards to use new components

### Phase 3: Visual Polish (Week 4)
- [ ] Standardize stats pill styling
- [ ] Unify search input styling
- [ ] Update onboarding flow

### Phase 4: Flow Improvements (Week 5)
- [ ] Standardize navigation after CRUD operations
- [ ] Implement consistent loading states
- [ ] Add missing empty state CTAs

---

## Appendix: Screenshot Reference

All screenshots are stored in `/nom-ui/screenshots/` organized by category:
- `/admin/` - Admin features
- `/communication/` - Messaging
- `/household/` - Household management
- `/meal-plan/` - Meal planning
- `/onboarding/` - User onboarding
- `/public/` - Public pages
- `/recipe/` - Recipe management
- `/shopping/` - Shopping lists
- `/user/` - User settings

Run `node playwright/screenshot-all-pages.mjs` to regenerate screenshots.

---

## Part 6: Screenshot Analysis vs Design Principles

Each screenshot graded against the five foundational design principles (DESIGN-SPECIFICATION.md Section 1).

### 6.1 Home / Landing Page (`01-home.png`)

**What's on screen:** "Welcome to NOM" hero text, subtitle, 2x2 feature card grid (Curated Quality, Nutrition Insights, Community Driven, Smart Search). Header with NOM text, search bar, Home/Get Started/About nav.

| Principle | Guideline | Current State | Violation | Fix |
|-----------|-----------|---------------|-----------|-----|
| **8pt Grid** | All spacing n x 8px | ~80px gap between hero and feature cards is excessive (10U); grid gap ~24px (3U) | PARTIAL | Reduce hero-to-cards gap to 4U (32px) |
| **Major Third Type** | H1 ~39px, H2 ~25px | "Welcome to NOM" ~40px bold (close); feature card headers ~20px (H3 level, should be H2) | PASS | Bump feature card headers to ~25px |
| **60-30-10 Color** | Blue for interactive only | Home button blue (correct); search bar blue outline (interactive, OK) | PASS | |
| **Z-Pattern IA** | Anonymous hero = recipe-on-demand | Hero is marketing text, not recipes. Feature cards are descriptive, not actionable. | FAIL | Replace hero with recipe search/spotlight/trending |
| **Micro-interactions** | Elevation on cards | Feature cards have 1px border, no shadow | FAIL | Add `$elevation-1` default, `$elevation-2` hover |
| **Border-Radius** | 8px for cards | Feature cards ~5px radius | FAIL | Update to 8px |
| **Anonymous Hero** | Recipe-on-demand, info-dense | No recipe content visible. Pure marketing. | FAIL | Implement recipe spotlight / trending recipes |

**Fixes:** (1) Replace marketing hero with recipe-on-demand; (2) Add card shadows; (3) Card border-radius 8px; (4) Tighten spacing.

### 6.2 User/Recipe Dashboard (`10-user-dashboard.png`, `50-recipe-dashboard.png`)

**What's on screen:** "My Dashboard" title, stats pills (0 Recipes, 0 Ingredients, 0 Pending), dual search sections (Recipes + Ingredients) each with search input, status dropdown, + button. Empty state: "No recipes yet" centered.

| Principle | Guideline | Current State | Violation | Fix |
|-----------|-----------|---------------|-----------|-----|
| **8pt Grid** | Consistent multiples | ~200px empty space between search and empty state; ingredient section pushed to bottom | PARTIAL | Redistribute space |
| **Major Third Type** | H1 ~39px for pages | "My Dashboard" ~24-28px (H2-H3 range) | FAIL | Promote to H1 (~39px) |
| **60-30-10 Color** | Blue for interactive only | Stats pill icons use decorative blue; + button is faint gray | FAIL | Neutral stats icons; primary color on + button |
| **Z-Pattern IA** | Content-forward, not action-forward | Search boxes first; no visual content | FAIL | Replace with content-forward or meal timeline |
| **Micro-interactions** | 44px targets, visible | + buttons icon-only, ~30% opacity, barely visible. No elevation. | FAIL | Primary fill + text labels on + buttons |
| **Elevation** | Shadows on cards | Completely flat. No shadows anywhere. | FAIL | Add elevation to all cards |

**Fixes:** (1) CRITICAL - boost + button contrast (primary fill, text label); (2) H1 page title; (3) Add elevation; (4) Content-forward layout.

### 6.3 Household Dashboard (`20-household-dashboard.png`)

**What's on screen:** "Households" title with subtitle, error card (red-tinted, "Failed to load household"), refresh and add icon buttons.

| Principle | Guideline | Current State | Violation | Fix |
|-----------|-----------|---------------|-----------|-----|
| **8pt Grid** | Consistent spacing | Error card ~48px padding for one line of text (excessive) | PARTIAL | Reduce to 2U-3U (16-24px) |
| **60-30-10 Color** | Red for errors only | Translucent red background + red text. Correct. | PASS | |
| **Micro-interactions** | Visible targets | Refresh and + icons nearly invisible (gray on dark) | FAIL | on-surface at 0.87 opacity minimum |
| **Elevation** | Shadows on cards | Error card has no shadow | PARTIAL | Add `$elevation-1` |

**Fixes:** (1) Make icon buttons visible; (2) Reduce error card padding.

### 6.4 Create Forms (`21-household-create.png`, `31-shopping-create.png`, `41-mealplan-create.png`)

**What's on screen:** Centered form cards. Household/Shopping use icon-only buttons (X/checkmark). Meal Plan uses text labels ("Cancel", "Create Meal Plan").

| Principle | Guideline | Current State | Violation | Fix |
|-----------|-----------|---------------|-----------|-----|
| **8pt Grid** | Card padding 2U-3U | ~24px padding (3U). Correct. | PASS | |
| **Major Third Type** | H2 ~25px | "Create Household" ~24-28px bold. Close. | PASS | |
| **60-30-10 Color** | Blue for interactive | Top icon is decorative blue. | PARTIAL | Make top icon neutral |
| **Micro-interactions** | Visible targets | **CRITICAL:** Household/Shopping use icon-only action buttons (X/checkmark) at extremely low contrast. Barely visible. Meal Plan correctly uses text buttons. | FAIL | Standardize ALL forms to text buttons |
| **Border-Radius** | 4px inputs, 8px cards | Inputs ~4-6px, cards ~8-12px | PARTIAL | Standardize to spec |
| **Elevation** | Cards have shadow | Flat or very subtle | PARTIAL | Add `$elevation-1` |

**Fixes:** (1) CRITICAL - text button labels on ALL create forms; (2) Card border-radius 8px; (3) Add elevation.

### 6.5 Meal Plan Dashboard (`40-mealplan-dashboard.png`)

**What's on screen:** "Meal Plans" title, stats pills, search, Week/Month toggle, empty state, error card.

| Principle | Guideline | Current State | Violation | Fix |
|-----------|-----------|---------------|-----------|-----|
| **8pt Grid** | Efficient spacing | Toggle separate from search; wasted vertical space | PARTIAL | Combine into header row |
| **Z-Pattern IA** | Meal Timeline with photos | Search + toggle first, no visual content | FAIL | Future: meal timeline hero |
| **Micro-interactions** | Visible states | Toggle has pill shape with active state (works) | PASS | |
| **Border-Radius** | Max 8px, no pills | Toggle uses pill shape >10px | FAIL | Reduce to 8px |

**Fixes:** (1) Toggle border-radius 8px; (2) Combine search/toggle; (3) Plan meal timeline.

### 6.6 Shopping Dashboard (`30-shopping-dashboard.png`)

Same pattern as Household (6.3). Same systemic issues: invisible icon buttons, error state card. Confirms the fixes need to be central/universal, not per-component.

### 6.7 Cross-Cutting Systemic Issues

| Issue | Screens | Central Fix (one change, everywhere) |
|-------|---------|--------------------------------------|
| **Invisible icon buttons** | 10, 20, 21, 30, 31, 50 | `_amw-overrides.scss`: `opacity: 0.87; color: var(--mat-sys-on-surface)` |
| **Icon-only form actions** | 21, 31 | Component templates: replace icons with text buttons |
| **No card elevation** | ALL | `_styles.scss`: add `box-shadow: $elevation-1` to card classes |
| **Flat feature cards** | 01 | `_styles.scss`: `$elevation-1` default, `$elevation-2` hover |
| **Stats pills low contrast** | 10, 40, 50 | `_styles.scss`: 14px font, 0.87 opacity text |
| **Excessive whitespace** | 10, 20, 30, 40, 50 | Reduce padding, `min-height` on content |
| **Page titles undersized** | 10, 20, 30, 40, 50 | `_variables.scss`: apply H1 scale (~39px) to page titles |
| **No branded mascot** | ALL | Header component: add NOM monster SVG |
| **Pill shapes >10px** | 40, stats pills | `_variables.scss`: cap `$nom-border-radius-pill: 8px` |
| **No Z-pattern layout** | 10, 50 | Future: implement Z-pattern with meal timeline |

### 6.8 Phased Fix Plan

**Phase 1 - Central SCSS (one change = universal fix):**
1. `_amw-overrides.scss`: icon button contrast (0.87 opacity)
2. `_variables.scss`: elevation scale, border-radius 8px cards, pill cap 8px
3. `_variables.scss`: Major Third type scale (H2: 25px, H3: 20px, Small: 13px)
4. `_styles.scss`: elevation on card classes
5. `_amw-overrides.scss`: transition timing `cubic-bezier(0.4, 0, 0.2, 1)`

**Phase 2 - Form templates:**
6. Text button labels on ALL create forms (household, shopping already need this)

**Phase 3 - Dashboard polish:**
7. H1 page titles, stats pill contrast, compact header rows

**Phase 4 - New components:**
8. Meal Timeline, Grocery Forecast, HUD Popover, anonymous recipe hero

**Phase 5 - Layout architecture:**
9. Z-pattern dashboard, right sidebar widgets, three-column recipe view
