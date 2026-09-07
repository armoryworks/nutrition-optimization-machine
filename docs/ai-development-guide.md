# AI Development Guide

This guide provides specific instructions and patterns for AI tools like Cursor AI when working with the NOM project.

## Quick Start for AI Tools

### 1. **Project Overview**

- **Framework**: Angular 22 with Standalone Components
- **UI Library**: Angular Material 3
- **Backend**: ASP.NET Core Web API with Entity Framework Core
- **Database**: PostgreSQL 17
- **Architecture**: Domain-driven design with base component patterns

### 2. **Key Architectural Patterns**

#### Base Component Architecture

All frontend components should extend one of these base components:

- `nom-base-page` - Full-page layouts with navigation
- `nom-base-form` - Form-focused components
- `nom-base-list` - Data display with search/filter
- `nom-base-detail` - Single item detailed view

#### Component Selection Decision Tree

```
Is this a full page with navigation?
├─ Yes → nom-base-page
└─ No → Is this primarily a form?
    ├─ Yes → nom-base-form
    └─ No → Is this displaying a list of items?
        ├─ Yes → nom-base-list
        └─ No → nom-base-detail
```

### 3. **Development Workflow**

#### When Creating New Components

1. **Choose Base Component**: Use the decision tree above
2. **Follow Naming Convention**: `feature-name.component.ts`
3. **Implement Required Methods**: `onBack()`, `onRefresh()`, `onRetry()`
4. **Add Loading States**: `isLoading` and `error` properties
5. **Use Material 3**: Theme variables, no hardcoded colors
6. **Follow BEM**: CSS class naming convention

#### When Modifying Existing Components

1. **Check Migration Status**: See `BASE_COMPONENT_MIGRATION_PROGRESS.md`
2. **Extend Base Component**: Replace standalone with base component
3. **Update Template**: Wrap content in base component
4. **Add Required Properties**: `isLoading`, `error`, config object
5. **Test Functionality**: Verify loading states and error handling

## Common Development Tasks

### Creating a New Page Component

```typescript
import {
  BasePageComponent,
  BasePageConfig,
} from "@app/common/components/base-page";
import { Component, OnInit, OnDestroy } from "@angular/core";
import { takeUntil } from "rxjs/operators";
import { Subject } from "rxjs";

@Component({
  selector: "nom-my-page",
  standalone: true,
  imports: [BasePageComponent],
  template: `
    <nom-base-page
      [config]="pageConfig"
      [isLoading]="isLoading"
      [error]="error"
      (back)="onBack()"
      (refresh)="onRefresh()"
      (retry)="onRetry()"
    >
      <!-- Your page content here -->
    </nom-base-page>
  `,
})
export class MyPageComponent
  extends BasePageComponent
  implements OnInit, OnDestroy
{
  private destroy$ = new Subject<void>();
  isLoading = false;
  error: string | null = null;

  pageConfig: BasePageConfig = {
    title: "My Page",
    showBackButton: true,
    actions: [{ label: "Save", icon: "save", action: () => this.onSave() }],
  };

  ngOnInit() {
    this.loadData();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData() {
    this.isLoading = true;
    this.myService
      .getData()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          // Handle success
          this.isLoading = false;
        },
        error: (error) => {
          this.error = error.message;
          this.isLoading = false;
        },
      });
  }

  onBack() {
    this.router.navigate(["/previous-route"]);
  }

  onRefresh() {
    this.loadData();
  }

  onRetry() {
    this.error = null;
    this.loadData();
  }
}
```

### Creating a New Form Component

```typescript
import {
  BaseFormComponent,
  BaseFormConfig,
} from "@app/common/components/base-form";
import { Component, OnInit } from "@angular/core";
import { FormBuilder, FormGroup, Validators } from "@angular/forms";

@Component({
  selector: "nom-my-form",
  standalone: true,
  imports: [BaseFormComponent],
  template: `
    <nom-base-form
      [config]="formConfig"
      [form]="form"
      (submit)="onSubmit($event)"
      (cancel)="onCancel()"
    >
    </nom-base-form>
  `,
})
export class MyFormComponent extends BaseFormComponent implements OnInit {
  form: FormGroup;

  formConfig: BaseFormConfig = {
    title: "My Form",
    submitLabel: "Save",
    cancelLabel: "Cancel",
    fields: [
      { name: "name", label: "Name", type: "text", required: true },
      { name: "email", label: "Email", type: "email", required: true },
    ],
  };

  constructor(private fb: FormBuilder) {
    super();
  }

  ngOnInit() {
    this.form = this.fb.group({
      name: ["", [Validators.required, Validators.minLength(2)]],
      email: ["", [Validators.required, Validators.email]],
    });
  }

  onSubmit(formData: any) {
    if (this.form.valid) {
      this.myService.save(formData).subscribe({
        next: () => {
          this.notificationService.success("Saved successfully");
          this.router.navigate(["/success-route"]);
        },
        error: (error) => {
          this.notificationService.showError(error.message);
        },
      });
    }
  }

  onCancel() {
    this.router.navigate(["/cancel-route"]);
  }
}
```

## Styling Guidelines

### Material 3 Theme Variables

```scss
//  CORRECT - Use theme variables
.my-component {
  background-color: var(--md-sys-color-surface);
  color: var(--md-sys-color-on-surface);
  border: 1px solid var(--md-sys-color-outline);
}

//  INCORRECT - Hardcoded colors
.my-component {
  background-color: #ffffff;
  color: #000000;
  border: 1px solid #cccccc;
}
```

### BEM Methodology

```scss
.my-component {
  &__header {
    padding: 16px;
    border-bottom: 1px solid var(--md-sys-color-outline);
  }

  &__content {
    padding: 16px;
  }

  &__actions {
    display: flex;
    gap: 8px;
    justify-content: flex-end;
  }

  &--loading {
    opacity: 0.6;
    pointer-events: none;
  }
}
```

## Common Patterns

### Error Handling

```typescript
private handleError(error: any) {
  this.error = error.message || 'An error occurred';
  this.notificationService.showError(this.error);
  console.error('Error:', error);
}
```

### Loading States

```typescript
private setLoading(loading: boolean) {
  this.isLoading = loading;
  if (loading) {
    this.error = null;
  }
}
```

### Search with Debounce

```typescript
private searchSubject = new Subject<string>();

ngOnInit() {
  this.searchSubject.pipe(
    debounceTime(300),
    distinctUntilChanged(),
    takeUntil(this.destroy$)
  ).subscribe(searchTerm => {
    this.performSearch(searchTerm);
  });
}

onSearchChange(searchTerm: string) {
  this.searchSubject.next(searchTerm);
}
```

## Common Pitfalls to Avoid

### 1. **Missing Loading States**

```typescript
//  INCORRECT
ngOnInit() {
  this.myService.getData().subscribe(data => {
    this.data = data;
  });
}

//  CORRECT
ngOnInit() {
  this.isLoading = true;
  this.myService.getData()
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: (data) => {
        this.data = data;
        this.isLoading = false;
      },
      error: (error) => {
        this.error = error.message;
        this.isLoading = false;
      }
    });
}
```

### 2. **Not Using takeUntil**

```typescript
//  INCORRECT
ngOnInit() {
  this.myService.getData().subscribe(data => {
    this.data = data;
  });
}

//  CORRECT
ngOnInit() {
  this.myService.getData()
    .pipe(takeUntil(this.destroy$))
    .subscribe(data => {
      this.data = data;
    });
}
```

### 3. **Hardcoded Colors**

```scss
//  INCORRECT
.my-component {
  background-color: #ffffff;
  color: #000000;
}

//  CORRECT
.my-component {
  background-color: var(--md-sys-color-surface);
  color: var(--md-sys-color-on-surface);
}
```

## Reference Documentation

### Essential Documents for AI Tools

1. **[System Architecture](architecture/system-architecture.md)** - Overall technical structure
2. **[Component Architecture](architecture/component-architecture.md)** - Frontend patterns
3. **[Component Quick Reference](architecture/component-quick-reference.md)** - Quick lookup guide
4. **[Implementation Status](requirements/implementation-status.md)** - Current progress
5. **[Conventions](development/conventions.md)** - Coding standards

### Migration Resources

- **[Migration Progress](../../nom-ui/BASE_COMPONENT_MIGRATION_PROGRESS.md)** - Completed migrations
- **[Development Workflow](workflows/development-workflow.md)** - Development process
- **[In-Process Tasks](workflows/in-process-tasks.md)** - Current task tracking and progress

## AI-Specific Instructions

### When Asked to Create Components

1. **Always check existing patterns** in similar components
2. **Use base components** unless there's a specific reason not to
3. **Follow naming conventions** strictly
4. **Include loading states** and error handling
5. **Use Material 3 theme variables** for styling
6. **Implement OnDestroy** with takeUntil for subscriptions

### When Asked to Modify Components

1. **Check migration status** first
2. **Preserve existing functionality** while adding new features
3. **Update to base component pattern** if not already migrated
4. **Test loading states** and error scenarios
5. **Verify Material 3 theming** compliance

### When Asked to Debug Issues

1. **Check loading states** implementation
2. **Verify error handling** patterns
3. **Confirm subscription cleanup** with takeUntil
4. **Validate Material 3** theme variable usage
5. **Review base component** configuration

## Project Status Summary

### Backend:  COMPLETE

- All database entities implemented
- All API controllers with proper authorization
- All orchestration services with business logic

### Frontend:  PARTIALLY COMPLETE

- **Recipe Management**:  IMPLEMENTED
- **Curation Queue**:  COMPLETE
- **Authentication**:  COMPLETE
- **Privacy Features**:  COMPLETE
- **Household Management**:  FOUNDATION
- **Shopping Lists**:  FOUNDATION
- **Meal Planning**:  FOUNDATION

### Next Priorities

1. Complete household management frontend components
2. Complete shopping lists frontend components
3. Complete meal planning frontend components
4. Finish messaging system frontend
5. Complete multi-participant onboarding

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
