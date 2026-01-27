# NOM UI Analysis and Improvement Specification

## Executive Summary

Based on automated screenshot capture of 26 authenticated pages across 9 feature areas, this document categorizes screens by type, identifies inconsistencies, and proposes sweeping changes for cleanup, standardization, and UI flow improvements.

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
