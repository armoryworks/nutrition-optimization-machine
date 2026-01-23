# NOM (Nutrition Optimization Machine) Development Conventions

This document outlines the comprehensive conventions, patterns, and standards used throughout the NOM project. These conventions ensure consistency, maintainability, and adherence to best practices across the entire codebase.

## Table of Contents

1. [Critical Naming Rules](#critical-naming-rules)
2. [Frontend Conventions (Angular)](#frontend-conventions-angular)
3. [Backend Conventions (C#/.NET)](#backend-conventions-cnet)
4. [Database Conventions](#database-conventions)
5. [CSS/SCSS Conventions](#cssscss-conventions)
6. [API Conventions](#api-conventions)
7. [File Structure Conventions](#file-structure-conventions)
8. [Component Architecture Patterns](#component-architecture-patterns)
9. [Service Architecture Patterns](#service-architecture-patterns)
10. [Security & Privacy Conventions](#security--privacy-conventions)
11. [Forbidden Patterns](#forbidden-patterns)

## ✅ COMPLETED: TypeScript File Splitting

**STATUS: COMPLETED** - All TypeScript files have been successfully split to follow the 1:1 convention.

**CHANGES MADE:**

- Split `household.classes.ts` into individual model files
- Split `household.interfaces.ts` into individual interface files
- Split `meal-plan.classes.ts` into individual model files
- Split `meal-plan.interfaces.ts` into individual interface files
- Split `shopping.classes.ts` into individual model files
- Split `shopping.interfaces.ts` into individual interface files
- Renamed all files to follow `.model.ts` and `.model.interface.ts` conventions
- Updated all import statements to reflect new file names
- **Removed centralized export files (barrel files)** - following modern best practices

**RESULT:** All TypeScript files now follow the 1:1 convention with proper naming.

## Critical Naming Rules

### 🚨 CRITICAL: NO DTO SUFFIXES ALLOWED

**NEVER USE** `DTO`, `Dto`, or `dto` suffixes in any model or data transport class.

**ALWAYS USE** these suffixes instead:

- `Model` - For core domain entities and data structures
- `Request` - For inbound API payloads and request objects
- `Response` - For outbound API responses and result objects

**Examples:**

```typescript
// ✅ CORRECT
export class PersonModel {}
export class OnboardingCompleteRequestModel {}
export class ApiResponseCommonModel {}

// ❌ FORBIDDEN
export class PersonDTO {}
export class OnboardingCompleteRequestDto {}
export class ApiResponseDto {}
```

## Frontend Conventions (Angular)

### Base Components Architecture

**CRITICAL: Use Base Components for Consistency**

The application provides base components to reduce repetition and ensure consistent patterns:

#### Base Page Component (`nom-base-page`)

**Use for:** Full page layouts with loading/error states
**When to use:**

- Pages that need loading states
- Pages that need error handling
- Pages that need header with actions
- Pages that may need full canvas layout

```typescript
// Example usage
<nom-base-page
  [config]="pageConfig"
  [isLoading]="isLoading"
  [error]="error"
  (back)="onBack()"
  (refresh)="onRefresh()"
  (retry)="onRetry()">
  <!-- Page content here -->
</nom-base-page>
```

#### Base Form Component (`nom-base-form`)

**Use for:** Create/Edit forms with validation
**When to use:**

- Forms with standard submit/cancel actions
- Forms that need loading states
- Forms that may need delete functionality

```typescript
// Example usage
<nom-base-form
  [config]="formConfig"
  [form]="myForm"
  [isSubmitting]="isSubmitting"
  (submit)="onSubmit()"
  (cancel)="onCancel()"
  (delete)="onDelete()">
  <!-- Form fields here -->
</nom-base-form>
```

#### Base Detail Component (`nom-base-detail`)

**Use for:** View details with actions
**When to use:**

- Detail views with standard actions
- Views that need action menus
- Views that need back/edit buttons

```typescript
// Example usage
<nom-base-detail
  [config]="detailConfig"
  (back)="onBack()"
  (edit)="onEdit()">
  <!-- Detail content here -->
</nom-base-detail>
```

#### Base List Component (`nom-base-list`)

**Use for:** Dashboard/list views with search/filtering
**When to use:**

- List views with search functionality
- Dashboards with create/refresh actions
- Views that need loading/error/empty states

```typescript
// Example usage
<nom-base-list
  [config]="listConfig"
  [isLoading]="isLoading"
  [error]="error"
  [isEmpty]="isEmpty"
  [searchControl]="searchControl"
  (create)="onCreate()"
  (refresh)="onRefresh()"
  (retry)="onRetry()">
  <!-- List content here -->
</nom-base-list>
```

### Page Layout Classes

#### `nom-page-container`

**Use for:** Standard page layouts
**When to use:**

- Most pages that need centered content
- Pages that work well on mobile
- Pages with standard card layouts

#### `full-canvas`

**Use for:** Pages that need full width/height
**When to use:**

- Pages that are not useful on mobile (like curation queue)
- Pages that need maximum screen real estate
- Pages with complex layouts that don't fit standard containers

**Example:**

```html
<div class="nom-page-container full-canvas">
  <!-- Full canvas content -->
</div>
```

### File Naming

| Component Type   | Pattern                         | Example                                          |
| ---------------- | ------------------------------- | ------------------------------------------------ |
| Components       | `kebab-case.component.ts`       | `person-edit.component.ts`                       |
| Services         | `kebab-case.service.ts`         | `privacy-orchestration.service.ts`               |
| Models           | `kebab-case.model.ts`           | `onboarding-complete-request.model.ts`           |
| Model Interfaces | `kebab-case.model.interface.ts` | `onboarding-complete-request.model.interface.ts` |
| Pipes            | `kebab-case.pipe.ts`            | `json-parse-common.pipe.ts`                      |
| Interfaces       | `kebab-case.interface.ts`       | `user-consent.interface.ts`                      |

### Class/Interface Naming

| Type       | Pattern                                | Example                          |
| ---------- | -------------------------------------- | -------------------------------- |
| Components | `PascalCaseComponent`                  | `PersonEditComponent`            |
| Services   | `PascalCaseService`                    | `PrivacyOrchestrationService`    |
| Models     | `PascalCaseModel/Request/Response`     | `OnboardingCompleteRequestModel` |
| Interfaces | `IPascalCase` or `PascalCaseInterface` | `IUserConsent`                   |
| Enums      | `PascalCase`                           | `ConsentType`                    |

### Variable/Method Naming

| Type       | Pattern                | Example                                   |
| ---------- | ---------------------- | ----------------------------------------- |
| Variables  | `camelCase`            | `currentStepIndex`, `userConsents`        |
| Methods    | `camelCase`            | `submitOnboarding()`, `withdrawConsent()` |
| Constants  | `SCREAMING_SNAKE_CASE` | `API_BASE_URL`, `MAX_RETRY_ATTEMPTS`      |
| Properties | `camelCase`            | `isConsented`, `consentTimestamp`         |

### Component Inheritance Patterns

**CRITICAL: Inherit from Base Components**

All new components should inherit from the appropriate base component to ensure consistency:

#### Page Components

```typescript
// my-page.component.ts
@Component({
  selector: "nom-my-page",
  templateUrl: "./my-page.component.html",
  styleUrls: ["./my-page.component.scss"],
})
export class MyPageComponent {
  pageConfig: BasePageConfig = {
    title: "My Page",
    subtitle: "Page description",
    showBackButton: true,
    fullCanvas: false,
  };
}
```

```html
<!-- my-page.component.html -->
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

#### Form Components

```typescript
// my-form.component.ts
@Component({
  selector: "nom-my-form",
  templateUrl: "./my-form.component.html",
  styleUrls: ["./my-form.component.scss"],
})
export class MyFormComponent {
  formConfig: BaseFormConfig = {
    title: "Create Item",
    subtitle: "Fill in the details",
    submitText: "Create",
    showCancelButton: true,
  };
}
```

```html
<!-- my-form.component.html -->
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

#### Detail Components

```typescript
// my-detail.component.ts
@Component({
  selector: "nom-my-detail",
  templateUrl: "./my-detail.component.html",
  styleUrls: ["./my-detail.component.scss"],
})
export class MyDetailComponent {
  detailConfig: BaseDetailConfig = {
    title: "Item Details",
    subtitle: "View item information",
    showBackButton: true,
    showEditButton: true,
    actions: [
      {
        label: "Delete",
        icon: "delete",
        color: "warn",
        action: () => this.onDelete(),
      },
    ],
  };
}
```

```html
<!-- my-detail.component.html -->
<nom-base-detail [config]="detailConfig" (back)="onBack()" (edit)="onEdit()">
  <!-- Detail content -->
</nom-base-detail>
```

#### List Components

```typescript
// my-list.component.ts
@Component({
  selector: "nom-my-list",
  templateUrl: "./my-list.component.html",
  styleUrls: ["./my-list.component.scss"],
})
export class MyListComponent {
  listConfig: BaseListConfig = {
    title: "My Items",
    subtitle: "Manage your items",
    showCreateButton: true,
    showSearch: true,
    showRefreshButton: true,
  };
}
```

```html
<!-- my-list.component.html -->
<nom-base-list
  [config]="listConfig"
  [isLoading]="isLoading"
  [error]="error"
  [isEmpty]="isEmpty"
  [searchControl]="searchControl"
  (create)="onCreate()"
  (refresh)="onRefresh()"
>
  <!-- List content -->
</nom-base-list>
```

### Benefits of Base Components

**Why Use Base Components:**

1. **Consistency**: All components follow the same patterns
2. **Reduced Repetition**: Common functionality is centralized
3. **Maintainability**: Changes to common patterns only need to be made once
4. **Accessibility**: Built-in accessibility features
5. **Responsive Design**: Consistent responsive behavior
6. **Error Handling**: Standardized error states and loading indicators

### Migration Strategy

**For New Components:**

- Always use base components from the start
- Follow the inheritance patterns shown above

**For Existing Components:**

- Gradually migrate to base components
- Start with the most commonly used components
- Refactor one component type at a time (all forms, then all details, etc.)

**Migration Priority:**

1. **CRITICAL**: Migrate all inline templates to separate HTML files
2. **CRITICAL**: Replace all structural directives with modern control flow
3. Form components (create/edit forms)
4. Detail components (view details)
5. List components (dashboards)
6. Page components (full pages)

**Migration Checklist:**

- [ ] Extract inline templates to separate `.html` files
- [ ] Replace `*ngIf` with `@if`
- [ ] Replace `*ngFor` with `@for`
- [ ] Replace `*ngSwitch` with `@switch`
- [ ] Add `track` expressions to all `@for` loops
- [ ] Update component decorators to use `templateUrl`
- [ ] Test all conditional rendering and iteration

### Desktop UI Viewport Requirements

**CRITICAL: Desktop UI Must Fit Standard Viewport Without Scrolling**

All desktop-targeted user interfaces MUST be designed to fit within a **1800x850px viewport** at 100% zoom without requiring horizontal or vertical scrolling.

**Viewport Specifications:**

- **Target Resolution**: 1080p (1920x1080)
- **Browser Window**: Maximized browser window
- **Effective Viewport**: ~1800x850px (accounting for browser UI, taskbar, etc.)
- **Zoom Level**: 100%
- **Scroll Requirement**: NO horizontal or vertical scrolling allowed

**Implementation Requirements:**

```scss
// Use viewport-relative units for height constraints
.main-container {
  height: calc(100vh - 120px); // Account for compact navigation header
  overflow: hidden; // Prevent scrolling
}

// Ensure content fits within boundaries
.content-area {
  max-height: calc(100vh - 180px);
  overflow-y: auto; // Only if absolutely necessary
}

// Compact Header Pattern (Recommended for Desktop Interfaces)
.compact-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--mat-sys-outline-variant);
  margin-bottom: 0.75rem;
  gap: 1rem;

  .header-title-section {
    flex-shrink: 0;
    // Title and subtitle on same vertical line
  }

  .header-center-section {
    flex: 1;
    // Stats, progress, or other dynamic content
  }

  .header-actions-section {
    flex-shrink: 0;
    // Action buttons and controls
  }
}
```

**Design Principles:**

1. **Compact Layouts**: Use efficient spacing and padding
2. **Smart Information Hierarchy**: Prioritize most important content
3. **Responsive Grid Systems**: Utilize available space effectively
4. **Collapsible Sections**: Use accordions/expandable areas when needed
5. **Optimized Typography**: Use appropriate font sizes for space efficiency
6. **Horizontal Information Density**: Place title, subtitle, and controls on same horizontal line when possible
7. **Eliminate Vertical Waste**: Reduce header height by 75% through compact design patterns

**Testing Requirements:**

- Test on 1080p monitor at 100% zoom
- Test with browser maximized
- Verify no scrollbars appear on primary workflows
- Ensure all interactive elements are accessible
- Verify header height is optimized (target: 25% of previous height)
- Confirm title, subtitle, and controls fit on single horizontal line

### Compact Header Pattern (Desktop Interfaces)

**CRITICAL: Use Compact Headers for Desktop Interfaces**

All desktop interfaces MUST use compact header patterns that place title, subtitle, and controls on the same horizontal line.

**Compact Header Requirements:**

- **Height Reduction**: Target 75% reduction in header height
- **Horizontal Layout**: Title, subtitle, and controls on single line
- **Space Efficiency**: Eliminate vertical waste in header sections
- **Information Density**: Maximize information per vertical pixel

**Implementation Pattern:**

```scss
.interface-header-compact {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 0;
  gap: 1rem;

  .header-title-section {
    flex-shrink: 0;
    // Title + subtitle vertically stacked
  }

  .header-center-section {
    flex: 1;
    // Dynamic content (stats, progress, etc.)
  }

  .header-actions-section {
    flex-shrink: 0;
    // Action buttons and controls
  }
}
```

**Examples of Interfaces Requiring Compact Headers:**

- Curation Queue ✅ (Completed) - Uses Base-List Integration Pattern
- Recipe Management 🔄 (TODO: Implement compact header)
- User Management 🔄 (TODO: Implement compact header)
- Household Management 🔄 (TODO: Implement compact header)
- Dashboard 🔄 (TODO: Implement compact header)
- Recipe Search 🔄 (TODO: Implement compact header)
- Meal Plan Management 🔄 (TODO: Implement compact header)
- Shopping Lists 🔄 (TODO: Implement compact header)
- Profile Management 🔄 (TODO: Implement compact header)

**TODO: Compact Header Implementation Priority**

1. **Phase 1 (Critical Admin)**: Recipe Management, User Management, Household Management
2. **Phase 2 (Content)**: Recipe Search, Meal Plan Management, Shopping Lists
3. **Phase 3 (User-facing)**: Dashboard, Profile Management

**Implementation Strategy:**

**For List Interfaces (extending BaseListComponent):**

- ✅ **Use Base-List Integration Pattern** (like Curation Queue)
- ✅ **Extend BaseListConfig** with stats, progress, custom actions
- ✅ **Eliminate duplicate headers** by integrating into base-list

**For Other Interfaces:**

- 🔄 **Use Direct Component Implementation Pattern**
- 🔄 **Create custom compact header layouts**
- 🔄 **Follow the same three-section structure**

### **Base-List Integration Pattern (Recommended for List Interfaces)**

**When to Use:**

- Interface extends `BaseListComponent`
- Has list-like functionality (search, pagination, etc.)
- Needs stats, progress, or custom actions

**Implementation Steps:**

1. **Extend BaseListConfig** with required properties
2. **Remove duplicate header** from component template
3. **Update listConfig** when data changes
4. **Use base-list header** for all title/subtitle/controls

**Example Configuration:**

```typescript
listConfig: BaseListConfig = {
  title: "Interface Title",
  subtitle: "Interface description",
  showStats: true,
  stats: [{ label: "Count", value: 0, type: "pending" }],
  showProgress: true,
  progressText: "Current status",
  progressValue: 0,
  progressTotal: 0,
  showCustomActions: true,
  customActions: [
    {
      label: "Action",
      icon: "icon_name",
      color: "primary",
      action: () => this.performAction(),
    },
  ],
  showLastUpdated: true,
  lastUpdated: new Date(),
};
```

**Benefits:**

- ✅ **No duplicate headers** - Single source of truth
- ✅ **Consistent styling** - Uses base-list component styles
- ✅ **Reusable pattern** - Can be applied to other list interfaces
- ✅ **Better maintainability** - Centralized header logic
- ✅ **Space efficiency** - Eliminates vertical waste

### **Control Buttons Pattern (Curation Interfaces)**

**When to Use:**

- Curation and review interfaces
- Interfaces with primary action buttons
- Need for always-visible controls

**Implementation:**

```typescript
listConfig: BaseListConfig = {
  // ... other config
  showControlButtons: true,
  controlButtons: [
    {
      label: "Approve",
      icon: "check_circle",
      color: "primary",
      disabled: !this.selectedItem || this.form.invalid,
      action: () => this.approve(),
    },
    {
      label: "Request Revision",
      icon: "edit",
      color: "accent",
      disabled: !this.selectedItem || this.form.invalid,
      action: () => this.requestRevision(),
    },
  ],
};
```

**Layout Pattern:**

- **Control buttons** (Approve, Request Revision, Reject, Cancel) positioned **left** of navigation buttons
- **Navigation buttons** (Previous, Next) positioned **right** of control buttons
- **Same horizontal line** for all action buttons
- **Refresh section** with timestamp **left** of refresh button for compact display
- **Accordion content** with fixed maximum height of 250px for scrollable sections
- **Single-line titles** combining action and item name: "Review [EntityType] - [ItemName]"
- **Horizontal form fields** with 50/50 width split for side-by-side input areas
- **Header action integration** with control menu and navigation buttons in review headers

**Benefits:**

- ✅ **Always visible** - Controls never hidden by scrolling
- ✅ **Quick access** - Primary actions in header
- ✅ **Context-aware** - Automatically disabled when invalid
- ✅ **Consistent layout** - Same position across interfaces
- ✅ **Logical grouping** - Control actions grouped with navigation actions

### **Compact Header Stats Pattern (List Interfaces)**

**When to Use:**

- List interfaces with statistics and progress information
- Need to maximize horizontal space usage
- Want to reduce vertical header height

**Implementation:**

```typescript
listConfig: BaseListConfig = {
  // ... other config
  showStats: true,
  stats: [
    { label: "Pending", value: 5, type: "pending" },
    { label: "Recipes", value: 3, type: "recipe" },
    { label: "Ingredients", value: 2, type: "ingredient" },
  ],
  showProgress: true,
  progressText: "Reviewing item 1 of 5",
};
```

**Layout Pattern:**

- **Stats badges** and **progress text** on the same horizontal line
- **Center-aligned** within the header center section
- **Responsive wrapping** on smaller screens
- **Proper spacing** between elements (1rem gap)

**Benefits:**

- ✅ **Space efficient** - Reduces header height by 50%
- ✅ **Better information density** - More data in less space
- ✅ **Improved scanability** - Related information grouped together
- ✅ **Responsive design** - Adapts to different screen sizes

### **Accordion Content Pattern (Content Organization)**

**When to Use:**

- Content-heavy interfaces
- Multiple information sections
- Need to reduce vertical space

**Implementation:**

```html
<amw-expansion-panel [config]="{ title: 'Section Title' }">
  <!-- Section content -->
</amw-expansion-panel>
```

**Scrollable Content Pattern:**

```scss
// Make accordion content scrollable with fixed maximum height
::ng-deep .mat-mdc-expansion-panel-content {
  .mat-mdc-expansion-panel-body {
    max-height: 250px; // Fixed height limit to prevent unnecessary empty space
    overflow-y: auto;
    overflow-x: hidden;

    // Custom scrollbar styling
    &::-webkit-scrollbar {
      width: 6px;
    }

    &::-webkit-scrollbar-track {
      background: var(--mat-sys-surface-container-low);
      border-radius: 3px;
    }

    &::-webkit-scrollbar-thumb {
      background: var(--mat-sys-outline-variant);
      border-radius: 3px;

      &:hover {
        background: var(--mat-sys-outline);
      }
    }
  }
}
```

**Benefits:**

- ✅ **Space efficient** - Reduces vertical scrolling
- ✅ **Better organization** - Groups related information
- ✅ **Improved scanability** - Users can focus on relevant sections
- ✅ **Consistent styling** - Material Design 3 accordion theming
- ✅ **Scrollable content** - Long content contained within viewport constraints
- ✅ **Custom scrollbars** - Consistent with Material Design 3 theming

### **Horizontal Form Fields Pattern (Space Efficiency)**

**When to Use:**

- Multiple related form fields that can share horizontal space
- Need to reduce vertical form height
- Fields have similar importance and usage patterns

**Implementation:**

```html
<div class="form-fields-row">
  <amw-input
    [config]="{ label: 'First Field', formControlName: 'field1', type: 'textarea', rows: 3 }"
    class="form-field form-field--half" />

  <amw-input
    [config]="{ label: 'Second Field', formControlName: 'field2', type: 'textarea', rows: 3 }"
    class="form-field form-field--half" />
</div>
```

**CSS Pattern:**

```scss
.form-fields-row {
  display: flex;
  gap: 1rem;
  width: 100%;
  box-sizing: border-box;

  @include mixins.breakpoint(mobile) {
    flex-direction: column;
    gap: 0.5rem;
  }
}

.form-field--half {
  flex: 1;
  min-width: 0;
}
```

**Benefits:**

- ✅ **Space efficient** - Reduces vertical form height
- ✅ **Better organization** - Related fields grouped horizontally
- ✅ **Responsive** - Stacks vertically on mobile devices
- ✅ **Equal width distribution** - Each field gets 50% of available width

### **Universal Scrolling Pattern (Footer Collision Prevention)**

**🚨 CRITICAL: Universal Rule for All Components**

**When to Use:**

- **ALWAYS** when component content approaches within 10px of footer
- Components with dynamic content that can grow vertically
- Components with bottom action buttons
- Any page that might overflow the viewport

**Universal Rule:**

When any component content approaches the footer at 10px above it, the content must have vertical scrolling. If there are buttons at the bottom of the component, the content above the buttons should scroll while keeping buttons fixed.

**Implementation Pattern:**

```html
<!-- Component with fixed header and scrollable content -->
<div class="component-container">
  <!-- Fixed header/controls (always visible) -->
  <div class="component-header">
    <h2>Section Title</h2>
    <button>Action Button</button>
  </div>

  <!-- Scrollable content area -->
  <div class="nom-scroll-container__content">
    <!-- Content items that can scroll -->
    <div class="content-item">Item 1</div>
    <div class="content-item">Item 2</div>
    <!-- ... more items -->
  </div>
</div>

<!-- Full page with scrollable sections -->
<div class="nom-scroll-container nom-scroll-container--dashboard">
  <!-- Fixed page header -->
  <div class="page-header">
    <h1>Page Title</h1>
    <div class="stats-pills">...</div>
  </div>

  <!-- Page content with individual scrollable sections -->
  <div class="page-content">
    <div class="section">
      <div class="section-header">
        <h2>My Items</h2>
        <button>New Item</button>
      </div>
      <!-- Only this content scrolls -->
      <div class="nom-scroll-container__content">
        <!-- Scrollable items -->
      </div>
    </div>
  </div>
</div>

<!-- Component with fixed bottom actions -->
<div class="nom-scroll-container nom-scroll-container--with-actions">
  <div class="fixed-header">
    <!-- Fixed header content -->
  </div>
  <div
    class="nom-scroll-container__content nom-scroll-container__content--full"
  >
    <!-- Scrollable content -->
  </div>
  <div class="nom-scroll-container__actions">
    <!-- Fixed bottom actions -->
  </div>
</div>
```

**Available Variants:**

| Variant          | Use Case                       | Height Calculation         |
| ---------------- | ------------------------------ | -------------------------- |
| Base             | Standard pages                 | `calc(100vh - 120px)`      |
| `--dashboard`    | Dashboard layouts              | `calc(100vh - 140px)`      |
| `--modal`        | Modal/dialog content           | `calc(100vh - 200px)`      |
| `--with-actions` | Components with bottom buttons | Same height, fixed actions |

**Content Area Variants:**

| Content Variant | Use Case                    | Max Height                |
| --------------- | --------------------------- | ------------------------- |
| Default         | Individual content sections | `300px`                   |
| `--small`       | Compact content areas       | `200px`                   |
| `--large`       | Extended content areas      | `500px`                   |
| `--full`        | Full height (original)      | `flex: 1` (no max-height) |

**CSS Implementation (in `_styles.scss`):**

```scss
.nom-scroll-container {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 120px);
  max-height: calc(100vh - 120px);
  overflow: hidden;

  &__content {
    flex: 1;
    overflow-y: auto;
    overflow-x: hidden;
    min-height: 0;
    padding-bottom: 10px; // Minimum space above footer
  }

  &__actions {
    flex-shrink: 0;
    padding: 1rem 0;
    border-top: 1px solid var(--mat-sys-outline-variant);
    background: var(--mat-sys-surface);
    margin-top: auto;
  }
}
```

**Key Principles:**

1. **Fixed Headers/Controls**: Section headers and action buttons remain static and always visible
2. **Targeted Scrolling**: Only content areas scroll, not entire sections
3. **Flexible Heights**: Different max-heights for different content types
4. **Consistent Behavior**: Same scrolling pattern across all components

**Benefits:**

- ✅ **Prevents content cutoff** - Content never gets hidden by footer
- ✅ **Fixed navigation** - Headers and controls always accessible
- ✅ **Consistent UX** - Same behavior across all components
- ✅ **Responsive** - Adapts to different screen sizes
- ✅ **Accessible** - Custom scrollbar styling
- ✅ **Reusable** - Single utility class system
- ✅ **Action preservation** - Bottom buttons always visible
- ✅ **Focused scrolling** - Only content scrolls, headers stay put

**Migration Requirements:**

- **MANDATORY**: All existing components must be updated to use this pattern
- **Priority**: Start with most frequently used components
- **Testing**: Verify 10px minimum space above footer on all screen sizes

### **Header Action Integration Pattern (Review Interfaces)**

**When to Use:**

- Review/curation interfaces with multiple action types
- Need to consolidate control buttons and navigation in header
- Want to maintain clean, organized header layout

**Implementation:**

```html
<amw-card [config]="{ title: 'Review ' + entityType + ' - ' + entityName }">
  <div class="review-header__actions" slot="actions">
    <!-- Control Buttons Context Menu -->
    <amw-button
      [config]="{ icon: 'more_vert', variant: 'icon' }"
      [matMenuTriggerFor]="controlMenu"
      class="control-menu-trigger"
      aria-label="Open control menu" />

    <amw-menu #controlMenu class="control-menu">
      <amw-menu-item (click)="approve()">
        <amw-icon [config]="{ icon: 'check_circle' }" />
        <span>Approve</span>
      </amw-menu-item>
      <amw-menu-item (click)="requestRevision()">
        <amw-icon [config]="{ icon: 'edit' }" />
        <span>Request Revision</span>
      </amw-menu-item>
      <amw-menu-item (click)="reject()">
        <amw-icon [config]="{ icon: 'cancel' }" />
        <span>Reject</span>
      </amw-menu-item>
      <amw-menu-item (click)="cancel()">
        <amw-icon [config]="{ icon: 'close' }" />
        <span>Cancel</span>
      </amw-menu-item>
    </amw-menu>

    <!-- Navigation Buttons -->
    <div class="review-header__navigation">
      <amw-button [config]="{ icon: 'keyboard_arrow_up', variant: 'icon' }" (clicked)="previous()" />
      <span class="navigation-counter">1 of 5</span>
      <amw-button [config]="{ icon: 'keyboard_arrow_down', variant: 'icon' }" (clicked)="next()" />
    </div>
  </div>
</amw-card>
```

**CSS Pattern:**

```scss
.review-header__actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-shrink: 0;

  .control-menu-trigger {
    width: 32px;
    height: 32px;
    line-height: 32px;
    color: var(--mat-sys-on-surface-variant);

    &:hover {
      color: var(--mat-sys-on-surface);
      background-color: var(--mat-sys-surface-container);
    }
  }

  .review-header__navigation {
    display: flex;
    align-items: center;
    gap: 0.375rem;
  }
}
```

**Benefits:**

- ✅ **Clean header layout** - Actions organized in logical groups
- ✅ **Space efficient** - Control buttons consolidated in context menu
- ✅ **Better UX** - Related actions grouped together
- ✅ **Responsive design** - Adapts to different screen sizes
- ✅ **Material Design 3** - Consistent with design system

### Angular Component Structure

**CRITICAL: Component HTML Must Be in Separate Files**

All Angular components MUST have their HTML templates in separate `.html` files. NEVER use inline templates.

```typescript
@Component({
  selector: "nom-person-edit",
  standalone: true,
  templateUrl: './person-edit.component.html',  // ✅ REQUIRED
  styleUrls: ['./person-edit.component.scss']
})
```

**❌ FORBIDDEN:**

```typescript
@Component({
  template: `...`  // ❌ NEVER use inline templates
})
```

### Modern Angular Control Flow

**CRITICAL: Use Modern Control Flow Syntax**

All components MUST use the modern Angular control flow syntax instead of structural directives.

#### ✅ REQUIRED: Modern Control Flow

```html
<!-- Conditional Rendering -->
@if (condition) {
<div>Content</div>
} @if (condition) {
<div>True content</div>
} @else {
<div>False content</div>
}

<!-- Iteration -->
@for (item of items; track item.id) {
<div>{{ item.name }}</div>
}

<!-- Switch -->
@switch (value) { @case ('option1') {
<div>Option 1</div>
} @case ('option2') {
<div>Option 2</div>
} @default {
<div>Default</div>
} }

<!-- Deferrable -->
@defer {
<heavy-component />
} @loading {
<loading-spinner />
} @error {
<error-message />
}

<!-- Let -->
@let item = getItem(); track item.id {
<div>{{ item.name }}</div>
}
```

#### ❌ FORBIDDEN: Structural Directives

```html
<!-- ❌ NEVER use these -->
<div *ngIf="condition">Content</div>
<div *ngFor="let item of items">Content</div>
<div *ngSwitch="value">Content</div>
```

#### Migration Priority

**CRITICAL: All components must be migrated to use modern control flow syntax**

1. **Replace `*ngIf` with `@if`** - All conditional rendering
2. **Replace `*ngFor` with `@for`** - All iteration with proper tracking
3. **Replace `*ngSwitch` with `@switch`** - All switch statements
4. **Add `track` expressions** - All `@for` loops must include tracking
5. **Test thoroughly** - Ensure all conditional rendering and iteration works correctly

#### Benefits of Modern Control Flow

1. **Performance**: Better tree-shaking and runtime performance
2. **Type Safety**: Better TypeScript integration and type checking
3. **Future-Proof**: Angular's direction is moving away from structural directives
4. **Cleaner Syntax**: More readable and maintainable code
5. **Better Error Messages**: More specific error reporting

### Benefits of Modern Angular Patterns

**Why Use Separate HTML Files:**

1. **Better IDE Support**: Syntax highlighting, autocomplete, and error detection
2. **Easier Maintenance**: HTML and TypeScript are clearly separated
3. **Better Collaboration**: Designers can work on HTML without touching TypeScript
4. **Improved Readability**: Templates are easier to read when not embedded in strings
5. **Better Tooling**: Linters, formatters, and other tools work better with separate files

**Why Use Modern Control Flow:**

1. **Performance**: Better tree-shaking and runtime performance
2. **Type Safety**: Better TypeScript integration and type checking
3. **Future-Proof**: Angular's direction is moving away from structural directives
4. **Cleaner Syntax**: More readable and maintainable code
5. **Better Error Messages**: More specific error reporting

### Angular Component Structure

```typescript
@Component({
  selector: "nom-person-edit",
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: "./person-edit.component.html",
  styleUrls: ["./person-edit.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class PersonEditComponent implements OnInit {
  @Input() person: PersonModel | null = null;
  @Output() formSubmitted = new EventEmitter<PersonModel>();
  @Output() skipStep = new EventEmitter<void>();

  personForm!: FormGroup;

  constructor(private fb: NonNullableFormBuilder) {}

  ngOnInit(): void {
    // Component initialization
  }

  public submitForm(): void {
    // Form submission logic
  }
}
```

### Model Instantiation Pattern

```typescript
// ✅ CORRECT: Explicit property-by-property assignment
export class PersonModel implements IPersonModel {
  id?: number;
  name: string;
  attributes?: PersonAttributeModel[];

  constructor(data: Partial<IPersonModel> = {}) {
    this.id = data.id || 0;
    this.name = data.name || "";
    this.attributes = data.attributes || [];
  }
}
```

### Service Pattern

```typescript
@Injectable({
  providedIn: "root",
})
export class PersonService {
  private readonly apiUrl = "/api/Person";

  constructor(private http: HttpClient) {}

  submitOnboardingComplete(
    request: OnboardingCompleteRequestModel
  ): Observable<ApiResponseCommonModel> {
    return this.http.post<ApiResponseCommonModel>(
      `${this.apiUrl}/onboarding-complete`,
      request
    );
  }
}
```

## Backend Conventions (C#/.NET)

### File Naming

| Type        | Pattern                               | Example                           |
| ----------- | ------------------------------------- | --------------------------------- |
| Controllers | `PascalCaseController.cs`             | `PrivacyController.cs`            |
| Services    | `PascalCaseService.cs`                | `ConsentManagementService.cs`     |
| Entities    | `PascalCaseEntity.cs`                 | `UserConsentEntity.cs`            |
| Models      | `PascalCaseModel/Request/Response.cs` | `DataExportRequest.cs`            |
| Interfaces  | `IPascalCase.cs`                      | `IPrivacyOrchestrationService.cs` |

### Class/Interface Naming

| Type        | Pattern                            | Example                     |
| ----------- | ---------------------------------- | --------------------------- |
| Controllers | `PascalCaseController`             | `PrivacyController`         |
| Services    | `PascalCaseService`                | `DataAnonymizationService`  |
| Entities    | `PascalCaseEntity`                 | `DataProcessingLogEntity`   |
| Models      | `PascalCaseModel/Request/Response` | `ConsentUpdateRequest`      |
| Interfaces  | `IPascalCase`                      | `IConsentManagementService` |

### Method/Property Naming

| Type       | Pattern                                 | Example                           |
| ---------- | --------------------------------------- | --------------------------------- |
| Methods    | `PascalCase`                            | `ProcessDataExportRequestAsync()` |
| Properties | `PascalCase`                            | `ConsentTimestamp`, `IsConsented` |
| Fields     | `camelCase` or `_camelCase` for private | `_context`, `_logger`             |
| Constants  | `PascalCase`                            | `DefaultRetentionPeriod`          |

### Entity Pattern

```csharp
[Table("Person", Schema = "person")]
public class PersonEntity : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public virtual ICollection<PlanParticipantEntity> PlanParticipations { get; set; } = new List<PlanParticipantEntity>();
    public virtual ICollection<PersonAttributeEntity> Attributes { get; set; } = new List<PersonAttributeEntity>();
    public virtual ICollection<RestrictionEntity> Restrictions { get; set; } = new List<RestrictionEntity>();
}
```

### Orchestration Service Pattern

```csharp
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
    private readonly ILogger<PersonOrchestrationService> _logger;

    public PersonOrchestrationService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IPrivacyOrchestrationService privacyOrchestrationService,
        ILogger<PersonOrchestrationService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _privacyOrchestrationService = privacyOrchestrationService;
        _logger = logger;
    }

    public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
    {
        // Business logic implementation
    }
}
```

### Controller Pattern

```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PersonController : BaseApiController
{
    private readonly IPersonOrchestrationService _personOrchestrationService;
    private readonly ILogger<PersonController> _logger;

    public PersonController(
        IPersonOrchestrationService personOrchestrationService,
        ILogger<PersonController> logger)
    {
        _personOrchestrationService = personOrchestrationService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertPerson([FromBody] PersonCreateModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var response = await _personOrchestrationService.UpsertPersonAsync(model);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in UpsertPerson.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
        }
    }
}
```

### Model Pattern

```csharp
public class PersonCreateModel
{
    [Required(ErrorMessage = "Person name is required.")]
    [StringLength(256, ErrorMessage = "Person name cannot exceed 256 characters.")]
    public required string PersonName { get; set; }
}
```

## Database Conventions

### Table Naming

| Type            | Pattern                            | Example                            |
| --------------- | ---------------------------------- | ---------------------------------- |
| Tables          | `PascalCase` matching entity names | `UserConsent`, `DataProcessingLog` |
| Junction tables | `Entity1Entity2`                   | `PersonPlan`, `ReferenceGroup`     |

### Column Naming

| Type         | Pattern                              | Example                        |
| ------------ | ------------------------------------ | ------------------------------ |
| Columns      | `PascalCase` matching property names | `ConsentTimestamp`, `PersonId` |
| Foreign keys | `ReferencedEntityId`                 | `PersonId`, `PlanId`           |
| Primary keys | `Id` (simple and consistent)         | `Id`                           |

### Schema Organization

```sql
-- Person-related tables
person.Person
person.PersonAttribute
person.Invitation

-- Plan-related tables
plan.Plan
plan.PlanParticipant
plan.Restriction

-- Privacy-related tables
privacy.UserConsent
privacy.DataProcessingLog
privacy.PrivacyRequest

-- Recipe-related tables
recipe.Recipe
recipe.RecipeIngredient
recipe.Ingredient

-- Curation-related tables
curation.CurationFeedback

-- Communication-related tables
communication.MessageThread
communication.MessageThreadParticipant
communication.Message
```

## CSS/SCSS Conventions

### BEM Methodology

| Type     | Pattern                        | Example                                |
| -------- | ------------------------------ | -------------------------------------- |
| Block    | `nom-component-name`           | `.nom-privacy-dashboard`               |
| Element  | `nom-component-name__element`  | `.nom-privacy-dashboard__consent-item` |
| Modifier | `nom-component-name--modifier` | `.nom-button--primary`                 |

### Global Classes

| Type    | Pattern            | Example                                 |
| ------- | ------------------ | --------------------------------------- |
| Utility | `nom-utility-name` | `.nom-text-center`, `.nom-margin-large` |
| Layout  | `nom-layout-name`  | `.nom-page-container`, `.nom-grid`      |

### Material 3 Theming

```scss
// Theme variables usage
.nom-card {
  background-color: var(--mat-sys-surface-container);
  color: var(--mat-sys-on-surface);
  box-shadow: 0 4px 8px rgba(var(--mat-sys-shadow), 0.1);
}

.nom-form {
  &__field {
    // Form field styling
  }

  &__input {
    // Input styling
  }

  &__error {
    // Error message styling
  }
}
```

### Component SCSS Structure

```scss
// Component-specific styles
.nom-person-edit {
  &__form {
    // Form container styles
  }

  &__field {
    // Field styles
  }

  &__button {
    // Button styles

    &--primary {
      // Primary button modifier
    }
  }
}
```

## API Conventions

### REST Endpoint Naming

| Type      | Pattern                          | Example                    |
| --------- | -------------------------------- | -------------------------- |
| Resources | `kebab-case`                     | `/api/privacy/data-export` |
| Actions   | `camelCase` for query parameters | `?includeArchived=true`    |
| Versions  | `v1`, `v2` prefix when needed    | `/api/v1/privacy/consents` |

### HTTP Status Codes

| Code | Usage                    | Example                                     |
| ---- | ------------------------ | ------------------------------------------- |
| 200  | Successful GET/PUT/PATCH | `return Ok(response)`                       |
| 201  | Successful POST          | `return CreatedAtAction(...)`               |
| 400  | Bad Request              | `return BadRequest(ModelState)`             |
| 401  | Unauthorized             | `[Authorize]` attribute                     |
| 403  | Forbidden                | `[Authorize(Policy = "CanManageCuration")]` |
| 404  | Not Found                | `return NotFound()`                         |
| 500  | Internal Server Error    | `return StatusCode(500, ...)`               |

### Authentication Schemes

```csharp
// Dual Bearer token support
services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    // JWT configuration
});
```

## File Structure Conventions

### 🚨 CRITICAL: Atomic File Organization

**ALWAYS PLACE** each class, interface, and enum in its own separate file.

**NEVER COMBINE** multiple classes, interfaces, or enums in a single file.

**APPLIES TO** both C# and TypeScript files throughout the entire codebase.

**MODEL FILES MUST END WITH** `.model.ts` for classes and `.model.interface.ts` for interfaces.

**Examples:**

```typescript
// ✅ CORRECT: Single class per file
// person.model.ts
export class PersonModel {
  // Person model implementation
}

// ✅ CORRECT: Single interface per file
// person.model.interface.ts
export interface IPersonModel {
  // Person interface definition
}

// ✅ CORRECT: Single enum per file
// person-status.enum.ts
export enum PersonStatus {
  Active = "active",
  Inactive = "inactive",
}

// ❌ FORBIDDEN: Multiple classes in one file
// person.ts - DON'T DO THIS
export class PersonModel {}
export class PersonService {}
export interface IPerson {}
export enum PersonStatus {}
```

```csharp
// ✅ CORRECT: Single class per file
// PersonEntity.cs
public class PersonEntity : BaseEntity
{
    // Person entity implementation
}

// ✅ CORRECT: Single interface per file
// IPersonOrchestrationService.cs
public interface IPersonOrchestrationService
{
    // Service contract definition
}

// ✅ CORRECT: Single enum per file
// PersonStatusEnum.cs
public enum PersonStatusEnum
{
    Active,
    Inactive
}

// ❌ FORBIDDEN: Multiple classes in one file
// Person.cs - DON'T DO THIS
public class PersonEntity { }
public class PersonService { }
public interface IPersonService { }
public enum PersonStatus { }
```

### TypeScript Model Naming Conventions

**MODEL CLASSES MUST END WITH** `.model.ts`:

- `household.model.ts`
- `person-create-request.model.ts`
- `meal-plan-response.model.ts`

**MODEL INTERFACES MUST END WITH** `.model.interface.ts`:

- `household.model.interface.ts`
- `person-create-request.model.interface.ts`
- `meal-plan-response.model.interface.ts`

### Import Patterns

**ALWAYS IMPORT DIRECTLY** from the specific model file:

```typescript
// ✅ CORRECT: Direct imports
import { HouseholdModel } from "./models/household.model";
import { IHouseholdModel } from "./models/household.model.interface";
import { MealPlanCreateRequestModel } from "./models/meal-plan-create-request.model";
import { IMealPlanCreateRequestModel } from "./models/meal-plan-create-request.model.interface";

// ❌ FORBIDDEN: Barrel file imports
import { HouseholdModel } from "./models/household.classes";
import { IHouseholdModel } from "./models/household.interfaces";
```

**NEVER CREATE** centralized export files (barrel files) for models:

- ❌ `household.classes.ts`
- ❌ `household.interfaces.ts`
- ❌ `meal-plan.classes.ts`
- ❌ `meal-plan.interfaces.ts`

**WHY AVOID BARREL FILES FOR MODELS:**

- **Unnecessary Complexity**: Adds extra layer of indirection
- **Maintenance Overhead**: Must keep export lists in sync with actual files
- **Import Confusion**: Developers unsure whether to import from barrel or actual file
- **Tree Shaking Issues**: Can prevent bundlers from properly tree-shaking unused code
- **Circular Dependencies**: Can create circular import issues
- **IDE Performance**: Slower autocomplete and navigation

**EXAMPLES:**

```typescript
// ✅ CORRECT: Model class file
// household.model.ts
export class HouseholdModel implements IHouseholdModel {
  // Implementation
}

// ✅ CORRECT: Model interface file
// household.model.interface.ts
export interface IHouseholdModel {
  // Interface definition
}

// ✅ CORRECT: Request model
// household-create-request.model.ts
export class HouseholdCreateRequestModel
  implements IHouseholdCreateRequestModel {
  // Implementation
}

// ✅ CORRECT: Request interface
// household-create-request.model.interface.ts
export interface IHouseholdCreateRequestModel {
  // Interface definition
}
```

### Frontend Structure (nom-ui)

```
src/app/
├── common/                    # Shared utilities and components
│   ├── components/
│   ├── models/
│   ├── pipes/
│   └── services/
├── person/                    # Domain-specific modules
│   ├── components/
│   ├── models/
│   ├── services/
│   └── guards/
├── privacy/                   # Privacy domain
├── recipe/                    # Recipe domain
├── curation/                  # Curation domain
├── communication/             # Communication domain
├── admin/                     # Admin functionality
├── auth/                      # Authentication components
├── utilities/                 # Utility services
└── shared/                    # Shared components
```

### Backend Structure (nom-api)

```
Nom.Api/
├── Controllers/               # API endpoints
├── Authentication/            # Auth configuration
├── Properties/               # Configuration files
└── Program.cs                # Application entry point

Nom.Data/
├── Person/                   # Domain entities
├── Plan/
├── Privacy/
├── Recipe/
├── Curation/
├── Communication/
├── Reference/
├── Audit/
├── Nutrient/
├── Shopping/
└── ApplicationDbContext.cs   # Database context

Nom.Orch/
├── Services/                 # Business logic services
├── Interfaces/               # Service contracts
├── Models/                   # Request/Response models
│   ├── Person/
│   ├── Privacy/
│   ├── Recipe/
│   ├── Curation/
│   └── Communication/
├── UtilityServices/          # Utility services
├── UtilityInterfaces/        # Utility interfaces
└── Enums/                   # Enumerations
```

## Component Architecture Patterns

### Domain-Driven Structure

Each domain has its own directory with consistent subdirectories:

```
domain-name/
├── components/               # Angular components
│   └── component-name/
│       ├── component-name.component.ts
│       ├── component-name.component.html
│       └── component-name.component.scss
├── models/                   # TypeScript interfaces and classes
│   ├── domain-name.model.ts
│   ├── domain-name-request.model.ts
│   └── domain-name-response.model.ts
├── services/                 # Angular services
│   └── domain-name.service.ts
└── guards/                   # Route guards (if applicable)
```

### Component Communication Pattern

```typescript
// Parent component
export class OnboardingWorkflowComponent {
  @Output() stepCompleted = new EventEmitter<any>();

  onStepSubmit(data: any) {
    this.stepCompleted.emit(data);
  }
}

// Child component
export class PersonEditComponent {
  @Input() person: PersonModel | null = null;
  @Output() formSubmitted = new EventEmitter<PersonModel>();

  submitForm() {
    this.formSubmitted.emit(updatedPerson);
  }
}
```

### Service Injection Pattern

```typescript
// Domain service
@Injectable({
  providedIn: "root",
})
export class PersonService {
  private readonly apiUrl = "/api/Person";

  constructor(private http: HttpClient) {}
}

// Utility service
@Injectable({
  providedIn: "root",
})
export class NotificationService {
  success(message: string) {
    /* implementation */
  }
  error(message: string) {
    /* implementation */
  }
}
```

## Service Architecture Patterns

### Domain vs Utility Service Pattern

**DOMAIN SERVICES** handle specific business domain API calls:

```typescript
// ✅ CORRECT: Domain service (AuthService)
@Injectable({ providedIn: "root" })
export class AuthService {
  private readonly apiUrl = "/api/auth";

  constructor(private httpClient: HttpClient) {}

  login(credentials: LoginUser): Observable<LoginResponse> {
    return this.httpClient.post<LoginResponse>(
      `${this.apiUrl}/login`,
      credentials
    );
  }

  register(userData: RegisterUser): Observable<void> {
    return this.httpClient.post<void>(`${this.apiUrl}/register`, userData);
  }
}
```

**UTILITY SERVICES** handle cross-cutting concerns and state management:

```typescript
// ✅ CORRECT: Utility service (AuthManagerService)
@Injectable({ providedIn: "root" })
export class AuthManagerService {
  public userLogin = new BehaviorSubject<boolean>(false);

  constructor(
    private authService: AuthService, // Uses domain service
    private router: Router,
    private notificationService: NotificationService
  ) {}

  login(credentials: LoginUser): Observable<LoginResponse> {
    return this.authService.login(credentials).pipe(
      tap((response) => {
        this.storeAuthData(response);
        this.notificationService.success("Logged in successfully!");
      })
    );
  }
}
```

### Orchestration Service Pattern

```csharp
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
    private readonly ILogger<PersonOrchestrationService> _logger;

    public async Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request)
    {
        // 1. Validate input
        // 2. Perform business logic
        // 3. Coordinate with other services
        // 4. Update database
        // 5. Return response
    }
}
```

### Event-Driven Communication

```typescript
// Event bus service
@Injectable({
  providedIn: "root",
})
export class EventBusService {
  private eventSubject = new Subject<any>();

  emit(event: string, data?: any) {
    this.eventSubject.next({ event, data });
  }

  on(event: string, callback: (data?: any) => void) {
    return this.eventSubject
      .pipe(filter(({ event: emittedEvent }) => emittedEvent === event))
      .subscribe(({ data }) => callback(data));
  }
}
```

### IoC Architecture

```csharp
// Service registration
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
    {
        services.AddScoped<IPersonOrchestrationService, PersonOrchestrationService>();
        services.AddScoped<IPrivacyOrchestrationService, PrivacyOrchestrationService>();
        // ... other services
        return services;
    }
}
```

## Security & Privacy Conventions

### Authentication Patterns

```csharp
// Controller authorization
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PrivacyController : BaseApiController
{
    [HttpPost("data-export")]
    public async Task<IActionResult> RequestDataExport([FromBody] DataExportRequest request)
    {
        // Implementation
    }
}
```

### Claims-Based Authorization

```csharp
// Policy-based authorization
[Authorize(Policy = "CanManageCuration")]
public class CurationController : BaseApiController
{
    [HttpPost("approve")]
    public async Task<IActionResult> ApproveSubmission([FromBody] ApprovalRequest request)
    {
        // Implementation
    }
}
```

### Privacy Compliance Patterns

```csharp
// Consent management
public class UserConsentEntity : BaseEntity
{
    public long PersonId { get; set; }
    public string ConsentType { get; set; } = string.Empty;
    public bool IsConsented { get; set; }
    public DateTime ConsentTimestamp { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
}

// Audit logging
public class DataProcessingLogEntity : BaseEntity
{
    public long PersonId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public long? ActorId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
```

## Forbidden Patterns

### Naming Conventions

| ❌ NEVER USE                                           | ✅ ALWAYS USE                                                     |
| ------------------------------------------------------ | ----------------------------------------------------------------- |
| `DTO`, `Dto`, `dto` suffixes                           | `Model`, `Request`, `Response` suffixes                           |
| Hungarian notation (`strUserName`, `intCount`)         | Descriptive, full words (`ConsentManagementService`)              |
| Abbreviations in public APIs (`usr` instead of `user`) | Consistent suffix patterns                                        |
| Inconsistent casing within same codebase               | Clear semantic naming (`withdrawConsent()` not `removeConsent()`) |

### Code Patterns

| ❌ AVOID                           | ✅ PREFER                                 |
| ---------------------------------- | ----------------------------------------- |
| Direct DOM manipulation in Angular | Angular's data binding and reactive forms |
| Complex logic in HTML templates    | TypeScript methods and getters            |
| Hardcoded colors and values        | Material 3 theme variables                |
| Tight coupling between services    | Event-driven communication                |
| Large, monolithic components       | Small, focused components                 |
| Inline styles                      | SCSS with BEM methodology                 |

### Security Anti-Patterns

| ❌ NEVER DO                                | ✅ ALWAYS DO                      |
| ------------------------------------------ | --------------------------------- |
| Store sensitive data in localStorage       | Use secure HTTP-only cookies      |
| Trust client-side validation               | Implement server-side validation  |
| Expose internal IDs in URLs                | Use opaque identifiers            |
| Log sensitive information                  | Implement proper audit logging    |
| Hardcode connection strings                | Use configuration management      |
| **Pass in-context user IDs from frontend** | **Get user ID from auth context** |

### User ID Security Requirements

**🚨 CRITICAL: NEVER pass user identification of the in-context user from frontend to backend**

**Security Principle:**

- **Frontend**: Never sends `AuthorId`, `CreatedById`, `UserId`, `PersonId`, or similar fields of the in-context user in request payloads
- **Backend**: Always determines current user ID from authentication context (claims, JWT, etc.)
- **Database**: Stores user ID for audit/ownership purposes
- **Response**: Can include user ID for display/authorization purposes
- **Display Fields**: `authorName`, `creatorName` fields are acceptable for showing who created content (read-only)
- **Other User IDs**: Frontend CAN send user IDs of OTHER users (e.g., `inviteePersonId`, `assigneeId`) when referencing someone else

**Backend Authentication Context Methods:**

- **Controllers**: Use `GetCurrentPersonIdRequired()` from `_BaseApiController`
- **Services**: Accept `long currentPersonId` as a parameter from the controller
- **Entities**: Set `AuthorId`, `CreatedByPersonId` from the service parameter, not from request models

**❌ FORBIDDEN Patterns:**

```typescript
// Frontend models - NEVER include user ID fields
export interface RecipeCreateModel {
  name: string;
  description: string;
  authorId: number; // ❌ FORBIDDEN - Remove this field
  personId: number; // ❌ FORBIDDEN - Remove this field
  ingredients: string[];
}

// Frontend components - NEVER set user ID in requests
const request = {
  name: "My Recipe",
  authorId: currentUser.id, // ❌ FORBIDDEN - Remove this assignment
  personId: currentUser.personId, // ❌ FORBIDDEN - Remove this assignment
};
```

**✅ REQUIRED Patterns:**

**Backend - Getting In-Context User ID:**

```csharp
// In Controllers - Use base controller method
public async Task<ActionResult<RecipeModel>> CreateRecipe([FromBody] RecipeCreateRequest request)
{
    var currentPersonId = GetCurrentPersonIdRequired(); // ✅ Gets authenticated user's person ID
    var recipe = await _recipeService.CreateAsync(request, currentPersonId);
    return Ok(recipe);
}

// In Services - Accept person ID as parameter
public async Task<RecipeModel> CreateAsync(RecipeCreateRequest request, long currentPersonId)
{
    var recipe = new RecipeEntity
    {
        Name = request.Name,
        AuthorId = currentPersonId, // ✅ Set from parameter, not from request
        CreatedByPersonId = currentPersonId
    };
    // ... rest of implementation
}
```

**Frontend - Clean Request Models:**

```typescript
// Frontend models - Only business data, no in-context user identification
export interface RecipeCreateModel {
  name: string;
  description: string;
  ingredients: string[];
  // ✅ Clean - No in-context user ID fields
}

// Frontend models - CAN include other user IDs when referencing someone else
export interface InvitationClaimModel {
  invitationCode: string;
  inviteePersonId: number; // ✅ Acceptable - This is someone else's ID
}

// Frontend components - Only send business data
const request = {
  name: "My Recipe",
  ingredients: ["flour", "sugar"],
  // ✅ Clean - No user ID assignment
};
```

**Security Verification Checklist:**

- [ ] No frontend models have `authorId`, `createdById`, `userId` fields
- [ ] No frontend components set user ID in request payloads
- [ ] All backend services receive user ID as parameter (not from request)
- [ ] All backend controllers get user ID from authentication context
- [ ] All request models are clean of user identification fields
- [ ] Response models can still include user ID for display purposes
- [ ] Entity models keep user ID for database storage
- [ ] `authorName`, `creatorName` fields are acceptable for display (read-only from API)

**Important Distinction:**

- **❌ FORBIDDEN**: `authorId: number` - Numeric user ID that could be manipulated
- **✅ ACCEPTABLE**: `authorName: string` - Display name for showing who created content
- **❌ FORBIDDEN**: `CreatedById: number` - Numeric user ID in request payloads
- **✅ ACCEPTABLE**: `creatorName: string` - Display name in response models

**Why This Matters:**

1. **Prevents Impersonation**: Users cannot create content as other users
2. **Maintains Data Integrity**: All content is properly attributed to actual creators
3. **Audit Trail**: Accurate tracking of who created/modified what
4. **Security Compliance**: Meets enterprise security requirements
5. **Trust Boundary**: Clear separation between client and server responsibilities

## Material 3 Theming Requirements

### Mandatory Theme Usage

- **NO hardcoded colors** in any component
- **NO hardcoded borders or shadows**
- **ALL UI must use Material 3 theme variables**
- **Support both light and dark themes**
- **Fully responsive across devices**

### Theme Variable Examples

```scss
// ✅ CORRECT: Using theme variables
.nom-card {
  background-color: var(--mat-sys-surface-container);
  color: var(--mat-sys-on-surface);
  box-shadow: 0 4px 8px rgba(var(--mat-sys-shadow), 0.1);
}

// ❌ FORBIDDEN: Hardcoded values
.nom-card {
  background-color: #ffffff;
  color: #000000;
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.1);
}
```

## Development Workflow

### Version Control

- **Feature branches**: All development on dedicated feature branches
- **Pull requests**: All changes via PRs with reviewer approval
- **Code reviews**: Focus on consistency, privacy, architecture, testability

### Quality Assurance

- **Privacy reviews**: All personal data features require privacy impact assessment
- **Unit testing**: Services with mocks, isolated component logic
- **E2E testing**: Critical user flows, especially privacy-related
- **Linting**: Pre-commit hooks enforce rules (ESLint, Stylelint)

### Documentation Standards

- **Code documentation**: Inline comments for complex privacy logic
- **API documentation**: OpenAPI/Swagger for all endpoints
- **Privacy documentation**: GDPR compliance alongside code changes

## Code-First Entity Framework Conventions

### Base Entity Pattern

```csharp
public abstract class BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    // Audit Fields
    public DateTime CreatedDate { get; set; }
    public long? CreatedByPersonId { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public long? LastModifiedByPersonId { get; set; }
}
```

### Entity Naming & Structure

| Pattern                                   | Example                                             | Description                             |
| ----------------------------------------- | --------------------------------------------------- | --------------------------------------- |
| `PascalCaseEntity`                        | `PersonEntity`                                      | All entities inherit from `BaseEntity`  |
| `[Table("TableName", Schema = "schema")]` | `[Table("Person", Schema = "person")]`              | Explicit schema and table naming        |
| `public virtual ICollection<T>`           | `public virtual ICollection<PersonAttributeEntity>` | Navigation properties for relationships |

### DbContext Organization

```csharp
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    // Organized by domain with regions
    #region Person
    public DbSet<PersonEntity> Persons { get; set; } = default!;
    public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
    #endregion

    #region Privacy
    public DbSet<UserConsentEntity> UserConsents { get; set; } = default!;
    public DbSet<DataProcessingLogEntity> DataProcessingLogs { get; set; } = default!;
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("auth");

        #region Person Namespace Fluent API Configurations
        modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
        // ... additional configurations
        #endregion
    }
}
```

### Migration Conventions

#### Custom Migration Pattern

```csharp
public static class CustomMigration
{
    // Constants for seeded data IDs
    private const long SystemPersonId = 1L;
    private const long MeasurementTypeGramId = 4003L;

    public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
    {
        // Seed reference data
        SeedInitialSystemPerson(migrationBuilder);
        AddReferenceGroups(migrationBuilder);
        AddMeasurementTypes(migrationBuilder);
    }

    public static void ApplyCustomDownOperations(this MigrationBuilder migrationBuilder)
    {
        // Remove seeded data in reverse order
        RemoveMeasurementTypes(migrationBuilder);
        RemoveReferenceGroups(migrationBuilder);
        RemoveInitialSystemPerson(migrationBuilder);
    }
}
```

#### Migration File Modification

```bash
# Add custom operations to generated migration
sed -i '1s/^/using Nom.Data;\n/' "$MIGRATION_FILE"
sed -i '/protected override void Up(MigrationBuilder migrationBuilder)/,/^        }/ s/^        }/            migrationBuilder.ApplyCustomUpOperations();\n        }/' "$MIGRATION_FILE"
sed -i '/protected override void Down(MigrationBuilder migrationBuilder)/{n;s/{/{ \n            migrationBuilder.ApplyCustomDownOperations();/}' "$MIGRATION_FILE"
```

### Database Schema Organization

| Schema          | Purpose                          | Tables                                               |
| --------------- | -------------------------------- | ---------------------------------------------------- |
| `auth`          | Identity and authentication      | `AspNetUsers`, `AspNetRoles`                         |
| `person`        | User profiles and attributes     | `Person`, `PersonAttribute`                          |
| `plan`          | Nutritional plans and goals      | `Plan`, `PlanParticipant`, `Restriction`             |
| `privacy`       | GDPR compliance                  | `UserConsent`, `DataProcessingLog`, `PrivacyRequest` |
| `recipe`        | Recipe and ingredient management | `Recipe`, `Ingredient`, `RecipeIngredient`           |
| `curation`      | Content curation                 | `CurationFeedback`                                   |
| `communication` | User messaging                   | `MessageThread`, `Message`                           |
| `reference`     | Lookup data                      | `Group`, `Reference`                                 |
| `audit`         | System audit trail               | `AuditLogEntry`                                      |

## RESTful API Conventions

### Controller Structure

```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PrivacyController : BaseApiController
{
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
    private readonly ILogger<PrivacyController> _logger;

    public PrivacyController(
        IPrivacyOrchestrationService privacyOrchestrationService,
        ILogger<PrivacyController> logger)
    {
        _privacyOrchestrationService = privacyOrchestrationService;
        _logger = logger;
    }

    [HttpPost("consent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateConsent([FromBody] UpdateConsentRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // Business logic
            return Ok(new { Message = "Success" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error message");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Internal error" });
        }
    }
}
```

### HTTP Status Code Usage

| Status Code | Usage                    | Example                                     |
| ----------- | ------------------------ | ------------------------------------------- |
| 200         | Successful GET/PUT/PATCH | `return Ok(response)`                       |
| 201         | Successful POST          | `return CreatedAtAction(...)`               |
| 202         | Async operation accepted | `return Accepted(response)`                 |
| 400         | Bad Request              | `return BadRequest(ModelState)`             |
| 401         | Unauthorized             | `[Authorize]` attribute                     |
| 403         | Forbidden                | `[Authorize(Policy = "CanManageCuration")]` |
| 404         | Not Found                | `return NotFound()`                         |
| 500         | Internal Server Error    | `return StatusCode(500, ...)`               |

### Response Pattern

```csharp
// Success response
return Ok(new { Message = "Operation successful", Data = result });

// Error response
return StatusCode(StatusCodes.Status500InternalServerError,
    new { Message = "An internal error occurred." });

// Async operation response
return Accepted(new PrivacyRequestStatusResponse
{
    RequestId = requestId,
    Status = "Queued"
});
```

## Bash Automation Scripts

### Database Migration Script Pattern

```bash
#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# Configuration
SOLUTION_ROOT=$(dirname "$(readlink -f "$0")")
NOM_API_PROJECT="Nom.Api"
NOM_DATA_PROJECT="Nom.Data"
APPSETTINGS_FILE="${NOM_API_DIR}/appsettings.Development.json"
CONNECTION_STRING_NAME="NomConnection"

# Functions
get_connection_string_value() {
    local CONNECTION_STRING_VALUE
    CONNECTION_STRING_VALUE=$(jq -r ".ConnectionStrings[\"$CONNECTION_STRING_NAME\"]" "$APPSETTINGS_FILE" 2>/dev/null)
    echo "$CONNECTION_STRING_VALUE"
}

check_status() {
    local last_status=$?
    local message="$1"
    if [ $last_status -ne 0 ]; then
        echo "Error: $message failed (exit code: $last_status)." >&2
        exit 1
    fi
}

# Main execution
echo "Starting database migration process..."
```

### Test Script Pattern

```bash
#!/bin/bash

# Test script for enhanced FDC import system
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Function to check if file exists
check_file() {
    local file=$1
    local description=$2
    if [ -f "$file" ]; then
        print_status $GREEN "✅ $description: $file"
        return 0
    else
        print_status $RED "❌ $description: $file (NOT FOUND)"
        return 1
    fi
}
```

### Script Organization

| Script Type        | Location                                       | Purpose                                  |
| ------------------ | ---------------------------------------------- | ---------------------------------------- |
| Database migration | `nom-api/refresh_db_and_migration.sh`          | Reset database and regenerate migrations |
| Import testing     | `nom-api/Nom.Import/test_enhanced_import.sh`   | Validate import process                  |
| Connection testing | `nom-api/Nom.Import/test_connection_string.cs` | Test database connectivity               |

## Global SCSS Conventions

### Material 3 Theme Integration

```scss
// Global theme variables
:root {
  --nom-card-bg: #f1f5f9;
  --nom-panel-bg: #f8fafc;
  --nom-text-high-contrast: #1e293b;
  --nom-border-subtle: #e2e8f0;
}

body.dark-theme,
[data-theme="dark"] {
  --nom-card-bg: #1e293b;
  --nom-panel-bg: #0f172a;
  --nom-text-high-contrast: #f8fafc;
  --nom-border-subtle: #334155;
}
```

### Component-Specific Styling

```scss
// App-level component styles
.nom-header {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 72px;
  background-color: var(--mat-sys-surface-container-high);
  color: var(--mat-sys-on-surface);

  &__left {
    display: flex;
    align-items: center;
    flex: 1;
    padding: 0 20px;
  }

  &__center {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    flex-shrink: 0;
    gap: 20px;
    padding: 0 20px;
  }

  &__right {
    display: flex;
    align-items: center;
    position: relative;
    gap: 10px;
    padding: 15px 20px;
    flex-shrink: 0;
  }
}
```

### AMW Component Overrides

```scss
// AMW component theme overrides
amw-card {
  background-color: var(--mat-sys-surface-container) !important;
  color: var(--mat-sys-on-surface) !important;
}

amw-input {
  background-color: var(--mat-sys-surface-container-low) !important;
  border-radius: 4px;
  color: var(--mat-sys-on-surface) !important;

  input.mat-input-element {
    color: var(--mat-sys-on-surface) !important;

    &::placeholder {
      color: var(--mat-sys-outline) !important;
    }
  }

  mat-error {
    color: var(--mat-sys-error) !important;
  }
}
```

### Animation and Transition Patterns

```scss
// Smooth transitions for theme changes
body {
  transition: background-color 0.3s ease, color 0.3s ease;
}

// Dropdown animations
&__dropdown {
  opacity: 0;
  visibility: hidden;
  transform: translateY(10px);
  transition: opacity 0.2s ease-out, transform 0.2s ease-out, visibility 0.2s;

  &--open {
    opacity: 1;
    visibility: visible;
    transform: translateY(0);
  }
}
```

## Reference Domain Usage Conventions

### 🚨 CRITICAL: Reference Domain Integration

**ALWAYS USE** the Reference domain structure for any categorical, type, or classification data throughout the entire application stack.

**NEVER USE** string-based enums or hardcoded values for:

- Event types
- Status types
- Category types
- Measurement types
- Any other classification data

### Reference Domain Pattern

```csharp
// ✅ CORRECT: Using Reference domain structure
public class RecipeTimelineEventEntity : BaseEntity
{
    [Required]
    public long RecipeId { get; set; }
    [ForeignKey(nameof(RecipeId))]
    public virtual RecipeEntity? Recipe { get; set; }

    [Required]
    public long EventTypeId { get; set; }  // Reference to ReferenceEntity
    [ForeignKey(nameof(EventTypeId))]
    public virtual ReferenceEntity? EventType { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Column(TypeName = "text")]
    public string? Description { get; set; }

    public DateTime EventDate { get; set; }
}

// ✅ CORRECT: Model using Reference ID
public class RecipeTimelineEventCreateModel
{
    [Required(ErrorMessage = "Recipe ID is required.")]
    public long RecipeId { get; set; }

    [Required(ErrorMessage = "Event type is required.")]
    public long EventTypeId { get; set; }  // Reference ID, not string

    [Required(ErrorMessage = "Event title is required.")]
    [StringLength(255, ErrorMessage = "Event title cannot exceed 255 characters.")]
    public required string Title { get; set; }

    [StringLength(2047, ErrorMessage = "Event description cannot exceed 2047 characters.")]
    public string? Description { get; set; }

    public DateTime EventDate { get; set; } = DateTime.UtcNow;
}

// ✅ CORRECT: Response model with Reference data
public class RecipeTimelineEventResponseModel
{
    public long Id { get; set; }
    public long RecipeId { get; set; }
    public string RecipeName { get; set; } = string.Empty;
    public long EventTypeId { get; set; }
    public string EventTypeName { get; set; } = string.Empty;  // From ReferenceEntity
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime EventDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}
```

### Frontend Reference Integration

```typescript
// ✅ CORRECT: TypeScript interfaces using Reference IDs
export interface RecipeTimelineEventModel {
  id: number;
  recipeId: number;
  recipeName: string;
  eventTypeId: number; // Reference ID
  eventTypeName: string; // Reference name
  title: string;
  description?: string;
  eventDate: Date;
  createdDate: Date;
  lastModifiedDate?: Date;
}

export interface RecipeTimelineEventCreateModel {
  recipeId: number;
  eventTypeId: number; // Reference ID, not string enum
  title: string;
  description?: string;
  eventDate: Date;
}

// ✅ CORRECT: Service methods using Reference IDs
@Injectable({
  providedIn: "root",
})
export class RecipeTimelineService {
  createEvent(
    event: RecipeTimelineEventCreateModel
  ): Observable<RecipeTimelineEventModel> {
    return this.http.post<RecipeTimelineEventModel>(
      `${this.apiUrl}/timeline-events`,
      event
    );
  }

  getEventTypes(): Observable<ReferenceModel[]> {
    return this.http.get<ReferenceModel[]>(`${this.apiUrl}/event-types`);
  }
}
```

### Reference Data Seeding Pattern

```csharp
// ✅ CORRECT: Seeding Reference data with specific IDs
public static void SeedRecipeEventTypes(MigrationBuilder migrationBuilder)
{
    // Recipe Timeline Event Types (10000-10099)
    migrationBuilder.InsertData(
        table: "Reference",
        schema: "reference",
        columns: new[] { "Id", "Name", "Description" },
        values: new object[,]
        {
            { 10001L, "Recipe Created", "Recipe was initially created" },
            { 10002L, "Recipe Updated", "Recipe was modified" },
            { 10003L, "Recipe Published", "Recipe was made public" },
            { 10004L, "Recipe Rated", "Recipe received a rating" },
            { 10005L, "Recipe Commented", "Recipe received a comment" },
            { 10006L, "Recipe Made", "Recipe was prepared/cooked" },
            { 10007L, "Recipe Shared", "Recipe was shared with others" },
            { 10008L, "Recipe Favorited", "Recipe was added to favorites" },
            { 10009L, "Recipe Added to Plan", "Recipe was added to meal plan" },
            { 10010L, "Recipe Exported", "Recipe was exported to external format" }
        });

    // Add to ReferenceIndex for grouping
    migrationBuilder.InsertData(
        table: "ReferenceIndex",
        schema: "reference",
        columns: new[] { "ReferenceId", "GroupId" },
        values: new object[,]
        {
            { 10001L, 1000L }, // RecipeEventType group
            { 10002L, 1000L },
            { 10003L, 1000L },
            { 10004L, 1000L },
            { 10005L, 1000L },
            { 10006L, 1000L },
            { 10007L, 1000L },
            { 10008L, 1000L },
            { 10009L, 1000L },
            { 10010L, 1000L }
        });
}
```

### Reference ID Ranges

| Category                 | Range         | Examples                                |
| ------------------------ | ------------- | --------------------------------------- |
| Recipe Event Types       | 10000L-10099L | `RecipeEventTypeCreatedId = 10001L`     |
| Recipe Status Types      | 10100L-10199L | `RecipeStatusTypeDraftId = 10101L`      |
| Recipe Share Token Types | 10200L-10299L | `RecipeShareTokenTypePublicId = 10201L` |
| Recipe Comment Types     | 10300L-10399L | `RecipeCommentTypeGeneralId = 10301L`   |
| Recipe Note Types        | 10400L-10499L | `RecipeNoteTypePrivateId = 10401L`      |

### Forbidden Patterns

| ❌ NEVER USE                                    | ✅ ALWAYS USE                           |
| ----------------------------------------------- | --------------------------------------- |
| `public string EventType { get; set; }`         | `public long EventTypeId { get; set; }` |
| `public enum RecipeStatus { Draft, Published }` | Reference domain with seeded data       |
| `public string Status { get; set; }`            | `public long StatusId { get; set; }`    |
| Hardcoded string values in models               | Reference IDs with proper foreign keys  |

### Reference Data & View Entity Conventions

### Table-Per-Hierarchy (TPH) Pattern

```csharp
// Base reference entity
[Table("Reference", Schema = "reference")]
public class ReferenceEntity : BaseEntity
{
    [Required]
    public required string Name { get; set; }
    public string? Description { get; set; }

    public virtual ICollection<GroupEntity>? Groups { get; set; }
}

// View entity base class
public abstract class GroupedReferenceViewEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ReferenceId { get; set; }
    public string ReferenceName { get; set; } = string.Empty;
    public string? ReferenceDescription { get; set; }
    public long GroupId { get; set; } // TPH discriminator
    public string GroupName { get; set; } = string.Empty;
    public string? GroupDescription { get; set; }
}

// Specific view entity
public class MeasurementTypeViewEntity : GroupedReferenceViewEntity
{
    // Inherits all properties from base view class
}
```

### Reference Discriminator Enum Pattern

```csharp
public enum ReferenceDiscriminatorEnum : long
{
    Unknown = 0,

    // Core System Reference Groups (1-999)
    MealType = 1,
    MeasurementType = 2,
    RecipeType = 3,

    // Core Application Feature Reference Groups (1000-1999)
    QuestionCategory = 1000,
    AnswerType = 1001,
    CurationStatusType = 1002,

    // Dietary & Health Related Reference Groups (2000-2999)
    RestrictionType = 2000,
    GoalType = 2001,

    // Nutritional & Ingredient Reference Groups (3000-3999)
    NutrientType = 3000,
    CuisineType = 3001,

    // Plan Management & User Roles (4000-4999)
    PlanInvitationRole = 4000,

    // Privacy & Compliance (5000-5999)
    PrivacyConsentType = 5000
}
```

### View Entity Naming Conventions

| Pattern                      | Example                          | Description            |
| ---------------------------- | -------------------------------- | ---------------------- |
| `PascalCaseViewEntity`       | `MeasurementTypeViewEntity`      | Specific view entities |
| `GroupedReferenceViewEntity` | Base class for all view entities | Abstract base class    |
| `ReferenceDiscriminatorEnum` | Enum with ID ranges              | Discriminator values   |

### Database View Pattern

```sql
CREATE OR REPLACE VIEW reference."ReferenceGroupView" AS
SELECT
    ref."Id" AS "ReferenceId",
    ref."Name" AS "ReferenceName",
    ref."Description" AS "ReferenceDescription",
    grp."Id" AS "GroupId",
    grp."Name" AS "GroupName",
    grp."Description" AS "GroupDescription"
FROM
    reference."Reference" AS ref
INNER JOIN
    reference."ReferenceIndex" AS idx ON ref."Id" = idx."ReferenceId"
INNER JOIN
    reference."Group" AS grp ON grp."Id" = idx."GroupId";
```

### Seeded Data ID Ranges

| Category              | Range       | Examples                                |
| --------------------- | ----------- | --------------------------------------- |
| System Person         | 1L          | `SystemPersonId = 1L`                   |
| Measurement Types     | 4000L-4051L | `MeasurementTypeGramId = 4003L`         |
| Nutrient Types        | 5000L-5035L | `NutrientProteinId = 5006L`             |
| Goal Types            | 6000L-6004L | `GoalTypeGeneralAdultId = 6004L`        |
| Privacy Consent Types | 8000L-8002L | `PrivacyConsentTypeAnalyticsId = 8000L` |
| Curation Status Types | 9000L-9004L | `CurationStatusTypeCuratedId = 9003L`   |
| Feedback Entity Types | 9100L-9101L | `FeedbackEntityTypeRecipeId = 9100L`    |
| Feedback Types        | 9200L-9203L | `FeedbackTypeApprovalPrivateId = 9200L` |

## Angular Routing Conventions

### Lazy Loading Pattern

```typescript
// Main app routes
export const routes: Routes = [
  // Eager-loaded routes for immediate access
  { path: "home", component: HomeComponent },
  { path: "register", component: RegistrationComponent },

  // Lazy-loaded feature routes
  {
    path: "recipes",
    loadChildren: () =>
      import("./recipe/recipe.routes").then((m) => m.RECIPE_ROUTES),
    canActivate: [AuthGuard],
  },
  {
    path: "curation",
    loadChildren: () =>
      import("./curation/curation.routes").then((m) => m.CURATION_ROUTES),
    canActivate: [AuthGuard],
  },
];
```

### Feature Route Organization

```typescript
// Feature-specific routes (e.g., recipe.routes.ts)
export const RECIPE_ROUTES: Routes = [
  {
    path: "", // Default route for this feature
    component: RecipeAuthorDashboardComponent,
    title: "My Recipes",
  },
  {
    path: "new", // e.g., /recipes/new
    component: RecipeEditComponent,
    title: "Create Recipe",
  },
  {
    path: ":id", // e.g., /recipes/123
    component: RecipeEditComponent,
    title: "View Recipe",
  },
  {
    path: ":id/edit", // e.g., /recipes/123/edit
    component: RecipeEditComponent,
    title: "Edit Recipe",
  },
  {
    path: "ingredients/new", // e.g., /recipes/ingredients/new
    component: IngredientEditComponent,
    title: "Create New Ingredient",
  },
];
```

### Route Naming Conventions

| Pattern        | Example                                   | Description                 |
| -------------- | ----------------------------------------- | --------------------------- |
| Feature routes | `recipes`, `curation`, `admin`            | Lazy-loaded feature modules |
| CRUD routes    | `new`, `:id`, `:id/edit`                  | Standard CRUD operations    |
| Nested routes  | `ingredients/new`, `ingredients/:id/edit` | Sub-feature routes          |
| Guard routes   | `canActivate: [AuthGuard]`                | Route protection            |

## Domain Organization Patterns

### Frontend Domain Structure

```
src/app/
├── common/                    # Shared utilities and components
│   ├── components/
│   ├── models/
│   └── pipes/
├── shared/                    # Shared models
│   └── models/
├── auth/                      # Authentication components
├── home/                      # Home page
├── utilities/                 # Utility services
├── person/                    # Domain-specific modules
│   ├── components/
│   ├── models/
│   └── services/
├── recipe/                    # Recipe domain
│   ├── components/
│   ├── models/
│   ├── services/
│   └── recipe.routes.ts
├── curation/                  # Curation domain
├── communication/             # Communication domain
├── admin/                     # Admin functionality
├── user/                      # User management
├── privacy/                   # Privacy domain
├── plan/                      # Plan domain
├── restriction/               # Restriction domain
├── onboarding/                # Onboarding domain
├── nutrient/                  # Nutrient domain
└── guards/                    # Route guards
```

### Backend Domain Structure

```
Nom.Orch/
├── Services/                  # Business logic services
│   ├── PersonOrchestrationService.cs
│   ├── RecipeOrchestrationService.cs
│   ├── CurationOrchestrationService.cs
│   ├── PrivacyOrchestrationService.cs
│   └── ...
├── Models/                    # Request/Response models
│   ├── Person/
│   ├── Recipe/
│   ├── Curation/
│   ├── Privacy/
│   ├── Reference/
│   ├── Communication/
│   ├── UserManagement/
│   └── Audit/
└── Interfaces/                # Service contracts
    ├── IPersonOrchestrationService.cs
    ├── IRecipeOrchestrationService.cs
    └── ...
```

### Domain Separation Patterns

| Frontend Domain  | Backend Domain    | Purpose                          |
| ---------------- | ----------------- | -------------------------------- |
| `recipe/`        | `Recipe/`         | Recipe and ingredient management |
| `curation/`      | `Curation/`       | Content curation workflow        |
| `communication/` | `Communication/`  | User messaging system            |
| `admin/`         | `UserManagement/` | User role management             |
| `privacy/`       | `Privacy/`        | GDPR compliance                  |
| `person/`        | `Person/`         | User profiles and attributes     |
| `plan/`          | `Plan/`           | Nutritional plans                |
| `restriction/`   | `Restriction/`    | Dietary restrictions             |
| `onboarding/`    | `Person/`         | User onboarding                  |
| `nutrient/`      | `Nutrient/`       | Nutritional data                 |

### Shared vs Common Patterns

| Directory    | Purpose               | Examples                                |
| ------------ | --------------------- | --------------------------------------- |
| `common/`    | Reusable utilities    | Pipes, base models, shared components   |
| `shared/`    | Cross-domain models   | Base interfaces, common data structures |
| `utilities/` | Application utilities | Services, helpers, utilities            |

### Feature Module Organization

Each domain follows this structure:

```
domain-name/
├── components/               # Angular components
│   └── component-name/
│       ├── component-name.component.ts
│       ├── component-name.component.html
│       └── component-name.component.scss
├── models/                   # TypeScript interfaces and classes
│   ├── domain-name.model.ts
│   ├── domain-name-request.model.ts
│   └── domain-name-response.model.ts
├── services/                 # Angular services
│   └── domain-name.service.ts
├── guards/                   # Route guards (if applicable)
└── domain-name.routes.ts     # Feature-specific routes
```

---

_This document should be updated as the project evolves and new patterns emerge._
