# NOM UI Style Guide

> **Version**: 2.0.0
> **Last Updated**: February 2026
> **Status**:  Complete

This style guide documents the standardized UI patterns, SCSS architecture, and accessibility guidelines for the NOM application.

> **Foundational Principles:** All patterns in this guide are governed by the five immutable design principles defined in [DESIGN-SPECIFICATION.md](./DESIGN-SPECIFICATION.md), Section 1:
> 1. **8pt Grid System** - All spacing in multiples of 8px (4px sub-grid for optical adjustments only)
> 2. **Major Third (1.25) Type Scale** - H1: ~39px, H2: ~25px, H3: 20px, Body: 16px, Small: ~13px
> 3. **60-30-10 Color Rule** - 60% neutral surfaces, 30% secondary content, 10% action accent
> 4. **Assistant-First Information Architecture** - Z-pattern desktop, vertical stack mobile
> 5. **Micro-interactions** - All state changes at 300ms cubic-bezier(0.4, 0, 0.2, 1), 44px touch targets

---

## Table of Contents

1. [SCSS Architecture](#scss-architecture)
2. [Common Patterns](#common-patterns)
3. [Accessibility Guidelines](#accessibility-guidelines)
4. [Component Examples](#component-examples)
5. [Do's and Don'ts](#dos-and-donts)
6. [Migration Guides](#migration-guides)

---

## SCSS Architecture

### File Organization

```
nom-ui/src/
├── _variables.scss         # Global variables (spacing, typography, colors)
├── _utilities.scss          # Utility mixins (eliminates 200-300+ lines of duplication)
├── _a11y.scss              # Accessibility-specific styles
├── _styles.scss            # Main stylesheet (imports all others)
└── app/
    └── [component]/
        └── [component].component.scss  # Component-specific styles
```

### Key Files

#### `_variables.scss`
Defines all global design tokens:

- **Spacing Scale** (8pt Grid - all values are multiples of 8px)
  ```scss
  // Sub-grid (optical adjustments only)
  $spacing-1: 0.25rem;    // 4px - ONLY badge padding, icon alignment
  // Primary grid
  $spacing-xs: 0.5rem;    // 8px (1U)
  $spacing-sm: 1rem;      // 16px (2U)
  $spacing-md: 1.5rem;    // 24px (3U)
  $spacing-lg: 2rem;      // 32px (4U)
  $spacing-xl: 3rem;      // 48px (6U)
  $spacing-2xl: 4rem;     // 64px (8U)
  ```

- **Typography Scale** (Major Third 1.25 Progression)
  ```scss
  $font-size-xs: 0.8rem;    // ~13px (Small/Meta)
  $font-size-sm: 0.875rem;  // 14px
  $font-size-md: 1rem;      // 16px (Body - base)
  $font-size-lg: 1.25rem;   // 20px (H3 - Card Headers)
  $font-size-xl: 1.56rem;   // ~25px (H2 - Section Titles)
  $font-size-2xl: 2.44rem;  // ~39px (H1 - Page Intent)
  ```

- **Component Sizing**
  ```scss
  $button-height: 40px;              // Touch-friendly button height
  $input-height: 48px;               // Touch-friendly form field height
  $card-padding: 1.5rem;             // Standard card padding (3U)
  $nom-border-radius: 4px;           // Buttons, inputs
  $nom-border-radius-card: 8px;      // Cards, containers (no pills > 10px)
  ```

- **Elevation Scale**
  ```scss
  $elevation-1: 0 1px 2px rgba(0, 0, 0, 0.08);    // Cards at rest
  $elevation-2: 0 2px 4px rgba(0, 0, 0, 0.1);     // Hovered cards
  $elevation-3: 0 4px 12px rgba(0, 0, 0, 0.15);   // Dropdowns, popovers
  $elevation-4: 0 8px 24px rgba(0, 0, 0, 0.2);    // Modals, HUD overlays
  ```

#### `_utilities.scss`
Provides reusable mixins that eliminate duplication:

```scss
@use 'utilities' as *;

// Available mixins:
@include nom-loading-state($padding);
@include nom-empty-state($padding);
@include nom-form-field($gap);
@include nom-action-group($gap, $justify);
@include nom-transition($property, $duration, $timing);
@include nom-focus-visible;
@include nom-sr-only;
```

#### `_a11y.scss`
Provides accessibility-specific styles:

- Skip links for keyboard navigation
- Focus visible styles for all interactive elements
- High contrast mode support
- Reduced motion support
- Screen reader only utility class

---

## Common Patterns

### 1. Loading States

**Before** (duplicated across 25+ files):
```scss
.component {
  &__loading {
    display: flex;
    justify-content: center;
    align-items: center;
    padding: 2rem;
  }
}
```

**After** (using utility mixin):
```scss
@use '../../../../_utilities' as *;

.component {
  &__loading {
    @include nom-loading-state(2rem);
  }
}
```

**HTML**:
```html
@if (isLoading()) {
  <div class="component__loading" role="status" aria-live="polite" aria-label="Loading data">
    <amw-progress-spinner [diameter]="40"></amw-progress-spinner>
    <span class="sr-only">Loading data, please wait...</span>
  </div>
}
```

### 2. Empty States

**Before** (duplicated across 20+ files):
```scss
.component {
  &__empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 3rem;
    text-align: center;
  }

  &__empty-icon {
    font-size: 4rem;
    color: var(--mat-sys-on-surface-variant);
    margin-bottom: 1rem;
  }
}
```

**After** (using utility mixin):
```scss
@use '../../../../_utilities' as *;

.component {
  &__empty-state {
    @include nom-empty-state(3rem);
  }
}
```

**HTML**:
```html
@if (items.length === 0 && !isLoading()) {
  <div class="component__empty-state">
    <amw-icon name="info" aria-hidden="true"></amw-icon>
    <p>No items found</p>
  </div>
}
```

### 3. Form Fields

**Before** (duplicated across 30+ files):
```scss
.component {
  &__form {
    display: flex;
    flex-direction: column;
    gap: 1.5rem;
  }
}
```

**After** (using utility mixin):
```scss
@use '../../../../_utilities' as *;

.component {
  &__form {
    @include nom-form-field(1.5rem);
  }
}
```

**HTML with ARIA**:
```html
<form [formGroup]="form" (ngSubmit)="onSubmit()" class="component__form">
  <amw-input
    formControlName="name"
    label="Name"
    [required]="true"
    [attr.aria-invalid]="form.get('name')?.invalid && form.get('name')?.touched"
    [attr.aria-describedby]="form.get('name')?.invalid && form.get('name')?.touched ? 'name-error' : null">
  </amw-input>
  @if (form.get('name')?.invalid && form.get('name')?.touched) {
    <span id="name-error" class="nom-form__error" role="alert">
      Name is required
    </span>
  }
</form>
```

### 4. Action Button Groups

**Before** (duplicated across 20+ files):
```scss
.component {
  &__actions {
    display: flex;
    gap: 1rem;
    justify-content: flex-end;
    align-items: center;
    margin-top: 1.5rem;
  }
}
```

**After** (using utility mixin):
```scss
@use '../../../../_utilities' as *;

.component {
  &__actions {
    @include nom-action-group(1rem, flex-end);
  }
}
```

### 5. Transitions with Reduced Motion Support

**Always use the transition mixin** to respect user preferences. All transitions use the standard timing: `300ms cubic-bezier(0.4, 0, 0.2, 1)`.

```scss
.component {
  &__card {
    @include nom-transition(box-shadow, vars.$transition-duration-normal);
    box-shadow: vars.$elevation-1; // Cards have elevation at rest

    &:hover {
      box-shadow: vars.$elevation-2; // Elevated on hover
      transform: translateY(-2px);
    }
  }
}
```

This automatically disables animations for users with `prefers-reduced-motion: reduce`.

### 6. Interactive Element States

**All interactive elements MUST define all five states** (see DESIGN-SPECIFICATION.md Section 1.5):

```scss
.component {
  &__button {
    @include nom-transition(all);

    // Default: resting appearance
    // Hover: subtle elevation or color shift
    &:hover:not(:disabled) { /* ... */ }
    // Focus: visible ring for keyboard nav
    &:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
    // Active: pressed feedback
    &:active:not(:disabled) { transform: scale(0.98); }
    // Disabled: reduced opacity
    &:disabled { opacity: 0.38; pointer-events: none; }
  }
}
```

---

## Accessibility Guidelines

### WCAG 2.1 AA Compliance

All components must meet WCAG 2.1 AA standards:
- Color contrast ratio of 4.5:1 for normal text
- Color contrast ratio of 3:1 for large text
- Keyboard navigation support
- Screen reader compatibility
- Touch target size of **44x44px minimum** (Fitts's Law - see DESIGN-SPECIFICATION.md Section 1.5)
- **60-30-10 color rule:** Action colors (blue) only for interactive elements; never decorative

### ARIA Patterns

#### 1. Form Validation

**Required attributes**:
- `aria-invalid`: Indicates validation state
- `aria-describedby`: Links field to error message
- `role="alert"`: Announces errors to screen readers

```html
<amw-input
  formControlName="email"
  label="Email"
  [attr.aria-invalid]="form.get('email')?.invalid && form.get('email')?.touched"
  [attr.aria-describedby]="form.get('email')?.invalid && form.get('email')?.touched ? 'email-error' : null">
</amw-input>

@if (form.get('email')?.invalid && form.get('email')?.touched) {
  <span id="email-error" class="nom-form__error" role="alert">
    @if (form.get('email')?.hasError('required')) {
      Email is required
    }
    @if (form.get('email')?.hasError('email')) {
      Please enter a valid email address
    }
  </span>
}
```

#### 2. Loading States

**Required attributes**:
- `role="status"`: Indicates status update
- `aria-live="polite"`: Announces to screen readers (non-disruptive)
- `aria-label`: Describes the loading state

```html
@if (isLoading()) {
  <div role="status" aria-live="polite" aria-label="Loading data">
    <amw-progress-bar mode="indeterminate"></amw-progress-bar>
    <span class="sr-only">Loading data, please wait...</span>
  </div>
}
```

#### 3. Error States

**Required attributes**:
- `role="alert"`: Immediate screen reader announcement
- `aria-live="assertive"`: Interrupts current announcement for critical errors

```html
@if (error()) {
  <div class="nom-error-state" role="alert" aria-live="assertive">
    <amw-icon name="error_outline" aria-hidden="true"></amw-icon>
    <p>{{ error() }}</p>
  </div>
}
```

#### 4. Decorative Icons

Always add `aria-hidden="true"` to decorative icons:

```html
<amw-icon name="check" aria-hidden="true"></amw-icon>
```

#### 5. Skip Links

App-level skip link for keyboard navigation:

```html
<!-- app.component.html -->
<a href="#main-content" class="nom-skip-link">Skip to main content</a>

<main id="main-content" role="main">
  <router-outlet></router-outlet>
</main>
```

The skip link is hidden by default and appears on focus.

### Semantic HTML

 **DO**:
- Use `<button>` for clickable elements
- Use `<nav>` for navigation sections
- Use `<main>` for primary content
- Use `<header>` and `<footer>` for page sections
- Use proper heading hierarchy (`<h1>` → `<h2>` → `<h3>`)

 **DON'T**:
- Don't use `<div>` with `(click)` handlers
- Don't skip heading levels
- Don't use `<span>` for interactive elements

---

## Component Examples

### Before/After: User Management Component

#### Before (with unnecessary wrappers):
```html
<div class="user-management__container">
  <div class="user-management__empty-state">
    <amw-card class="user-management__empty-card">
      <ng-template #cardContent>
        <div class="user-management__empty-content">
          <mat-icon>info</mat-icon>
          <p>No users found</p>
        </div>
      </ng-template>
    </amw-card>
  </div>
</div>
```

```scss
.user-management {
  &__container {
    display: flex;
    justify-content: center;
    align-items: center;
    min-height: 400px;
  }

  &__empty-state {
    width: 100%;
  }

  &__empty-content {
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 3rem;
    text-align: center;
  }
}
```

#### After (clean structure with accessibility):
```html
<amw-card class="user-management__card">
  <ng-template #cardContent>
    <div class="user-management__empty-state">
      <amw-icon name="info" aria-hidden="true"></amw-icon>
      <p>No users found</p>
    </div>
  </ng-template>
</amw-card>
```

```scss
@use '../../../../_utilities' as *;

.user-management {
  &__card {
    @include nom-centered-container(400px);
  }

  &__empty-state {
    @include nom-empty-state(3rem);
  }
}
```

**Benefits**:
- Removed 3 unnecessary wrapper divs
- Eliminated 15 lines of duplicated SCSS
- Added proper ARIA attributes
- Improved maintainability

---

## Do's and Don'ts

### HTML Structure

#### DON'T: Single-child container wrappers

```html
<!-- BAD: Unnecessary wrapper -->
<div class="component__container">
  <amw-card>...</amw-card>
</div>
```

#### DO: Direct component usage

```html
<!-- GOOD: Clean structure -->
<amw-card class="component__card">...</amw-card>
```

---

#### DON'T: Nested empty state wrappers

```html
<!-- BAD: Unnecessary nesting -->
<div class="component__empty-state">
  <div class="component__empty-content">
    <mat-icon>info</mat-icon>
    <p>No items</p>
  </div>
</div>
```

#### DO: Flat empty state structure

```html
<!-- GOOD: Flat structure with ARIA -->
<div class="component__empty-state">
  <amw-icon name="info" aria-hidden="true"></amw-icon>
  <p>No items found</p>
</div>
```

---

#### DON'T: Interactive divs

```html
<!-- BAD: Non-semantic, poor accessibility -->
<div (click)="doAction()" (keyup.enter)="doAction()" tabindex="0" role="button">
  <mat-icon>edit</mat-icon>
  <span>Edit</span>
</div>
```

#### DO: Semantic buttons

```html
<!-- GOOD: Semantic, accessible -->
<button type="button" (click)="doAction()" aria-label="Edit item">
  <amw-icon name="edit" aria-hidden="true"></amw-icon>
  <span>Edit</span>
</button>
```

---

### SCSS Patterns

#### DON'T: Duplicate centering patterns

```scss
// BAD: Duplicated 40+ times across codebase
.component {
  &__container {
    display: flex;
    justify-content: center;
    align-items: center;
    min-height: 400px;
  }
}
```

#### DO: Use utility mixins

```scss
// GOOD: Reusable mixin
@use '../../../../_utilities' as *;

.component {
  &__container {
    @include nom-centered-container(400px);
  }
}
```

---

#### DON'T: Hardcoded transitions

```scss
// BAD: Doesn't respect user preferences
.component {
  &__card {
    transition: all 0.2s ease;
  }
}
```

#### DO: Use transition mixin

```scss
// GOOD: Respects prefers-reduced-motion
.component {
  &__card {
    @include nom-transition(box-shadow, vars.$transition-duration-normal);
  }
}
```

---

### Accessibility

#### DON'T: Missing ARIA attributes

```html
<!-- BAD: No accessibility support -->
<amw-input formControlName="email" label="Email" [required]="true"></amw-input>
@if (form.get('email')?.invalid) {
  <span class="error">Email is required</span>
}
```

#### DO: Complete ARIA support

```html
<!-- GOOD: Full accessibility -->
<amw-input
  formControlName="email"
  label="Email"
  [required]="true"
  [attr.aria-invalid]="form.get('email')?.invalid && form.get('email')?.touched"
  [attr.aria-describedby]="form.get('email')?.invalid && form.get('email')?.touched ? 'email-error' : null">
</amw-input>
@if (form.get('email')?.invalid && form.get('email')?.touched) {
  <span id="email-error" class="nom-form__error" role="alert">
    Email is required
  </span>
}
```

---

## Migration Guides

### Related Documentation

- **[BASE_COMPONENTS_MIGRATION_GUIDE.md](../BASE_COMPONENTS_MIGRATION_GUIDE.md)** - AMW component migration
- **[MODERN_CONTROL_FLOW_MIGRATION.md](../MODERN_CONTROL_FLOW_MIGRATION.md)** - Angular modern syntax
- **[DESKTOP_UI_VIEWPORT_MIGRATION_PLAN.md](../DESKTOP_UI_VIEWPORT_MIGRATION_PLAN.md)** - Viewport optimization

### Quick Migration Checklist

When creating or updating a component:

- [ ] Use utility mixins from `_utilities.scss` (no duplicated centering/empty state patterns)
- [ ] Add ARIA attributes to all form fields (`aria-invalid`, `aria-describedby`)
- [ ] Add `role="alert"` to all error messages
- [ ] Add `role="status" aria-live="polite"` to all loading states
- [ ] Add `aria-hidden="true"` to all decorative icons
- [ ] Use semantic HTML (`<button>` not `<div (click)>`)
- [ ] Use transition mixin for animations (respects reduced motion)
- [ ] Remove unnecessary wrapper divs
- [ ] Test with keyboard navigation (Tab, Shift+Tab, Enter, Escape)
- [ ] Test with screen reader (NVDA, JAWS, or VoiceOver)
- [ ] Verify color contrast meets WCAG 2.1 AA (4.5:1 ratio)

---

## Testing Guidelines

### Accessibility Testing

**Automated**:
```bash
# Run Lighthouse accessibility audit
npm run lighthouse

# Run axe-core tests (if configured)
npm run test:a11y
```

**Manual**:
1. **Keyboard Navigation**
   - Tab through all interactive elements
   - Verify focus indicators are visible
   - Test with screen reader enabled

2. **Screen Reader Testing**
   - Test with NVDA (Windows), JAWS (Windows), or VoiceOver (Mac)
   - Verify all form errors are announced
   - Verify loading states are announced

3. **Visual Testing**
   - Test at 200% and 400% zoom
   - Test in high contrast mode
   - Test with color blindness simulation

### Success Criteria

 **Component is ready when**:
- Lighthouse Accessibility score >95
- Zero critical/serious axe-core violations
- Full keyboard navigation works without mouse
- All interactive elements have visible focus indicators
- All form validation is announced to screen readers
- Color contrast meets WCAG 2.1 AA standards

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | January 2026 | Initial release - Phase 2-6 complete |
| 2.0.0 | February 2026 | Aligned with foundational design principles (8pt grid, Major Third type, 60-30-10 color, elevation, micro-interactions) |

---

## Questions or Contributions

For questions about this style guide or to suggest improvements, please:
1. Review the existing migration guides
2. Check the examples in this guide
3. Consult the team lead or senior developer
4. Submit a pull request with proposed changes

---

**Generated with Claude Code** 
