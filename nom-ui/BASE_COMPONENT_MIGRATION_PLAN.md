# Base Component Migration Plan

## Overview

This plan outlines the systematic migration of all non-base domain components in nom-ui to use the established base components for consistency and maintainability.

## Current State Analysis

### Base Components Available
- ✅ `app-base-page` - Full page layouts with loading/error states
- ✅ `app-base-form` - Create/Edit forms with validation
- ✅ `app-base-detail` - View details with actions
- ✅ `app-base-list` - Dashboard/list views with search/filtering

### Current Usage
- ✅ `household-invite-refactored` - Uses `app-base-form`
- ❌ All other components use custom implementations

## Migration Strategy

### Phase 1: Form Components (Priority: HIGH)
Components that should use `app-base-form`:

#### Recipe Domain
- [ ] `recipe-edit` - Complex form with ingredients and steps
- [ ] `ingredient-edit` - Form for creating/editing ingredients
- [ ] `ingredient-create-modal` - Modal form for new ingredients

#### Person Domain
- [ ] `person-edit` - Simple person form
- [ ] `person-creation` - Person creation form
- [ ] `person-health-edit` - Health information form

#### Household Domain
- [ ] `household-invite-refactored` - ✅ Already migrated
- [ ] Any other household forms

#### Other Domains
- [ ] Any forms in `admin/`, `privacy/`, `plan/`, `restriction/`, `onboarding/`, `nutrient/`

### Phase 2: Page Components (Priority: HIGH)
Components that should use `app-base-page`:

#### Recipe Domain
- [ ] `recipe-edit` - Full page layout
- [ ] `ingredient-edit` - Full page layout

#### Person Domain
- [ ] `person-edit` - Full page layout
- [ ] `person-creation` - Full page layout
- [ ] `person-health-edit` - Full page layout

#### Curation Domain
- [ ] `curation-queue` - Full page layout with complex content

#### Other Domains
- [ ] Any full-page components in other domains

### Phase 3: List Components (Priority: MEDIUM)
Components that should use `app-base-list`:

#### Recipe Domain
- [ ] `recipe-search` - Search results list
- [ ] `recipe-ratings` - Ratings list
- [ ] `recipe-comments` - Comments list
- [ ] `ingredient-search` - Search results list

#### Curation Domain
- [ ] `curation-queue` - Queue items list

#### Other Domains
- [ ] Any list/dashboard components

### Phase 4: Detail Components (Priority: MEDIUM)
Components that should use `app-base-detail`:

#### Recipe Domain
- [ ] `ingredient-details` - Ingredient detail view
- [ ] `recipe-notes` - Notes detail view
- [ ] `recipe-timeline-events` - Timeline events detail view
- [ ] `recipe-share-token` - Share token detail view
- [ ] `recipe-rating` - Rating detail view

#### Other Domains
- [ ] Any detail view components

## Migration Steps for Each Component

### Step 1: Analyze Current Component
1. Identify the component type (form, page, list, detail)
2. Extract current configuration (title, subtitle, actions, etc.)
3. Identify loading states, error handling, and user interactions

### Step 2: Create Base Component Configuration
1. Create appropriate config object (BaseFormConfig, BasePageConfig, etc.)
2. Map current properties to base component inputs
3. Wire up event handlers (submit, cancel, back, refresh, etc.)

### Step 3: Refactor Template
1. Replace custom layout with base component wrapper
2. Move content into `<ng-content>` area
3. Remove duplicate loading/error states
4. Remove custom action buttons

### Step 4: Update Component Class
1. Import base component
2. Add base component to imports array
3. Create config object
4. Wire up event handlers
5. Remove duplicate logic handled by base component

### Step 5: Update Styles
1. Remove styles that are now handled by base component
2. Keep component-specific styles
3. Ensure proper theming with Material 3 variables

## Example Migrations

### Form Component Migration Example

**Before:**
```html
<div class="nom-page-container">
  <mat-card>
    <mat-card-header>
      <mat-card-title>{{ pageTitle }}</mat-card-title>
    </mat-card-header>
    <mat-card-content>
      <form [formGroup]="recipeForm" (ngSubmit)="onSubmit()">
        <!-- Form fields -->
      </form>
    </mat-card-content>
    <mat-card-actions>
      <button mat-button (click)="onCancel()">Cancel</button>
      <button mat-raised-button (click)="onSubmit()">Save</button>
    </mat-card-actions>
  </mat-card>
</div>
```

**After:**
```html
<app-base-form
  [config]="formConfig"
  [form]="recipeForm"
  [isSubmitting]="isSubmitting"
  (submit)="onSubmit()"
  (cancel)="onCancel()">
  <!-- Form fields -->
</app-base-form>
```

### Page Component Migration Example

**Before:**
```html
<div class="nom-page-container full-canvas">
  <div class="page-header">
    <h1>{{ pageTitle }}</h1>
    <button mat-button (click)="onBack()">Back</button>
  </div>
  @if (isLoading) {
    <mat-spinner></mat-spinner>
  }
  @if (error) {
    <div class="error">{{ error }}</div>
  }
  <!-- Content -->
</div>
```

**After:**
```html
<app-base-page
  [config]="pageConfig"
  [isLoading]="isLoading"
  [error]="error"
  (back)="onBack()"
  (refresh)="onRefresh()"
  (retry)="onRetry()">
  <!-- Content -->
</app-base-page>
```

## Benefits of Migration

1. **Consistency**: All components follow the same patterns
2. **Maintainability**: Common functionality centralized
3. **Accessibility**: Built-in accessibility features
4. **Responsive Design**: Consistent responsive behavior
5. **Error Handling**: Standardized error states
6. **Loading States**: Consistent loading indicators
7. **Theme Integration**: Proper Material 3 theming

## Migration Checklist

For each component:

- [ ] Identify component type (form/page/list/detail)
- [ ] Create appropriate base component config
- [ ] Refactor template to use base component
- [ ] Update component class with base component imports
- [ ] Wire up event handlers
- [ ] Remove duplicate logic
- [ ] Update styles to remove base component styles
- [ ] Test component functionality
- [ ] Test responsive behavior
- [ ] Test accessibility
- [ ] Test theme integration

## Priority Order

1. **Form Components** - Most impact on user experience
2. **Page Components** - Full page layouts
3. **List Components** - Search and dashboard views
4. **Detail Components** - Detail views

## Success Criteria

- [ ] All form components use `app-base-form`
- [ ] All page components use `app-base-page`
- [ ] All list components use `app-base-list`
- [ ] All detail components use `app-base-detail`
- [ ] No duplicate loading/error handling logic
- [ ] Consistent user experience across all components
- [ ] Proper Material 3 theming throughout
- [ ] Accessibility compliance
- [ ] Responsive design consistency 