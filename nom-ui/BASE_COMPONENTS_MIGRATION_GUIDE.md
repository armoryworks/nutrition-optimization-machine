# Base Components to Angular Material Wrap Migration Guide

## Overview

This guide documents the migration from custom base components to angular-material-wrap library components. The AMW library provides pre-built, configuration-driven components that eliminate boilerplate and reduce custom HTML/SCSS.

**Migration Strategy**: Components will be migrated section-by-section (Sections 3-10). Base components will remain in the codebase until Section 10 is complete to avoid breaking changes.

---

## Component Mappings

| Current Base Component | AMW Replacement | Complexity |
|---|---|---|
| `<nom-base-page>` | `<amw-detail-page>` | Simple |
| `<nom-base-form>` | `<amw-form-page>` | Moderate |
| `<nom-base-list>` | `<amw-list-page>` | Moderate |
| `<nom-base-detail>` | `<amw-detail-page>` | Simple |

---

## Pattern 1: nom-base-page → AmwDetailPageComponent

### Current Implementation (base-page)

**Template** (72 lines):
```html
<nom-base-page
  [config]="pageConfig"
  [isLoading]="isLoading"
  [error]="error"
  (back)="onBack()"
  (refresh)="onRefresh()">
  <!-- Custom content -->
  <div>...</div>
</nom-base-page>
```

**Component**:
```typescript
export class MyComponent {
  @Input() pageConfig: BasePageConfig = {
    title: 'My Page',
    subtitle: 'Page description',
    showBackButton: true,
    showRefreshButton: true
  };
  isLoading = false;
  error: string | null = null;
}
```

### New Implementation (AMW)

**Template** (~30 lines):
```html
<amw-detail-page
  [config]="pageConfig"
  [dataSource]="pageData()"
  (actionClick)="onAction($event)">
</amw-detail-page>
```

**Component** (with signals):
```typescript
import { AmwDetailPageComponent } from 'angular-material-wrap';

export class MyComponent {
  isLoading = signal(false);
  error = signal<string | null>(null);
  data = signal<MyData | null>(null);

  pageConfig: DetailPageConfig = {
    title: 'My Page',
    subtitle: 'Page description',
    enableActions: true,
    actions: [
      { id: 'back', label: 'Back', icon: 'arrow_back' },
      { id: 'refresh', label: 'Refresh', icon: 'refresh' }
    ]
  };

  pageData = computed(() => ({
    isLoading: this.isLoading(),
    error: this.error(),
    data: this.data()
  }));

  onAction(event: { id: string }) {
    if (event.id === 'back') this.router.back();
    if (event.id === 'refresh') this.loadData();
  }
}
```

**Benefits**:
- ✅ 58% less HTML (72 → 30 lines)
- ✅ Loading/error states managed by library
- ✅ Signals instead of @Input decorators
- ✅ Configuration-driven actions

---

## Pattern 2: nom-base-form → AmwFormPageComponent

### Current Implementation (base-form)

**Template** (58 lines):
```html
<nom-base-form
  [config]="formConfig"
  [form]="myForm"
  [isSubmitting]="isSubmitting"
  (formSubmit)="onSubmit()"
  (formCancel)="onCancel()">

  <div>
    <mat-form-field>
      <mat-label>Name</mat-label>
      <input matInput formControlName="name" />
    </mat-form-field>

    <mat-form-field>
      <mat-label>Email</mat-label>
      <input matInput formControlName="email" type="email" />
    </mat-form-field>
  </div>
</nom-base-form>
```

**Component**:
```typescript
export class MyFormComponent {
  @Input() formConfig: BaseFormConfig = {
    title: 'Edit Item',
    submitText: 'Save',
    showCancelButton: true
  };

  myForm = this.fb.group({
    name: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]]
  });

  isSubmitting = false;
}
```

### New Implementation (AMW)

**Template** (~25 lines):
```html
<amw-form-page
  [config]="formConfig()"
  [dataSource]="formData()"
  (formSubmit)="onSubmit($event)">

  <amw-input
    [config]="{ label: 'Name', required: true }"
    [(ngModel)]="name" />

  <amw-input
    [config]="{ label: 'Email', type: 'email', required: true }"
    [(ngModel)]="email" />
</amw-form-page>
```

**Component** (with signals):
```typescript
import { AmwFormPageComponent, AmwInputComponent } from 'angular-material-wrap';

export class MyFormComponent {
  name = signal('');
  email = signal('');
  isSubmitting = signal(false);

  formConfig = computed<FormPageConfig>(() => ({
    title: 'Edit Item',
    submitText: 'Save',
    showCancelButton: true,
    disabled: this.isSubmitting()
  }));

  formData = computed(() => ({
    isValid: this.isFormValid(),
    isSubmitting: this.isSubmitting()
  }));

  isFormValid(): boolean {
    return this.name().length > 0 &&
           this.email().includes('@');
  }

  onSubmit(event: FormSubmitEvent) {
    this.isSubmitting.set(true);
    // Submit logic
  }
}
```

**Benefits**:
- ✅ 57% less HTML (58 → 25 lines)
- ✅ No FormBuilder boilerplate
- ✅ Built-in validation handling
- ✅ `amw-input` components with minimal configuration

---

## Pattern 3: nom-base-list → AmwListPageComponent

### Current Implementation (base-list)

**Template** (136 lines):
```html
<nom-base-list
  [config]="listConfig"
  [isLoading]="isLoading"
  [isEmpty]="items.length === 0"
  (create)="onCreate()"
  (refresh)="onRefresh()">

  <table>
    <thead>
      <tr>
        <th>Name</th>
        <th>Email</th>
        <th>Actions</th>
      </tr>
    </thead>
    <tbody>
      @for (item of items; track item.id) {
        <tr>
          <td>{{ item.name }}</td>
          <td>{{ item.email }}</td>
          <td>
            <button mat-icon-button (click)="onEdit(item)">
              <mat-icon>edit</mat-icon>
            </button>
            <button mat-icon-button (click)="onDelete(item)">
              <mat-icon>delete</mat-icon>
            </button>
          </td>
        </tr>
      }
    </tbody>
  </table>
</nom-base-list>
```

**Component**:
```typescript
export class MyListComponent {
  @Input() items: Item[] = [];
  isLoading = false;

  listConfig: BaseListConfig = {
    title: 'Items',
    showCreateButton: true,
    showSearch: true
  };
}
```

### New Implementation (AMW)

**Template** (~15 lines):
```html
<amw-list-page
  [config]="pageConfig"
  [dataSource]="pageData()"
  (actionClick)="onAction($event)">
</amw-list-page>
```

**Component** (with signals):
```typescript
import { AmwListPageComponent } from 'angular-material-wrap';

export class MyListComponent {
  items = signal<Item[]>([]);
  isLoading = signal(false);

  pageConfig: ListPageConfig = {
    title: 'Items',
    enableSearch: true,
    enablePagination: true,
    actions: [
      { id: 'create', label: 'Create', icon: 'add', color: 'primary' }
    ]
  };

  pageData = computed<ListPageData>(() => ({
    columns: [
      { key: 'name', label: 'Name', sortable: true },
      { key: 'email', label: 'Email', sortable: true }
    ],
    data: this.items(),
    rowActions: [
      { id: 'edit', label: 'Edit', icon: 'edit' },
      { id: 'delete', label: 'Delete', icon: 'delete', color: 'warn' }
    ],
    isLoading: this.isLoading()
  }));

  onAction(event: ActionEvent) {
    if (event.id === 'create') this.router.navigate(['create']);
    if (event.id === 'edit') this.router.navigate(['edit', event.data.id]);
    if (event.id === 'delete') this.confirmDelete(event.data);
  }
}
```

**Benefits**:
- ✅ 89% less HTML (136 → 15 lines)
- ✅ Built-in table with sorting, pagination, search
- ✅ No custom table HTML
- ✅ Row actions handled automatically
- ✅ All data-driven through configuration

---

## Pattern 4: nom-base-detail → AmwDetailPageComponent

### Current Implementation (base-detail)

**Template** (62 lines):
```html
<nom-base-detail
  [config]="detailConfig"
  (back)="onBack()"
  (edit)="onEdit()">

  <div class="detail-grid">
    <div class="detail-row">
      <label>Name:</label>
      <span>{{ item.name }}</span>
    </div>
    <div class="detail-row">
      <label>Email:</label>
      <span>{{ item.email }}</span>
    </div>
    <div class="detail-row">
      <label>Status:</label>
      <span>{{ item.status }}</span>
    </div>
  </div>
</nom-base-detail>
```

**Component**:
```typescript
export class MyDetailComponent {
  @Input() item: Item;

  detailConfig: BaseDetailConfig = {
    title: 'Item Details',
    showBackButton: true,
    showEditButton: true,
    actions: [
      { label: 'Delete', icon: 'delete', color: 'warn', action: () => this.delete() }
    ]
  };
}
```

### New Implementation (AMW)

**Template** (~15 lines):
```html
<amw-detail-page
  [config]="pageConfig"
  [dataSource]="detailData()"
  (actionClick)="onAction($event)">
</amw-detail-page>
```

**Component** (with signals):
```typescript
import { AmwDetailPageComponent } from 'angular-material-wrap';

export class MyDetailComponent {
  item = signal<Item | null>(null);

  pageConfig: DetailPageConfig = {
    title: 'Item Details',
    enableActions: true,
    actions: [
      { id: 'back', label: 'Back', icon: 'arrow_back' },
      { id: 'edit', label: 'Edit', icon: 'edit', color: 'primary' },
      { id: 'delete', label: 'Delete', icon: 'delete', color: 'warn' }
    ]
  };

  detailData = computed<DetailPageData>(() => ({
    sections: [
      {
        title: 'General Information',
        fields: [
          { label: 'Name', value: this.item()?.name },
          { label: 'Email', value: this.item()?.email },
          { label: 'Status', value: this.item()?.status }
        ]
      }
    ]
  }));

  onAction(event: ActionEvent) {
    if (event.id === 'back') this.router.back();
    if (event.id === 'edit') this.router.navigate(['edit']);
    if (event.id === 'delete') this.confirmDelete();
  }
}
```

**Benefits**:
- ✅ 76% less HTML (62 → 15 lines)
- ✅ No custom detail grid CSS
- ✅ Sectioned data display handled by library
- ✅ Actions in unified menu

---

## General Migration Checklist

### For Each Component:

1. **Update Imports**
   ```typescript
   // Remove
   import { BaseListComponent } from '../../common/components/base-list';

   // Add
   import { AmwListPageComponent } from 'angular-material-wrap';
   ```

2. **Replace @Input/@Output with Signals**
   ```typescript
   // Remove
   @Input() items: Item[] = [];
   @Output() itemSelected = new EventEmitter<Item>();

   // Add
   items = input<Item[]>([]);
   itemSelected = output<Item>();
   ```

3. **Convert to Configuration-Driven**
   - Move template logic to TypeScript configuration objects
   - Use computed signals for dynamic configurations
   - Centralize action handling in single method

4. **Update Template**
   - Replace `<nom-base-*>` with `<amw-*-page>`
   - Remove custom HTML structure
   - Use `[config]` and `[dataSource]` bindings

5. **Remove Custom SCSS**
   - Delete component-specific SCSS files (AMW handles styling)
   - Keep only application-specific overrides

---

## Common Patterns

### Loading States
```typescript
// Old
isLoading = true;

// New
isLoading = signal(true);

// In template, AMW handles automatically via dataSource
pageData = computed(() => ({
  isLoading: this.isLoading(),
  data: this.items()
}));
```

### Error Handling
```typescript
// Old
error: string | null = null;

// New
error = signal<string | null>(null);

// AMW displays error automatically
pageData = computed(() => ({
  error: this.error(),
  data: this.items()
}));
```

### Actions
```typescript
// Old - Multiple event handlers
(back)="onBack()"
(edit)="onEdit()"
(delete)="onDelete()"

// New - Single action handler
(actionClick)="onAction($event)"

onAction(event: ActionEvent) {
  switch(event.id) {
    case 'back': this.router.back(); break;
    case 'edit': this.editItem(event.data); break;
    case 'delete': this.deleteItem(event.data); break;
  }
}
```

---

## Migration Order (By Section)

| Section | Components | Priority | Status |
|---------|-----------|----------|--------|
| 3 | Auth & User (10) | HIGH | Pending |
| 4 | Onboarding (5) | MEDIUM | Pending |
| 5 | Simple CRUD (13) | MEDIUM | Pending |
| 6 | Plan & Communication (8) | MEDIUM | Pending |
| 7 | Shopping & Household (14) | MEDIUM | Pending |
| 8 | Meal Plan (6) | HIGH | Pending |
| 9 | Admin & Curation (8) | HIGH | Pending |
| 10 | Recipe (18) | VERY HIGH | Pending |

**Base components will be deleted after Section 10 completion.**

---

## Expected Outcomes

### Lines of Code Reduction
- **HTML**: 30-40% reduction
- **SCSS**: 50-70% reduction (most custom styling eliminated)
- **TypeScript**: Slight increase (more configuration, but cleaner)

### Maintenance Benefits
- Consistent UI across application
- Material Design 3 compliance
- Automatic accessibility features
- Reduced CSS specificity battles
- Faster feature development

---

## Questions & Support

For questions about this migration, refer to:
- **AMW Documentation**: `/node_modules/angular-material-wrap/docs/`
- **Plan File**: `~/.claude/plans/purring-hatching-sonnet.md`
- **Section 1 Complete**: Foundation services migrated to signals ✅

---

**Last Updated**: Section 1 complete (2026-01-06)
**Next Section**: Section 3 - Authentication & User Module
