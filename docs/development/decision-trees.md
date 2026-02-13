# Development Decision Trees

This guide provides clear decision trees for common development scenarios that AI tools and developers encounter when working with the NOM project.

## Component Architecture Decisions

### Which Base Component to Use?

```
Is this a full page with navigation and actions?
├─ Yes → nom-base-page
└─ No → Is this primarily a form with fields and validation?
    ├─ Yes → nom-base-form
    └─ No → Is this displaying a list of items with search/filter?
        ├─ Yes → nom-base-list
        └─ No → Is this showing detailed information about a single item?
            ├─ Yes → nom-base-detail
            └─ No → Consider if this should be a custom component or if you're missing a use case
```

### When to Use Standalone vs Base Components?

```
Is this a reusable UI pattern that will be used across multiple features?
├─ Yes → Create a new base component
└─ No → Is this a simple utility component (button, icon, etc.)?
    ├─ Yes → Use standalone component
    └─ No → Does this component need loading states, error handling, or navigation?
        ├─ Yes → Use appropriate base component
        └─ No → Use standalone component
```

## Styling Decisions

### Which Styling Approach to Use?

```
Is this a global style that affects the entire application?
├─ Yes → Add to global styles (styles.scss)
└─ No → Is this a component-specific style?
    ├─ Yes → Use component-specific SCSS file
    └─ No → Is this a reusable style pattern?
        ├─ Yes → Create a shared SCSS mixin or utility class
        └─ No → Use inline styles (rarely recommended)
```

### Material 3 vs Custom Styling?

```
Is this a standard UI element (button, card, dialog)?
├─ Yes → Use Material 3 components and theme variables
└─ No → Is this a custom design that needs specific styling?
    ├─ Yes → Use Material 3 theme variables with custom CSS
    └─ No → Can this be achieved with Material 3 components?
        ├─ Yes → Use Material 3 components
        └─ No → Create custom component with Material 3 theme variables
```

## State Management Decisions

### How to Handle Component State?

```
Is this state needed across multiple components?
├─ Yes → Use a service with RxJS BehaviorSubject
└─ No → Is this form state with validation?
    ├─ Yes → Use Reactive Forms with FormBuilder
    └─ No → Is this simple local component state?
        ├─ Yes → Use component properties
        └─ No → Is this async data loading?
            ├─ Yes → Use RxJS with loading states
            └─ No → Use simple component properties
```

### When to Use Services vs Direct API Calls?

```
Is this data needed by multiple components?
├─ Yes → Create a dedicated service
└─ No → Is this a complex API operation with business logic?
    ├─ Yes → Create a service with orchestration
    └─ No → Is this a simple CRUD operation?
        ├─ Yes → Use existing service or create simple service
        └─ No → Direct API call in component (not recommended)
```

## Performance Decisions

### When to Use Lazy Loading?

```
Is this a feature module that's not immediately needed?
├─ Yes → Use lazy loading with route configuration
└─ No → Is this a large component that's conditionally rendered?
    ├─ Yes → Consider dynamic imports
    └─ No → Is this a utility that's rarely used?
        ├─ Yes → Use dynamic imports
        └─ No → Load synchronously
```

### When to Implement Caching?

```
Is this data that doesn't change frequently?
├─ Yes → Implement caching with TTL
└─ No → Is this expensive to compute or fetch?
    ├─ Yes → Consider memoization or caching
    └─ No → Is this user-specific data that changes often?
        ├─ Yes → No caching needed
        └─ No → Consider lightweight caching
```

## Security Decisions

### How to Handle Authentication?

```
Is this a public page (login, register, onboarding)?
├─ Yes → No authentication required
└─ No → Is this an admin-only feature?
    ├─ Yes → Check for admin claims (CanManageCuration, CanManageUserRoles)
    └─ No → Is this a user-specific feature?
        ├─ Yes → Check for valid authentication token
        └─ No → Check for specific feature permissions
```

### How to Validate User Input?

```
Is this form data that will be sent to the API?
├─ Yes → Use Reactive Forms with validators + server-side validation
└─ No → Is this search/filter input?
    ├─ Yes → Sanitize input and use parameterized queries
    └─ No → Is this display-only data?
        ├─ Yes → Sanitize for XSS prevention
        └─ No → No validation needed
```

## UX/UI Decisions

### When to Show Loading States?

```
Is this an async operation that takes more than 100ms?
├─ Yes → Show loading indicator
└─ No → Is this a critical operation (save, delete)?
    ├─ Yes → Show loading state even if fast
    └─ No → Is this a background operation?
        ├─ Yes → Show subtle loading indicator
        └─ No → No loading state needed
```

### When to Show Error Messages?

```
Did the operation fail with a user-actionable error?
├─ Yes → Show specific error message with retry option
└─ No → Is this a network or server error?
    ├─ Yes → Show generic error with retry option
    └─ No → Is this a validation error?
        ├─ Yes → Show field-specific error messages
        └─ No → Log error but don't show to user
```

## Data Flow Decisions

### How to Handle Parent-Child Communication?

```
Is this a simple data pass from parent to child?
├─ Yes → Use @Input() property
└─ No → Is this an event from child to parent?
    ├─ Yes → Use @Output() EventEmitter
    └─ No → Is this complex state that needs to be shared?
        ├─ Yes → Use a service with RxJS
        └─ No → Is this a one-time data fetch?
            ├─ Yes → Use service method call
            └─ No → Consider if this communication is necessary
```

### When to Use Event Bus vs Direct Service Calls?

```
Is this a cross-module communication that should be loosely coupled?
├─ Yes → Use EventBusService
└─ No → Is this a direct service-to-service communication?
    ├─ Yes → Use direct service method calls
    └─ No → Is this a component-to-service communication?
        ├─ Yes → Use direct service injection
        └─ No → Use EventBusService for loose coupling
```

## Testing Decisions

### What Type of Tests to Write?

```
Is this a service with business logic?
├─ Yes → Write unit tests with mocked dependencies
└─ No → Is this a component with user interactions?
    ├─ Yes → Write component tests with TestBed
    └─ No → Is this a utility function?
        ├─ Yes → Write unit tests
        └─ No → Is this an API endpoint?
            ├─ Yes → Write integration tests
            └─ No → Consider if testing is necessary
```

### When to Mock vs Use Real Dependencies?

```
Is this a unit test that should be isolated?
├─ Yes → Mock all dependencies
└─ No → Is this an integration test?
    ├─ Yes → Use real dependencies where possible
    └─ No → Is this a component test?
        ├─ Yes → Mock services, use real component dependencies
        └─ No → Use real dependencies for end-to-end testing
```

## Error Handling Decisions

### How to Handle Different Types of Errors?

```
Is this a network connectivity error?
├─ Yes → Show retry option with offline indicator
└─ No → Is this a server error (500)?
    ├─ Yes → Show generic error with retry option
    └─ No → Is this a client error (400)?
        ├─ Yes → Show specific error message
        └─ No → Is this a validation error?
            ├─ Yes → Show field-specific errors
            └─ No → Log error and show generic message
```

### When to Retry vs Fail Fast?

```
Is this a critical operation (save, delete)?
├─ Yes → Retry with exponential backoff
└─ No → Is this a data fetch operation?
    ├─ Yes → Retry once, then show error
    └─ No → Is this a background operation?
        ├─ Yes → Retry silently in background
        └─ No → Fail fast and show error
```

## Migration Decisions

### When to Migrate to Base Components?

```
Is this component already using a base component?
├─ Yes → No migration needed
└─ No → Is this a page-level component with navigation?
    ├─ Yes → Migrate to nom-base-page
    └─ No → Is this a form component?
        ├─ Yes → Migrate to nom-base-form
        └─ No → Is this a list component?
            ├─ Yes → Migrate to nom-base-list
            └─ No → Is this a detail component?
                ├─ Yes → Migrate to nom-base-detail
                └─ No → Keep as standalone component
```

### How to Prioritize Migration?

```
Is this component actively being developed?
├─ Yes → Migrate now to establish pattern
└─ No → Is this component stable and working?
    ├─ Yes → Migrate when convenient
    └─ No → Is this component broken or problematic?
        ├─ Yes → Fix first, then migrate
        └─ No → Migrate to improve consistency
```

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
