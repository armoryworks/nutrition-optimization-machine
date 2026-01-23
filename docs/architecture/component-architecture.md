# Component Architecture Documentation

## Overview

This document outlines the established component architecture patterns in the nom-ui application, following the successful migration of all components to use base components for consistency and maintainability.

## Base Components

The application uses four core base components to ensure consistency across all UI patterns:

### 1. `nom-base-form` - Form Components

**Purpose**: Standardized form layouts with consistent validation, loading states, and actions.

**Usage Pattern**:

```html
<nom-base-form
  [config]="formConfig"
  [form]="myForm"
  [isSubmitting]="isSubmitting"
  (submit)="onSubmit()"
  (cancel)="onCancel()"
>
  <!-- Form fields -->
</nom-base-form>
```

**Configuration**:

```typescript
formConfig: BaseFormConfig = {
  title: "Form Title",
  subtitle: "Form description",
  submitText: "Save",
  showCancelButton: true,
  cancelText: "Cancel",
  maxWidth: "600px",
};
```

### 2. `nom-base-page` - Page Components

**Purpose**: Full page layouts with consistent headers, loading states, error handling, and navigation.

**Usage Pattern**:

```html
<nom-base-page
  [config]="pageConfig"
  [isLoading]="isLoading"
  [error]="error"
  (back)="onBack()"
  (refresh)="onRefresh()"
  (retry)="onRetry()"
>
  <!-- Page content -->
</nom-base-page>
```

**Configuration**:

```typescript
pageConfig: BasePageConfig = {
  title: "Page Title",
  subtitle: "Page description",
  showBackButton: true,
  maxWidth: "1200px",
};
```

### 3. `nom-base-detail` - Detail Components

**Purpose**: Detail views with consistent layouts, navigation, and content presentation.

**Usage Pattern**:

```html
<nom-base-detail
  [config]="detailConfig"
  [loading]="isLoading"
  [error]="error"
  (back)="onBack()"
>
  <!-- Detail content -->
</nom-base-detail>
```

**Configuration**:

```typescript
detailConfig: BaseDetailConfig = {
  title: "Detail Title",
  subtitle: "Detail description",
  showBackButton: true,
  maxWidth: "800px",
};
```

### 4. `nom-base-list` - List Components

**Purpose**: List views with consistent search, filtering, pagination, and item display.

**Usage Pattern**:

```html
<nom-base-list
  [config]="listConfig"
  [items]="items"
  [loading]="loading"
  [error]="error"
  [totalCount]="totalCount"
  [currentPage]="currentPage"
  [pageSize]="pageSize"
  (pageChange)="onPageChange($event)"
>
  <!-- List content -->
</nom-base-list>
```

**Configuration**:

```typescript
listConfig: BaseListConfig = {
  title: "List Title",
  subtitle: "List description",
  showSearch: true,
  showFilters: true,
  showPagination: true,
  maxWidth: "1200px",
};
```

## Component Patterns

### Form Component Pattern

```html
<nom-base-form
  [config]="formConfig"
  [form]="recipeForm"
  [isSubmitting]="isSubmitting"
  (submit)="onSubmit()"
  (cancel)="onCancel()"
>
  <!-- AMW Form Fields -->
  <amw-input
    [config]="{ label: 'Field Name', formControlName: 'field' }"
  />

  <amw-select
    [config]="{
      label: 'Category',
      options: categories,
      formControlName: 'category'
    }"
  />

  <div class="form-actions">
    <amw-button [config]="{ label: 'Cancel' }"></amw-button>
    <amw-button [config]="{ label: 'Save', variant: 'raised' }"></amw-button>
  </div>
</nom-base-form>
```

### Page + Form Component Pattern

For full-page forms, use both base components:

```html
<nom-base-page
  [config]="pageConfig"
  [isLoading]="isLoading"
  [error]="error"
  (back)="onBack()"
  (refresh)="onRefresh()"
  (retry)="onRetry()"
>
  <nom-base-form
    [config]="formConfig"
    [form]="form"
    [isSubmitting]="isSubmitting"
    (submit)="onSubmit()"
    (cancel)="onCancel()"
  >
    <!-- Form fields -->
  </nom-base-form>
</nom-base-page>
```

### List Component Pattern

```html
<nom-base-list
  [config]="listConfig"
  [items]="items"
  [loading]="loading"
  [error]="error"
  [totalCount]="totalCount"
  [currentPage]="currentPage"
  [pageSize]="pageSize"
  (pageChange)="onPageChange($event)"
>
  <!-- AMW Components for List Content -->
  @for (item of items; track item.id) {
    <amw-card [config]="{ title: item.title }">
      <p>{{ item.description }}</p>
      <div class="card-actions">
        <amw-button [config]="{ label: 'View' }"></amw-button>
        <amw-button [config]="{ icon: 'edit', variant: 'icon' }"></amw-button>
      </div>
    </amw-card>
  }
</nom-base-list>
```

## Migration Guidelines

### When to Use Each Base Component

1. **Use `nom-base-form` when**:

   - Component primarily handles form input and submission
   - Need consistent form validation and loading states
   - Component is part of a larger page or modal

2. **Use `nom-base-page` when**:

   - Component represents a full page layout
   - Need consistent page navigation and error handling
   - Component is the main content area

3. **Use `nom-base-detail` when**:

   - Component displays detailed information about a single item
   - Need consistent detail view layout and navigation
   - Component shows read-only or limited interaction content

4. **Use `nom-base-list` when**:
   - Component displays a collection of items
   - Need search, filtering, or pagination functionality
   - Component shows multiple items in a structured format

### Migration Checklist

For each component migration:

- [ ] Identify the primary component type (form/page/list/detail)
- [ ] Create appropriate base component configuration
- [ ] Refactor template to use base component
- [ ] Update component class with base component imports
- [ ] Wire up event handlers and outputs
- [ ] Remove duplicate loading/error handling logic
- [ ] Update styles to remove base component styles
- [ ] Test component functionality
- [ ] Test responsive behavior
- [ ] Test accessibility
- [ ] Test theme integration

## Benefits Achieved

### 1. Consistency

- All components follow the same patterns
- Standardized loading states, error handling, and user interactions
- Consistent visual appearance across the application

### 2. Maintainability

- Common functionality centralized in base components
- Changes to common patterns only need to be made once
- Reduced code duplication

### 3. User Experience

- Consistent loading indicators and error states
- Standardized form validation and submission
- Uniform page layouts and navigation

### 4. Accessibility

- Built-in accessibility features from base components
- Consistent keyboard navigation
- Proper ARIA attributes

### 5. Responsive Design

- Consistent responsive behavior across components
- Proper Material 3 theming integration
- Mobile-friendly layouts

## Best Practices

### 1. Component Type Selection

- Choose the most appropriate base component type
- Consider the primary purpose of the component
- Don't force a component type - let the functionality guide the choice

### 2. Configuration

- Use meaningful titles and subtitles
- Set appropriate maxWidth values
- Configure only the features you need

### 3. Error Handling

- Always set `error = null` before API calls
- Use centralized error handling through base components
- Provide meaningful error messages

### 4. Loading States

- Use `isLoading` for page-level loading
- Use `isSubmitting` for form submission loading
- Let base components handle loading UI

### 5. Event Handling

- Wire up all appropriate events (back, refresh, retry, submit, cancel)
- Use proper TypeScript typing for event handlers
- Handle cleanup in `ngOnDestroy`

### 6. Form Integration

- Use reactive forms consistently
- Properly validate form controls
- Handle form submission through base component events

## Migration Statistics

- **Total Components Migrated**: 25
- **Form Components**: 7/7 (100%)
- **Page Components**: 5/5 (100%)
- **Detail Components**: 5/5 (100%)
- **List Components**: 4/4 (100%)

## Lessons Learned

1. **Complex Forms**: Complex forms with nested arrays and dynamic fields can be successfully migrated while preserving all functionality
2. **Modal Forms**: Modal forms can use base components while preserving modal-specific features
3. **Dynamic Forms**: Dynamic form generation can be preserved in base component migrations
4. **Detail Views**: Detail views with complex content can be successfully migrated
5. **Search Components**: Complex search functionality with filtering and pagination can be preserved
6. **Form Integration**: Components with both forms and lists can be successfully migrated
7. **Component Type Selection**: Choose the correct base component type based on primary functionality
8. **Service Integration**: All components can successfully integrate with existing services
9. **Error Handling**: Base components provide better error handling than custom implementations
10. **Loading States**: Centralized loading states improve user experience

## Future Development

When creating new components:

1. **Start with the appropriate base component**
2. **Follow the established patterns**
3. **Use the migration checklist as a guide**
4. **Test thoroughly for functionality and accessibility**
5. **Document any new patterns discovered**

This architecture ensures consistent, maintainable, and user-friendly components throughout the application.
