# Nutrition Optimization Machine

A comprehensive nutrition and meal planning application built with Angular and .NET.

## 🏗️ Architecture

### Frontend (nom-ui)

- **Framework**: Angular 17 with standalone components
- **UI Library**: Angular Material 3
- **Component Architecture**: Base component pattern for consistency
- **State Management**: Reactive forms and services
- **Styling**: SCSS with Material 3 theming

### Backend (nom-api)

- **Framework**: .NET 8
- **Database**: Entity Framework Core
- **API**: RESTful with OpenAPI/Swagger
- **Authentication**: JWT-based

## �� Documentation

### Quick Start

- **[Getting Started Guide](./docs/README.md)** - Documentation index and project overview
- **[Development Conventions](./docs/development/conventions.md)** - Coding standards and patterns

### Architecture & Development

- **[Component Architecture](./docs/architecture/component-architecture.md)** - Detailed guide to the base component patterns
- **[Component Quick Reference](./docs/architecture/component-quick-reference.md)** - Fast lookup for component development
- **[Development Workflow](./docs/workflows/development-workflow.md)** - Complete development process and procedures

### Migration History

- **[Migration Progress](./nom-ui/BASE_COMPONENT_MIGRATION_PROGRESS.md)** - Complete migration history and patterns

## 🚀 Getting Started

### Prerequisites

- Node.js 18+
- .NET 8 SDK
- SQL Server or SQLite

### Frontend Setup

```bash
cd nom-ui
npm install
ng serve
```

### Backend Setup

```bash
cd nom-api
dotnet restore
dotnet run
```

## 🎯 Key Features

- **Recipe Management**: Create, edit, and organize recipes
- **Ingredient Database**: Comprehensive ingredient catalog with nutrition data
- **Meal Planning**: Plan meals and track nutrition goals
- **User Profiles**: Personalized nutrition tracking
- **Curation System**: Community-driven content moderation
- **Responsive Design**: Mobile-first approach

## 🏛️ Component Architecture

The application uses a base component pattern for consistency:

- **`app-base-form`**: Standardized form layouts
- **`app-base-page`**: Full page layouts with navigation
- **`app-base-detail`**: Detail views for single items
- **`app-base-list`**: List views with search and pagination

See [Component Architecture](./docs/architecture/component-architecture.md) for detailed patterns and guidelines.

## 🛠️ Development

### Creating New Components

1. Choose the appropriate base component type
2. Follow the established patterns in [Quick Reference](./docs/architecture/component-quick-reference.md)
3. Use the migration checklist for consistency
4. Test thoroughly for functionality and accessibility

### Code Quality

- Follow the conventions in [CONVENTIONS.md](./CONVENTIONS.md)
- Use the base component patterns for consistency
- Implement proper error handling and loading states
- Ensure responsive design and accessibility

## 📊 Project Structure

```
nutrition-optimization-machine/
├── docs/                        # Documentation
│   ├── architecture/            # System architecture docs
│   ├── development/             # Development guidelines
│   └── workflows/               # Process documentation
├── nom-ui/                      # Angular frontend
│   ├── src/app/
│   │   ├── common/             # Base components and shared code
│   │   ├── recipe/             # Recipe management
│   │   ├── person/             # User management
│   │   └── curation/           # Content moderation
├── nom-api/                     # .NET backend
│   ├── Nom.Api/                # API controllers
│   ├── Nom.Data/               # Data layer
│   └── Nom.Orch/               # Business logic
├── README.md                    # Project overview
└── LICENSE                      # Project license
```

## 🤝 Contributing

1. Follow the established component architecture patterns
2. Use the base components for consistency
3. Follow the coding conventions
4. Test thoroughly before submitting
5. Update documentation as needed

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
