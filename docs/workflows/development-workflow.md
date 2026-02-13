# Development Workflow Guide

## Overview

This guide outlines the development workflow for the Nutrition Optimization Machine, incorporating the established component architecture patterns.

## Development Process

### 1. **Component Creation Workflow**

#### Step 1: Identify Component Type

Use the quick decision tree:

```
Is it a form? → Use nom-base-form
Is it a full page? → Use nom-base-page
Is it a detail view? → Use nom-base-detail
Is it a list/collection? → Use nom-base-list
```

#### Step 2: Generate Component

```bash
# Generate a new component
ng generate component path/to/component-name --standalone

# Example for a form component
ng generate component recipe/components/recipe-create --standalone
```

#### Step 3: Implement Base Component Pattern

1. Import the appropriate base component
2. Create configuration object
3. Wire up event handlers
4. Implement template with base component wrapper

#### Step 4: Follow the Migration Checklist

- [ ] Identify the primary component type
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

### 2. **Code Review Process**

#### Pre-Review Checklist

- [ ] Component uses appropriate base component
- [ ] Configuration follows established patterns
- [ ] Error handling is implemented
- [ ] Loading states are properly managed
- [ ] Event handlers are wired correctly
- [ ] Styles follow BEM methodology
- [ ] Accessibility features are implemented
- [ ] Responsive design is tested

#### Review Criteria

1. **Architecture Compliance**

   - Uses correct base component type
   - Follows established patterns
   - Implements proper error handling

2. **Code Quality**

   - Follows naming conventions
   - Implements proper TypeScript typing
   - Uses reactive forms consistently

3. **User Experience**

   - Loading states are clear
   - Error messages are helpful
   - Navigation is intuitive

4. **Accessibility**
   - Proper ARIA attributes
   - Keyboard navigation support
   - Screen reader compatibility

### 3. **Testing Strategy**

#### Unit Testing

```typescript
describe("MyComponent", () => {
  it("should use base component correctly", () => {
    // Test base component integration
  });

  it("should handle form submission", () => {
    // Test form functionality
  });

  it("should display loading states", () => {
    // Test loading behavior
  });

  it("should handle errors gracefully", () => {
    // Test error handling
  });
});
```

#### Integration Testing

- Test component interaction with services
- Verify base component event handling
- Test responsive behavior
- Validate accessibility features

#### E2E Testing

- Test complete user workflows
- Verify form submissions
- Test navigation patterns
- Validate error scenarios

## Development Tools

### VS Code Extensions

- **Angular Language Service**: Enhanced Angular support
- **ESLint**: Code quality enforcement
- **Prettier**: Code formatting
- **Material Icon Theme**: Visual file organization

### Development Commands

```bash
# Start development server
ng serve

# Run tests
ng test

# Build for production
ng build --configuration production

# Lint code
ng lint

# Generate component
ng generate component path/to/component --standalone
```

## Quality Assurance

### Code Quality Standards

1. **TypeScript**: Strict mode enabled
2. **ESLint**: Enforce coding standards
3. **Prettier**: Consistent formatting
4. **Angular Style Guide**: Follow official guidelines

### Performance Standards

1. **Bundle Size**: Monitor component size
2. **Loading Time**: Optimize for fast loading
3. **Memory Usage**: Prevent memory leaks
4. **Network Requests**: Minimize API calls

### Accessibility Standards

1. **WCAG 2.1 AA**: Meet accessibility guidelines
2. **Keyboard Navigation**: Full keyboard support
3. **Screen Readers**: Proper ARIA attributes
4. **Color Contrast**: Sufficient color contrast

## Continuous Integration

### Automated Checks

- [ ] Code linting passes
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Accessibility tests pass
- [ ] Performance benchmarks met
- [ ] Security scans pass

### Security Checklist

** CRITICAL: User ID Security Verification**

Before submitting any code that creates/updates entities:

- [ ] **Frontend Models**: No `authorId`, `createdById`, `userId`, `personId` fields of the in-context user in request models
- [ ] **Frontend Components**: No in-context user ID assignment in request payloads
- [ ] **Backend Services**: Services receive user ID as parameter (not from request)
- [ ] **Backend Controllers**: Controllers get user ID from authentication context
- [ ] **Request Models**: All request models are clean of in-context user identification fields (other user IDs like `inviteePersonId` are acceptable)
- [ ] **Backend Implementation**: Services use `GetCurrentPersonIdRequired()` or similar methods to get authenticated user's person ID
- [ ] **Response Models**: Can still include user ID for display purposes
- [ ] **Entity Models**: Keep user ID for database storage

**Why This Matters:**

- Prevents user impersonation attacks
- Maintains data integrity and audit trails
- Ensures proper authorization
- Meets enterprise security requirements

### Deployment Pipeline

1. **Development**: Local development and testing
2. **Staging**: Integration testing environment
3. **Production**: Live application deployment

## Documentation Standards

### Component Documentation

Each component should include:

- Purpose and functionality
- Base component usage
- Configuration options
- Event handling
- Styling guidelines

### API Documentation

- OpenAPI/Swagger specifications
- Request/response examples
- Error handling patterns
- Authentication requirements

## Styling Guidelines

### BEM Methodology

```scss
.component {
  &__element {
    &--modifier {
      // Styles
    }
  }
}
```

### Material 3 Integration

- Use Material 3 theme variables
- Follow Material Design principles
- Implement consistent spacing
- Use semantic color tokens

### Responsive Design

- Mobile-first approach
- Breakpoint consistency
- Flexible layouts
- Touch-friendly interactions

## Debugging

### Common Issues

1. **Base Component Not Working**

   - Check imports and configuration
   - Verify event handler wiring
   - Test with minimal configuration

2. **Styling Conflicts**

   - Remove duplicate styles
   - Use component-specific selectors
   - Check Material theme integration

3. **Performance Issues**
   - Monitor bundle size
   - Check for memory leaks
   - Optimize API calls

### Debugging Tools

- Angular DevTools
- Chrome DevTools
- Network tab for API calls
- Performance profiler

## Best Practices

### Component Development

1. **Start with base component**: Always use appropriate base component
2. **Follow patterns**: Use established migration patterns
3. **Test thoroughly**: Comprehensive testing strategy
4. **Document changes**: Update documentation as needed

### Code Organization

1. **Feature-based structure**: Organize by feature, not type
2. **Shared components**: Use common directory for shared code
3. **Service separation**: Separate business logic from UI
4. **Type safety**: Use TypeScript strictly

### Performance Optimization

1. **Lazy loading**: Implement route-based code splitting
2. **OnPush strategy**: Use change detection strategies
3. **Memory management**: Proper cleanup in ngOnDestroy
4. **Bundle optimization**: Monitor and optimize bundle size

## Resources

### Documentation

- [Component Architecture](../architecture/component-architecture.md)
- [Quick Reference](../architecture/component-quick-reference.md)
- [Development Conventions](../development/conventions.md)

### External Resources

- [Angular Style Guide](https://angular.io/guide/styleguide)
- [Material Design](https://material.io/design)
- [Web Content Accessibility Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)

This workflow ensures consistent, high-quality development while maintaining the established component architecture patterns.
