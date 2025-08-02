# Component Quick Reference

This guide provides quick lookup information for AI tools and developers working with the NOM base component architecture.

## 🎯 Base Component Selection Guide

### When to Use Each Base Component

| Component         | Use Case                          | Key Features                                       | Example Usage                                      |
| ----------------- | --------------------------------- | -------------------------------------------------- | -------------------------------------------------- |
| `app-base-page`   | Full-page layouts with navigation | Title, actions, loading states, error handling     | Recipe edit, person creation, household management |
| `app-base-form`   | Form-focused components           | Form validation, submit handling, field management | Ingredient creation, user profile editing          |
| `app-base-list`   | Data display with search/filter   | Pagination, sorting, item selection                | Recipe search, curation queue, shopping lists      |
| `app-base-detail` | Single item detailed view         | Item display, related data, actions                | Recipe details, ingredient details, user profiles  |

### Quick Decision Tree

```
Is this a full page with navigation?
├─ Yes → app-base-page
└─ No → Is this primarily a form?
    ├─ Yes → app-base-form
    └─ No → Is this displaying a list of items?
        ├─ Yes → app-base-list
        └─ No → app-base-detail
```

## 🚀 Quick Start Templates

### Base Page Template

```typescript
import {
  BasePageComponent,
  BasePageConfig,
} from "@app/common/components/base-page";

export class MyComponent extends BasePageComponent {
  isLoading = false;
  error: string | null = null;

  pageConfig: BasePageConfig = {
    title: "My Page Title",
    showBackButton: true,
    actions: [{ label: "Save", icon: "save", action: () => this.onSave() }],
  };

  ngOnInit() {
    this.loadData();
  }

  private loadData() {
    this.isLoading = true;
    // Your data loading logic
  }

  onBack() {
    // Navigation logic
  }

  onRefresh() {
    this.loadData();
  }

  onRetry() {
    this.loadData();
  }
}
```

### Base Form Template

```typescript
import {
  BaseFormComponent,
  BaseFormConfig,
} from "@app/common/components/base-form";

export class MyFormComponent extends BaseFormComponent {
  formConfig: BaseFormConfig = {
    title: "My Form",
    submitLabel: "Save",
    cancelLabel: "Cancel",
    fields: [
      { name: "name", label: "Name", type: "text", required: true },
      { name: "email", label: "Email", type: "email", required: true },
    ],
  };

  onSubmit(formData: any) {
    // Handle form submission
  }

  onCancel() {
    // Handle cancellation
  }
}
```

### Base List Template

```typescript
import {
  BaseListComponent,
  BaseListConfig,
} from "@app/common/components/base-list";

export class MyListComponent extends BaseListComponent {
  listConfig: BaseListConfig = {
    title: "My List",
    searchPlaceholder: "Search items...",
    columns: [
      { field: "name", header: "Name", sortable: true },
      { field: "status", header: "Status", sortable: true },
    ],
    actions: [{ label: "Add New", icon: "add", action: () => this.onAdd() }],
  };

  loadData() {
    // Load list data
  }

  onItemSelect(item: any) {
    // Handle item selection
  }
}
```

### Base Detail Template

```typescript
import {
  BaseDetailComponent,
  BaseDetailConfig,
} from "@app/common/components/base-detail";

export class MyDetailComponent extends BaseDetailComponent {
  detailConfig: BaseDetailConfig = {
    title: "Item Details",
    sections: [
      { title: "Basic Information", fields: ["name", "description"] },
      { title: "Additional Info", fields: ["createdDate", "status"] },
    ],
    actions: [
      { label: "Edit", icon: "edit", action: () => this.onEdit() },
      { label: "Delete", icon: "delete", action: () => this.onDelete() },
    ],
  };

  loadData() {
    // Load detail data
  }
}
```

## 🔧 Common Configuration Patterns

### Loading States

```typescript
// Always implement loading state
this.isLoading = true;
try {
  await this.loadData();
} catch (error) {
  this.error = error.message;
} finally {
  this.isLoading = false;
}
```

### Error Handling

```typescript
// Centralized error handling
private handleError(error: any) {
  this.error = error.message || 'An error occurred';
  this.notificationService.showError(this.error);
}
```

### Form Validation

```typescript
// Reactive form validation
this.form = this.fb.group({
  name: ["", [Validators.required, Validators.minLength(2)]],
  email: ["", [Validators.required, Validators.email]],
});
```

## 📋 Migration Checklist

When migrating existing components to base components:

- [ ] Import base component class
- [ ] Extend base component instead of standalone
- [ ] Add required properties (isLoading, error, config)
- [ ] Implement required methods (onBack, onRefresh, onRetry)
- [ ] Update template to use base component wrapper
- [ ] Test loading states and error handling
- [ ] Verify navigation and actions work correctly

## 🎨 Styling Guidelines

### BEM Methodology

```scss
.my-component {
  &__header {
    // Header styles
  }

  &__content {
    // Content styles
  }

  &__actions {
    // Action button styles
  }
}
```

### Material 3 Integration

```scss
// Use theme variables, never hardcode colors
.my-component {
  background-color: var(--md-sys-color-surface);
  color: var(--md-sys-color-on-surface);
}
```

## 🔍 Common Patterns

### Search and Filter

```typescript
// Implement search with debounce
private searchSubject = new Subject<string>();

ngOnInit() {
  this.searchSubject.pipe(
    debounceTime(300),
    distinctUntilChanged()
  ).subscribe(searchTerm => {
    this.performSearch(searchTerm);
  });
}
```

### Pagination

```typescript
// Handle pagination
onPageChange(page: number) {
  this.currentPage = page;
  this.loadData();
}
```

### Item Selection

```typescript
// Handle item selection
onItemSelect(item: any) {
  this.selectedItem = item;
  this.loadItemDetails(item.id);
}
```

## 🚨 Common Pitfalls

1. **Forgetting Loading States**: Always implement `isLoading` and `error` properties
2. **Missing Error Handling**: Implement `onRetry()` method for error recovery
3. **Hardcoded Colors**: Use Material 3 theme variables instead
4. **Ignoring Accessibility**: Include proper ARIA labels and keyboard navigation
5. **Not Using takeUntil**: Always implement `OnDestroy` with `takeUntil` for subscriptions

## 📚 Related Documentation

- [Component Architecture](./component-architecture.md) - Detailed architecture guide
- [Development Conventions](../development/conventions.md) - Development conventions
- [Migration Progress](../../nom-ui/BASE_COMPONENT_MIGRATION_PROGRESS.md) - Migration history

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
