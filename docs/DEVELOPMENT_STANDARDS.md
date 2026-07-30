# Development Standards

## Abstract vs Concrete Naming Conventions

### **CRITICAL: Conceptual vs Literal Abstraction**

**Abstract classes and interfaces are NOT the same thing. They serve different purposes and MUST follow different naming conventions.**

#### **Abstract Classes (Literally Abstract)**

- **Purpose**: Base classes that provide common functionality but cannot be instantiated directly
- **Naming Convention**: `_<Name>` (underscore prefix)
- **File Naming**: `_<Name>.ts`
- **Location**: `shared/components/base/` or `shared/services/base/`
- **Example**: `_BaseService`, `_BaseButtonComponent`

#### **Interfaces (Conceptually Abstract)**

- **Purpose**: Type definitions that describe contracts and shapes
- **Naming Convention**: `I<Name>` (capital I prefix)
- **File Naming**: `I<Name>.ts`
- **Location**: `shared/interfaces/<domain>/`
- **Example**: `IBaseService`, `IInputConfig`

#### **Concrete Implementations**

- **Purpose**: Actual, instantiable classes that extend abstract classes or implement interfaces
- **Naming Convention**: `<Name>` (no prefix)
- **File Naming**: `<Name>.ts`
- **Location**: `shared/components/<domain>/` or `shared/services/<domain>/`
- **Example**: `ButtonComponent`, `EventBusService`

## File Separation Standards

### **MANDATORY: 1 Object 1 File Rule**

**Every class, interface, or type MUST be in its own separate file.**

#### **Allowed File Structures**

```
 CORRECT:
- IInputConfig.ts (single interface)
- _BaseService.ts (single abstract class)
- ButtonComponent.ts (single concrete class)
- IValidationResult.ts (single interface)
```

#### **Forbidden File Structures**

```
 FORBIDDEN:
- _BaseService.ts (multiple interfaces + abstract class)
- IInputConfig.ts (multiple interfaces)
- ButtonComponent.ts (interface + concrete class)
```

### **Angular Component File Separation**

**Angular components MUST have separate files for HTML, SCSS, and TypeScript.**

#### **Required File Structure**

```
 CORRECT:
- ButtonComponent.ts (TypeScript logic)
- ButtonComponent.html (HTML template)
- ButtonComponent.scss (SCSS styles)
```

#### **Forbidden File Structures**

```
 FORBIDDEN:
- ButtonComponent.ts (embedded template: template: `...`)
- ButtonComponent.ts (embedded styles: styles: [`...`])
- ButtonComponent.ts (inline template and styles)
```

## Folder Structure Standards

### **NO Literal "Abstractions" Folders**

**Folders named "Abstractions" or "Abstract" are FORBIDDEN.**

#### **Correct Domain-Based Structure**

```
 CORRECT:
src/app/shared/
├── interfaces/
│   ├── input/
│   │   ├── IInputConfig.ts
│   │   ├── IInputEvents.ts
│   │   └── IValidationResult.ts
│   └── services/
│       ├── IBaseService.ts
│       ├── IServiceHealthStatus.ts
│       └── ICrudService.ts
├── components/
│   ├── base/
│   │   ├── _BaseButtonComponent.ts
│   │   ├── _BaseButtonComponent.html
│   │   └── _BaseButtonComponent.scss
│   └── buttons/
│       ├── ButtonComponent.ts
│       ├── ButtonComponent.html
│       └── ButtonComponent.scss
└── services/
    ├── base/
    │   └── _BaseService.ts
    └── events/
        └── EventBusService.ts
```

#### **Forbidden Structure**

```
 FORBIDDEN:
src/app/
├── _Abstractions/
│   ├── _Components/
│   ├── _Services/
│   └── _Interfaces/
└── Abstract/
    ├── BaseClasses/
    └── Interfaces/
```

## Desktop UI Viewport Standards

### **Desktop UI Must Fit Standard Viewport**

**All desktop-targeted user interfaces MUST fit within 1800x850px viewport at 100% zoom without scrolling.**

#### **Viewport Requirements**

```scss
correct: .main-container {
  height: calc(100vh - 120px); // Account for compact header
  overflow: hidden;
}

forbidden: .main-container {
  height: 1200px; // Fixed height that may cause scrolling
  overflow: scroll; // Allows scrolling
}
```

#### **Compact Header Pattern (Required for Desktop Interfaces)**

```scss
correct: .compact-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 0;
  // Title, subtitle, and controls on same horizontal line
}

forbidden: .tall-header {
  padding: 2rem 0;
  // Title, subtitle, and controls stacked vertically
}
```

#### **Target Specifications**

- **Resolution**: 1080p (1920x1080)
- **Browser**: Maximized window
- **Effective Viewport**: ~1800x850px
- **Zoom**: 100%
- **Scrolling**: NONE allowed on primary workflows
- **Header Height**: Maximum 25% of previous height (75% reduction target)
- **Header Layout**: Title, subtitle, and controls on single horizontal line

## Angular Selector Standards

### **Component Selector Prefix**

**All Angular component selectors MUST use the "nom-" prefix.**

#### **Correct Selector Usage**

```typescript
 CORRECT:
@Component({
  selector: 'nom-button',
  templateUrl: './ButtonComponent.html',
  styleUrls: ['./ButtonComponent.scss']
})
export class ButtonComponent extends _BaseButtonComponent {
  // Implementation
}
```

#### **Forbidden Selector Usage**

```typescript
 FORBIDDEN:
@Component({
  selector: 'app-button',  // Wrong prefix (should be 'nom-button')
  selector: 'button',      // No prefix
  selector: 'btn',         // Wrong prefix
})
```

## Implementation Examples

### **Abstract Base Service**

```typescript
// File: shared/services/base/_BaseService.ts
@Injectable()
export abstract class BaseService implements OnDestroy {
  abstract getServiceName(): string;
  abstract performInitialization(): Promise<void>;
  abstract performCleanup(): Promise<void>;
  abstract performHealthCheck(): Promise<boolean>;
}
```

### **Interface Definition**

```typescript
// File: shared/interfaces/services/IBaseService.ts
export interface IBaseService {
  readonly serviceName: string;
  readonly isInitialized: boolean;
  initialize(): Promise<void>;
  dispose(): Promise<void>;
}
```

### **Concrete Implementation**

```typescript
// File: shared/services/events/EventBusService.ts
@Injectable({
  providedIn: "root",
})
export class EventBusService implements OnDestroy {
  // Concrete implementation
}
```

### **Abstract Base Component**

```typescript
// File: shared/components/base/_BaseButtonComponent.ts
@Component({
  selector: "nom-base-button",
  templateUrl: "./_BaseButtonComponent.html",
  styleUrls: ["./_BaseButtonComponent.scss"],
})
export abstract class BaseButtonComponent implements OnInit, OnDestroy {
  abstract getButtonClasses(): string;
}
```

### **Concrete Component**

```typescript
// File: shared/components/buttons/ButtonComponent.ts
@Component({
  selector: "nom-button",
  templateUrl: "./ButtonComponent.html",
  styleUrls: ["./ButtonComponent.scss"],
})
export class ButtonComponent extends _BaseButtonComponent {
  getButtonClasses(): string {
    // Concrete implementation
  }
}
```

## Validation Checklist

### **Before Creating Any New File**

1. **Is this an abstract class?** → Use `_<Name>` naming
2. **Is this an interface?** → Use `I<Name>` naming
3. **Is this a concrete class?** → Use `<Name>` naming
4. **Does this file contain only ONE object?** → Yes, or split it
5. **Is this an Angular component?** → Separate HTML/SCSS/TS files
6. **Is the folder structure domain-based?** → No literal "Abstractions" folders
7. **Does the component use "nom-" selector?** → Yes, always

### **File Naming Validation**

| **Type**       | **Correct Pattern** | **Examples**                                 |
| -------------- | ------------------- | -------------------------------------------- |
| Abstract Class | `_<Name>.ts`        | `_BaseService.ts`, `_BaseButtonComponent.ts` |
| Interface      | `I<Name>.ts`        | `IBaseService.ts`, `IInputConfig.ts`         |
| Concrete Class | `<Name>.ts`         | `EventBusService.ts`, `ButtonComponent.ts`   |

### **Component File Structure Validation**

| **File Type** | **Required**     | **Forbidden**             |
| ------------- | ---------------- | ------------------------- |
| TypeScript    | `Component.ts`   | Embedded templates/styles |
| HTML          | `Component.html` | Inline templates          |
| SCSS          | `Component.scss` | Inline styles             |

## Enforcement

### **Code Review Requirements**

1. **ALL new files MUST be reviewed for naming convention compliance**
2. **ALL Angular components MUST be reviewed for file separation**
3. **ALL folder structures MUST be reviewed for domain-based organization**
4. **ALL selectors MUST be reviewed for "nom-" prefix**

### **Automated Checks**

- **Linting rules MUST enforce 1 object per file**
- **Build process MUST validate selector prefixes**
- **CI/CD MUST check for forbidden folder names**

### **Documentation Requirements**

- **ALL new patterns MUST be documented here**
- **ALL exceptions MUST be justified and documented**
- **ALL changes to standards MUST be versioned**

## Design System Principles

### **MANDATORY: Five Foundational Design Principles**

All UI development must comply with the five immutable design principles defined in `nom-ui/docs/DESIGN-SPECIFICATION.md`, Section 1. These principles are non-negotiable and propagate universally.

#### 1. 8pt Grid System
- **Atomic Unit:** 8px. All padding, margins, and gutters must be `n x 8px`.
- **Sub-grid exception:** 4px is allowed for optical adjustments only (badge padding, icon alignment).
- **Border-radius:** 4px for buttons/inputs, 8px for cards. No pill shapes (>10px).

#### 2. Major Third (1.25) Type Scale
- H1: ~39px (page intent), H2: ~25px (sections), H3: 20px (card headers), Body: 16px, Small: ~13px
- Headers must enable 3-second page skim comprehension.

#### 3. 60-30-10 Color Rule
- 60% Neutral surfaces, 30% Secondary content, 10% Action accent.
- **Blue/primary color is ONLY for interactive elements.** Never decorative.
- Semantic colors (amber, red, green) are strictly for system state feedback.

#### 4. Information Architecture
- Desktop: Z-pattern (hero top-left, secondary top-right, action center).
- Mobile: Prioritized vertical stack.
- Every text/background pairing must pass WCAG 2.1 AA (4.5:1 contrast).

#### 5. Micro-interactions
- All transitions: `300ms cubic-bezier(0.4, 0, 0.2, 1)`.
- Respect `prefers-reduced-motion: reduce`.
- All interactive elements must have 44x44px minimum touch target.
- All interactive elements must define: Default, Hover, Focus, Active, Disabled states.

#### Reference Files
- **Full specification:** `nom-ui/docs/DESIGN-SPECIFICATION.md`
- **Variables:** `nom-ui/src/_variables.scss`
- **Overrides:** `nom-ui/src/_amw-overrides.scss`
- **Stereotypes:** `nom-ui/src/_stereotype-*.scss`

## UI State Conventions (v1.5)

#### Loading
- **Global overlay** (`LoadingService` + `nom-loading-overlay`): route navigation only.
  The app shell drives this automatically; components must not add() for in-page work.
- **Inline spinners**: all in-page loading (data fetches, form submissions) uses a local
  `loading`/`saving` signal with an inline `mat-spinner`. Do not dim the whole app for
  work scoped to one region.

#### Errors
- Page/section-level failures render `<nom-error-banner [message]="..."/>`
  (`shared/components/error-banner`) — announced via `role="alert"`; pass
  `[showRetry]="true" (retry)="reload()"` whenever the operation can be retried.
- Form-level errors keep the `.nom-form__error` stereotype.
- Field-level errors use `mat-error` guarded by `hasError(...) && control.touched`.
- Name the state signal `error` (type `signal<string>('')`) in new code.

#### Form validation
- Submit buttons stay enabled while the form is invalid (disable only while saving).
- The submit handler starts with:
  `if (form.invalid) { form.markAllAsTouched(); return; }`

## Version History

- **v1.0**: Initial standards definition
- **v1.1**: Added comprehensive file separation rules
- **v1.2**: Clarified abstract vs interface naming conventions
- **v1.3**: Added Angular selector prefix requirements
- **v1.4**: Added design system principles (8pt grid, Major Third type, 60-30-10 color, IA, micro-interactions)
- **v1.5**: Added UI state conventions (loading, errors, form validation)
