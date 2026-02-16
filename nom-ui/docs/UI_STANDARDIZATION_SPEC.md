# NOM UI Standardization Specification

## Overview

This document outlines the approach for standardizing the NOM UI with:
1. **Functional component groupings** - Reusable SCSS variables scoped by component type
2. **Theme-based aesthetics** - All styling through AMW theme system
3. **AMW bug reports** - Issues requiring upstream fixes in angular-material-wrap

> **Governing Principles:** All standardization work must comply with the five foundational design principles in [DESIGN-SPECIFICATION.md](./DESIGN-SPECIFICATION.md), Section 1:
> - **8pt Grid**: All spacing tokens must be multiples of 8px (4px sub-grid for optical adjustments only)
> - **Major Third Type Scale**: H1 ~39px, H2 ~25px, H3 20px, Body 16px, Small ~13px
> - **60-30-10 Color**: Action blue exclusively for interactive elements; semantic colors for state only
> - **Border-Radius**: 4px for buttons/inputs, 8px for cards/containers. No pill shapes (>10px)
> - **Elevation**: Cards must have `$elevation-1` at rest, `$elevation-2` on hover. No flat cards.
> - **Transitions**: All state changes use `300ms cubic-bezier(0.4, 0, 0.2, 1)`

---

## Part 1: Functional Component Categories

### Category Definitions

| Category | Description | Components |
|----------|-------------|------------|
| **Dashboard** | Overview pages with stats, search, lists | user-dashboard, shopping-dashboard, meal-plan-dashboard, household-dashboard, recipe-author-dashboard |
| **Create/Edit Form** | CRUD forms for entities | household-create/edit, shopping-create/edit, meal-plan-create/edit, recipe-edit, person-creation/edit |
| **Detail View** | Read-only entity display | household-detail, shopping-detail, meal-plan-detail |
| **List/Queue** | Scrollable lists with actions | curation-queue, messaging-inbox, recipe-comments |
| **Settings** | User preference pages | privacy-settings, household-settings, update-info, update-two-factor |
| **Search** | Search-focused interfaces | recipe-search, ingredient-search, recipe-selection |
| **Wizard/Onboarding** | Multi-step flows | onboarding-wizard, onboarding-invitation-code, onboarding-participants |
| **Auth** | Authentication forms | login, registration, forgot-password, reset-password |

---

## Part 2: SCSS Architecture - Functional Scoping

### File Structure

```
nom-ui/src/
├── _variables.scss              # Existing - base spacing, colors, typography
├── _component-tokens.scss       # NEW - functional component tokens
├── _amw-overrides.scss          # NEW - temporary overrides until AMW fixes
├── _a11y.scss                   # Existing - accessibility
├── _utilities.scss              # Existing - utility classes
└── _styles.scss                 # Existing - global styles
```

### _component-tokens.scss (NEW)

```scss
// ===========================================
// FUNCTIONAL COMPONENT TOKENS
// ===========================================
// These tokens define consistent sizing, spacing, and behavior
// for each functional component category.

@use 'variables' as vars;

// -------------------------------------------
// DASHBOARD COMPONENTS
// -------------------------------------------
$dashboard-header-gap: vars.$spacing-3;        // 12px - space between title and stats
$dashboard-stats-gap: vars.$spacing-2;         // 8px - space between stat pills
$dashboard-filter-bar-height: 40px;            // Compact filter inputs
$dashboard-content-gap: vars.$spacing-4;       // 16px - gap between sections
$dashboard-card-min-width: 280px;              // Min card width in grid

// -------------------------------------------
// CREATE/EDIT FORM COMPONENTS
// -------------------------------------------
$form-field-gap: vars.$spacing-4;              // 16px - vertical gap between fields
$form-section-gap: vars.$spacing-6;            // 24px - gap between form sections
$form-input-height: 48px;                      // Standard input height (ISSUE: see AMW bug #1)
$form-input-padding-y: vars.$spacing-3;        // 12px - vertical padding
$form-input-padding-x: vars.$spacing-4;        // 16px - horizontal padding
$form-textarea-min-height: 100px;              // Minimum textarea height
$form-actions-gap: vars.$spacing-3;            // 12px - gap between action buttons
$form-card-max-width: 600px;                   // Max width for form cards

// -------------------------------------------
// DETAIL VIEW COMPONENTS
// -------------------------------------------
$detail-header-margin-bottom: vars.$spacing-4; // 16px
$detail-section-gap: vars.$spacing-6;          // 24px - between sections
$detail-field-gap: vars.$spacing-2;            // 8px - between label and value
$detail-card-padding: vars.$spacing-6;         // 24px

// -------------------------------------------
// LIST/QUEUE COMPONENTS
// -------------------------------------------
$list-item-padding-y: vars.$spacing-3;         // 12px
$list-item-padding-x: vars.$spacing-4;         // 16px
$list-item-gap: 0;                             // No gap, use borders
$list-header-height: 48px;                     // Fixed header row
$list-action-button-size: 32px;                // Compact action buttons
$list-empty-state-padding: vars.$spacing-8;    // 32px

// -------------------------------------------
// SETTINGS COMPONENTS
// -------------------------------------------
$settings-section-gap: vars.$spacing-6;        // 24px - between settings sections
$settings-toggle-row-padding: vars.$spacing-4; // 16px
$settings-description-margin: vars.$spacing-2; // 8px - below toggle description

// -------------------------------------------
// SEARCH COMPONENTS
// -------------------------------------------
$search-input-height: 48px;                    // Prominent search input
$search-results-gap: vars.$spacing-3;          // 12px
$search-filter-panel-width: 280px;             // Sidebar filter width
$search-result-card-padding: vars.$spacing-4;  // 16px

// -------------------------------------------
// WIZARD/ONBOARDING COMPONENTS
// -------------------------------------------
$wizard-step-indicator-size: 32px;
$wizard-step-gap: vars.$spacing-2;             // 8px
$wizard-content-padding: vars.$spacing-6;      // 24px
$wizard-action-gap: vars.$spacing-4;           // 16px

// -------------------------------------------
// AUTH COMPONENTS
// -------------------------------------------
$auth-card-max-width: 400px;
$auth-card-padding: vars.$spacing-6;           // 24px
$auth-field-gap: vars.$spacing-4;              // 16px
$auth-logo-margin-bottom: vars.$spacing-6;     // 24px
```

---

## Part 3: AMW Theme System Integration

### Current AMW Theme Variables (from docs)

AMW uses Material Design 3 CSS custom properties:

```scss
// Surface colors
--mat-sys-surface
--mat-sys-surface-container
--mat-sys-surface-container-low
--mat-sys-surface-container-high
--mat-sys-on-surface
--mat-sys-on-surface-variant

// Primary colors
--mat-sys-primary
--mat-sys-on-primary
--mat-sys-primary-container
--mat-sys-on-primary-container

// Error colors
--mat-sys-error
--mat-sys-error-container
--mat-sys-on-error

// Outline colors
--mat-sys-outline
--mat-sys-outline-variant
```

### What Should Use AMW Themes (NOT custom overrides)

| Property | AMW Variable | Current Issue |
|----------|--------------|---------------|
| Button background | `--mat-sys-primary` | Working |
| Card background | `--mat-sys-surface-container` | Working |
| Text color | `--mat-sys-on-surface` | Working |
| Secondary text | `--mat-sys-on-surface-variant` | **Too faint** |
| Toggle track | N/A | **Not configurable** |
| Icon button contrast | N/A | **Not configurable** |
| Error state background | `--mat-sys-error-container` | **Uses blue instead** |

---

## Part 4: AMW Bug Reports / Enhancement Requests

### NOM-UI FIX #1: Input Field Padding Doubled (NOT an AMW bug)

**Root Cause:** nom-ui's `_styles.scss` applies padding at TWO levels, stacking on top of each other:

1. **Line 132-136** `.mat-mdc-form-field .mat-mdc-form-field-infix { padding-top: 8px; padding-bottom: 8px; }`
2. **Line 1182-1195** `.amw-input input { min-height: 48px; padding: 12px 16px; }`

The `mat-mdc-form-field-infix` adds 8px top + 8px bottom, AND the inner `input` adds 12px top + 12px bottom = **total 40px vertical padding** on a 48px min-height element. This inflates inputs to ~80px+.

**AMW's AmwInputComponent adds NO padding itself.** It delegates all spacing to Angular Material's `mat-form-field`. The double-padding is entirely from nom-ui's global styles.

**Fix:** Remove the inner input padding override and rely on the `mat-mdc-form-field-infix` padding only:

```scss
// REMOVE this from _styles.scss (lines 1182-1195):
.nom-form__field,
.amw-input,
.amw-textarea,
.amw-select {
  input,
  textarea,
  select {
    min-height: vars.$input-height;        // <-- REMOVE
    padding: vars.$input-padding-y vars.$input-padding-x;  // <-- REMOVE (conflicts with mat-form-field-infix)
    font-size: vars.$font-size-md;
    border-radius: vars.$nom-border-radius;
    @include nom-transition(border-color, vars.$transition-duration-fast);
  }
}

// KEEP the mat-mdc-form-field-infix override (lines 132-136) as the SOLE padding source:
.mat-mdc-form-field {
  .mat-mdc-form-field-infix {
    padding-top: 8px !important;
    padding-bottom: 8px !important;
  }
}
```

**Alternative:** If you want to use the component-token approach instead, replace BOTH overrides with a single source of truth:

```scss
// In _amw-overrides.scss
.mat-mdc-form-field .mat-mdc-form-field-infix {
  padding-top: $form-input-padding-y !important;
  padding-bottom: $form-input-padding-y !important;
}

// Do NOT also set padding on inner input/textarea/select
```

---

### BUG #2: Toggle Switch Visibility in Dark Mode

**Component:** `AmwSwitchComponent`, `AmwToggleComponent`

**Issue:** Toggle switches are nearly invisible in dark mode. The track color has insufficient contrast against dark backgrounds.

**Expected:** Toggle track should have at least 3:1 contrast ratio against background

**Affected Pages:**
- privacy-settings (Analytics, Marketing, Personalization toggles)
- update-two-factor (2FA enable toggle)

**Current Appearance:** Dark gray track on near-black background (~1.5:1 contrast)

**Suggested Fix in AMW:**
```scss
// Add theme variable for toggle track
:root {
  --amw-toggle-track-off: var(--mat-sys-outline);
  --amw-toggle-track-on: var(--mat-sys-primary);
}

.dark-theme {
  --amw-toggle-track-off: var(--mat-sys-outline-variant); // Lighter in dark mode
}
```

---

### BUG #3: Icon-Only Button Contrast

**Component:** `AmwButtonComponent` with `variant="icon"` or icon-only usage

**Issue:** Icon-only buttons (refresh, add, cancel, submit) have extremely low contrast - nearly invisible in both light and dark modes.

**Expected:** Icon buttons should have visible backgrounds or sufficient icon contrast

**Affected Patterns:**
- Form action buttons (X for cancel, checkmark for submit)
- Header action buttons (+ for add, refresh icon)
- List row action buttons

**Suggested Fix in AMW:**
```scss
// Add theme variable for icon button visibility
amw-button[variant="icon"],
.amw-icon-button {
  // Ensure minimum contrast
  color: var(--mat-sys-on-surface);

  &:hover {
    background: var(--mat-sys-surface-container-high);
  }
}
```

---

### BUG #4: Form Validation Error State Styling

**Component:** `AmwInputComponent`, `AmwFormPageComponent`

**Issue:** Error states use light blue background which doesn't communicate "error" semantically. Should use error color palette.

**Expected:** Error states should use `--mat-sys-error-container` background with `--mat-sys-on-error-container` text

**Affected:** Error message containers in meal-plan calendar/rules pages

**Suggested Fix in AMW:**
```scss
.amw-error-state {
  background: var(--mat-sys-error-container);
  color: var(--mat-sys-on-error-container);
  border-left: 4px solid var(--mat-sys-error);
}
```

---

### ENHANCEMENT #1: Theme Token for Secondary Text Contrast

**Request:** Add configurable opacity/contrast for secondary text

**Issue:** `--mat-sys-on-surface-variant` is too faint in dark mode for descriptive text

**Suggested Addition:**
```scss
:root {
  --amw-text-secondary-opacity: 0.7; // Light mode
}

.dark-theme {
  --amw-text-secondary-opacity: 0.85; // Higher in dark mode for readability
}
```

---

### ENHANCEMENT #2: Configurable Input Density

**Request:** Allow input field height configuration via theme

**Current:** Input height is hardcoded, leading to apps overriding with `!important`

**Suggested Addition:**
```scss
:root {
  --amw-input-height-sm: 40px;
  --amw-input-height-md: 48px;  // default
  --amw-input-height-lg: 56px;
}

amw-input {
  &[size="sm"] { /* use sm height */ }
  &[size="lg"] { /* use lg height */ }
}
```

---

## Part 5: Temporary NOM-UI Overrides

Until AMW bugs are fixed, these overrides should be placed in `_amw-overrides.scss`:

```scss
// ===========================================
// TEMPORARY AMW OVERRIDES
// ===========================================
// These overrides address AMW bugs and should be removed
// when the corresponding AMW issues are resolved.
//
// Each override references the AMW bug number for tracking.

@use 'variables' as vars;
@use 'component-tokens' as tokens;

// -------------------------------------------
// FIX #1: Input Padding - Single Source of Truth
// -------------------------------------------
// Root cause: _styles.scss applies padding at BOTH the
// mat-mdc-form-field-infix level AND on the inner input element.
// The fix: use ONE layer of padding only.
//
// This override replaces lines 132-136 AND lines 1182-1195
// in _styles.scss with a single consistent rule.
.mat-mdc-form-field .mat-mdc-form-field-infix {
  padding-top: tokens.$form-input-padding-y !important;
  padding-bottom: tokens.$form-input-padding-y !important;
}

// Remove conflicting inner element padding (was lines 1182-1195)
.amw-input,
.amw-textarea,
.amw-select {
  input,
  textarea,
  select {
    // Do NOT set padding here - it's handled by form-field-infix above
    min-height: auto;       // Remove forced min-height
    padding: 0 !important;  // Prevent double-padding
    font-size: vars.$font-size-md;
    border-radius: vars.$nom-border-radius;
  }
}

// -------------------------------------------
// BUG #2: Toggle Visibility Fix
// -------------------------------------------
.dark-theme,
[data-theme='dark'] {
  // Improve toggle track visibility
  .mat-mdc-slide-toggle {
    .mdc-switch__track {
      background-color: var(--mat-sys-outline) !important;
    }

    &.mat-mdc-slide-toggle-checked .mdc-switch__track {
      background-color: var(--mat-sys-primary) !important;
    }
  }
}

// -------------------------------------------
// BUG #3: Icon Button Contrast Fix
// -------------------------------------------
amw-button[variant="icon"],
.amw-icon-button,
button.mat-mdc-icon-button {
  // Ensure visibility
  color: var(--mat-sys-on-surface) !important;
  opacity: 0.87;

  &:hover {
    background: var(--mat-sys-surface-container-high);
    opacity: 1;
  }

  &:disabled {
    opacity: 0.38;
  }
}

// -------------------------------------------
// BUG #4: Error State Fix
// -------------------------------------------
.nom-error-state,
.amw-error-message {
  background: var(--mat-sys-error-container) !important;
  color: var(--mat-sys-on-error-container) !important;
  border-left: 4px solid var(--mat-sys-error);
  padding: vars.$spacing-3 vars.$spacing-4;
  border-radius: vars.$nom-border-radius;
}
```

---

## Part 6: Implementation Priority

### Phase 1: Critical Accessibility (Immediate)
1. Fix toggle visibility (BUG #2)
2. Fix icon button contrast (BUG #3)
3. Fix error state colors (BUG #4)

### Phase 2: Input Sizing (High Priority)
1. Fix input padding (BUG #1)
2. Standardize form field heights across all form components

### Phase 3: Functional Component Standardization
1. Create `_component-tokens.scss`
2. Apply dashboard tokens to all dashboard components
3. Apply form tokens to all create/edit components
4. Apply list tokens to all list/queue components

### Phase 4: AMW Upstream Work
1. File bug reports in angular-material-wrap repository
2. Submit PRs for suggested fixes
3. Remove temporary overrides as AMW fixes are released

---

## Part 7: Verification Checklist

After implementation, verify:

- [ ] Toggle switches visible in dark mode (4.5:1 contrast)
- [ ] Icon buttons visible in all themes
- [ ] Error states use red/amber colors
- [ ] Input fields are 48px height consistently
- [ ] All dashboard pages have consistent stat pill styling
- [ ] All form pages have consistent field spacing
- [ ] All list pages have consistent item padding
- [ ] Footer never overlaps content
- [ ] Screen reader properly announces all interactive elements
