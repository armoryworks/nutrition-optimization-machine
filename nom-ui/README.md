# 🎨 NOM UI - Frontend Application

The frontend application for the Nutrition Optimization Machine (NOM), built with Angular 17+ and Material Design 3.

[![Angular](https://img.shields.io/badge/Angular-17+-red.svg)](https://angular.io/)
[![Material](https://img.shields.io/badge/Material-3-blue.svg)](https://material.angular.io/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5+-blue.svg)](https://www.typescriptlang.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-blue.svg)](Dockerfile)

## 🏗️ **Architecture**

### **Modern Angular Features**

- ✅ **Angular 17+** - Latest Angular features and performance improvements
- ✅ **Standalone Components** - Modern component architecture without NgModules
- ✅ **Material Design 3** - Latest Material Design system
- ✅ **Reactive Forms** - Type-safe form handling
- ✅ **Modern Control Flow** - `@if`, `@for`, `@switch` syntax
- ✅ **Base Component Pattern** - Consistent, reusable component architecture

### **Project Structure**

```
nom-ui/src/app/
├── 📁 common/                   # 🔧 Shared utilities & base components
│   ├── components/             # Reusable UI components
│   ├── services/               # Shared services
│   └── interfaces/             # TypeScript interfaces
├── 📁 recipe/                   # 🍳 Recipe management features
│   ├── components/             # Recipe-specific components
│   ├── services/               # Recipe business logic
│   └── models/                 # Recipe data models
├── 📁 meal-plan/                # 📅 Meal planning functionality
├── 📁 shopping/                 # 🛒 Shopping list management
├── 📁 person/                   # 👤 User management & profiles
├── 📁 curation/                 # ✅ Content moderation
├── 📁 privacy/                  # 🔒 Privacy & GDPR compliance
└── 📁 household/                # 🏠 Multi-user household features
```

## 🚀 **Quick Start**

### **Development Setup**

```bash
# Navigate to frontend
cd nom-ui

# Install dependencies
npm install

# Start development server
ng serve

# Open browser
open http://localhost:4200
```

### **Docker Development**

```bash
# Build and run with Docker
docker build -t nom-ui .
docker run -p 80:80 nom-ui

# Or use docker-compose (from project root)
docker-compose up nom-ui
```

## 🧩 **Component Architecture**

### **Base Component Pattern**

The application uses a sophisticated base component system for consistency:

```typescript
// Base components provide consistent patterns
export abstract class BaseFormComponent<T> {
  abstract form: FormGroup;
  abstract onSubmit(): void;
  abstract onCancel(): void;
}

// Concrete implementations extend base components
export class RecipeFormComponent extends BaseFormComponent<Recipe> {
  // Implementation specific to recipes
}
```

### **Component Types**

| Base Component          | Purpose           | Usage                  |
| ----------------------- | ----------------- | ---------------------- |
| **BasePageComponent**   | Full page layouts | Main application pages |
| **BaseFormComponent**   | Form handling     | Create/edit forms      |
| **BaseListComponent**   | List views        | Data tables and lists  |
| **BaseDetailComponent** | Detail views      | Single item displays   |
| **BaseModalComponent**  | Modal dialogs     | Popup interactions     |

### **Dynamic Data Components**

Advanced components that work with backend reference data:

```typescript
// Generic selector for any reference data
<app-reference-selector
  [discriminatorId]="REFERENCE_IDS.RECIPE_DIFFICULTY_TYPE"
  [control]="difficultyControl"
  label="Difficulty Level">
</app-reference-selector>
```

## 🎨 **Design System**

### **Material Design 3**

- ✅ **Modern Theming** - Material 3 color system and typography
- ✅ **Responsive Design** - Mobile-first approach with desktop optimization
- ✅ **Accessibility** - WCAG 2.1 compliance throughout
- ✅ **Dark Mode Support** - Automatic theme switching
- ✅ **Custom Components** - Extended Material components

### **Styling Architecture**

```scss
// SCSS with BEM methodology
.recipe-card {
  &__header {
    // Header styles
  }

  &__content {
    // Content styles
  }

  &--featured {
    // Featured variant
  }
}
```

### **Desktop UI Optimization**

- ✅ **Compact Headers** - 75% height reduction for desktop efficiency
- ✅ **1800x850 Viewport** - Optimized for standard desktop resolutions
- ✅ **No Scrolling** - Primary workflows fit within viewport
- ✅ **Efficient Layouts** - Horizontal layouts for desktop productivity

## 🔧 **Development**

### **Available Scripts**

```bash
# Development
npm start                    # Start dev server
npm run build               # Build for production
npm run watch               # Build and watch for changes

# Code Quality
npm run lint                # Run ESLint
npm run lint:fix            # Fix linting issues
npm run format              # Format code with Prettier

# Testing
npm run test                # Run unit tests
npm run test:watch          # Run tests in watch mode
npm run test:coverage       # Run tests with coverage
npm run e2e                 # Run end-to-end tests
```

### **Code Generation**

```bash
# Generate new components
ng generate component recipe/components/recipe-card --standalone

# Generate services
ng generate service recipe/services/recipe-api

# Generate interfaces
ng generate interface recipe/models/recipe
```

## 🧪 **Testing**

### **Testing Strategy**

- ✅ **Unit Tests** - Karma + Jasmine for component testing
- ✅ **Integration Tests** - Testing component interactions
- ✅ **E2E Tests** - Cypress for end-to-end workflows
- ✅ **Visual Tests** - Component screenshot testing

### **Test Commands**

```bash
# Unit tests
npm run test                # Run once
npm run test:watch          # Watch mode
npm run test:coverage       # With coverage report

# E2E tests (see nom-test directory)
cd ../nom-test
npm run test:integration    # Full integration tests
npm run test:recipes        # Recipe-specific tests
```

### **Test Coverage**

The application maintains high test coverage across:

- ✅ **Component Logic** - Business logic and user interactions
- ✅ **Service Integration** - API communication and data handling
- ✅ **Form Validation** - Input validation and error handling
- ✅ **User Workflows** - Complete user journey testing

## 🔐 **Security Features**

### **Frontend Security**

- ✅ **JWT Token Management** - Secure token storage and refresh
- ✅ **Route Guards** - Authentication and authorization guards
- ✅ **Input Sanitization** - XSS prevention throughout
- ✅ **CSRF Protection** - Cross-site request forgery prevention
- ✅ **Content Security Policy** - CSP headers for security

### **Privacy Compliance**

- ✅ **GDPR Compliance** - Complete data subject rights implementation
- ✅ **Consent Management** - Granular consent collection and withdrawal
- ✅ **Data Export** - User data export functionality
- ✅ **Right to Erasure** - Complete data deletion capabilities

## 📱 **Responsive Design**

### **Breakpoints**

```scss
// Mobile-first responsive design
@media (max-width: 599px) {
  // Mobile styles
}

@media (min-width: 600px) and (max-width: 1023px) {
  // Tablet styles
}

@media (min-width: 1024px) {
  // Desktop styles
}
```

### **Mobile Features**

- ✅ **Touch-Friendly** - Large touch targets and gestures
- ✅ **Offline Support** - Service worker for offline functionality
- ✅ **Progressive Web App** - PWA features for mobile installation
- ✅ **Performance Optimized** - Lazy loading and code splitting

## 🎯 **Key Features**

### **Recipe Management**

- ✅ **Advanced Search** - Multi-criteria recipe search with filters
- ✅ **AI Suggestions** - Intelligent recipe recommendations
- ✅ **Ingredient Management** - Comprehensive ingredient database
- ✅ **Nutrition Analysis** - Real-time nutritional calculations
- ✅ **Recipe Import** - Web scraping from popular recipe sites

### **Meal Planning**

- ✅ **Smart Planning** - AI-assisted meal plan generation
- ✅ **Dietary Restrictions** - Comprehensive dietary restriction support
- ✅ **Calendar Integration** - Visual meal planning calendar
- ✅ **Shopping Lists** - Auto-generated shopping lists from meal plans

### **User Experience**

- ✅ **Multi-Step Onboarding** - Personalized user setup
- ✅ **Household Management** - Multi-user household support
- ✅ **Content Curation** - Community-driven quality control
- ✅ **Messaging System** - In-app communication features

## 🔧 **Configuration**

### **Environment Configuration**

```typescript
// environment.ts
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000/api',
  features: {
    aiSuggestions: true,
    webScraping: true,
    messaging: true,
  },
};
```

### **Proxy Configuration**

```json
// proxy.config.json
{
  "/api/*": {
    "target": "http://localhost:5000",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  }
}
```

## 🐳 **Docker Deployment**

### **Multi-Stage Build**

```dockerfile
# Stage 1: Build
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

# Stage 2: Serve
FROM nginx:alpine
COPY --from=build /app/dist/nom-ui/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
```

### **Production Features**

- ✅ **Nginx Optimization** - Gzip compression, caching headers
- ✅ **Security Headers** - CSP, HSTS, XSS protection
- ✅ **Rate Limiting** - Request throttling at nginx level
- ✅ **Health Checks** - Built-in health check endpoints

## 📈 **Performance**

### **Build Optimization**

- ✅ **AOT Compilation** - Ahead-of-time compilation for performance
- ✅ **Tree Shaking** - Unused code elimination
- ✅ **Code Splitting** - Lazy loading for optimal bundle sizes
- ✅ **Service Workers** - Caching and offline support

### **Runtime Performance**

- ✅ **OnPush Strategy** - Optimized change detection
- ✅ **Virtual Scrolling** - Efficient large list rendering
- ✅ **Image Optimization** - Lazy loading and responsive images
- ✅ **Bundle Analysis** - Regular bundle size monitoring

## 📚 **Documentation**

### **Component Documentation**

- 🧩 **[Component Architecture](../docs/architecture/component-architecture.md)** - Detailed component patterns
- 📋 **[Quick Reference](../docs/architecture/component-quick-reference.md)** - Fast lookup guide
- 🔗 **[Component Library](../docs/architecture/component-library.md)** - Dynamic data components
- 🔄 **[Migration Guide](../docs/development/migration-guide.md)** - Component migration patterns

### **Development Guides**

- 📏 **[Development Standards](../docs/DEVELOPMENT_STANDARDS.md)** - **MANDATORY** conventions
- 🛠️ **[Development Workflow](../docs/workflows/development-workflow.md)** - Complete development process
- 🔧 **[Troubleshooting](../docs/development/troubleshooting.md)** - Common issues and solutions

## 🤝 **Contributing**

### **Development Standards**

1. **Follow Component Architecture** - Use established base component patterns
2. **Modern Angular Patterns** - Use standalone components and modern control flow
3. **Maintain Accessibility** - Ensure WCAG 2.1 compliance
4. **Test Thoroughly** - Include unit tests and update E2E tests
5. **Document Changes** - Update component documentation

### **Code Quality Standards**

- ✅ **File Separation** - One component/service/interface per file
- ✅ **Naming Conventions** - Follow Angular style guide
- ✅ **TypeScript Strict** - Strict TypeScript configuration
- ✅ **ESLint Rules** - Comprehensive linting rules
- ✅ **Prettier Formatting** - Consistent code formatting

## 🆘 **Troubleshooting**

### **Common Issues**

1. **Build Errors** - Check Node.js version (20+) and clear node_modules
2. **API Connection** - Verify proxy configuration and backend status
3. **Styling Issues** - Check Material theme imports and SCSS compilation
4. **Performance** - Use Angular DevTools for performance analysis

### **Development Support**

- 📚 **Documentation**: [../docs/README.md](../docs/README.md)
- 🐛 **Troubleshooting**: [../docs/development/troubleshooting.md](../docs/development/troubleshooting.md)
- 🧪 **Testing Guide**: [../nom-test/README.md](../nom-test/README.md)

---

**The NOM UI is modern, accessible, and production-ready!** ✨
