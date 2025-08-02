# System Architecture

## Overview

The Nutritional Optimization Machine (NOM) is a comprehensive nutritional planning platform built with modern web technologies. The system follows a layered architecture pattern with clear separation of concerns between frontend, backend, and data layers.

## Technology Stack

### Frontend (nom-ui)

- **Framework**: Angular 17 with Standalone Components
- **UI Library**: Angular Material 3
- **Styling**: SCSS with BEM methodology
- **State Management**: RxJS for reactive programming
- **Authentication**: Bearer token with dual scheme support
- **Build Tool**: Angular CLI with AOT compilation

### Backend (nom-api)

- **Framework**: ASP.NET Core Web API
- **ORM**: Entity Framework Core
- **Database**: PostgreSQL 17
- **Authentication**: ASP.NET Core Identity with JWT Bearer tokens
- **Architecture**: Domain-driven design with orchestration services

### Database

- **Primary**: PostgreSQL 17 with MERGE statements
- **Schema**: Organized into functional schemas (recipe, plan, shopping, etc.)
- **Migrations**: Code-First Entity Framework migrations
- **Performance**: Materialized views and optimized indexes

## High-Level Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Frontend      │    │   Backend       │    │   Database      │
│   (Angular)     │◄──►│   (.NET Core)   │◄──►│   (PostgreSQL)  │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         │                       │                       │
    ┌─────────┐            ┌─────────┐            ┌─────────┐
    │Material │            │Orchestr.│            │Schemas  │
    │3 Theme  │            │Services │            │(recipe, │
    └─────────┘            └─────────┘            │plan,    │
                                                  │shopping)│
                                                  └─────────┘
```

## Frontend Architecture

### Component Architecture

- **Base Components**: Reusable UI patterns (`app-base-page`, `app-base-form`, `app-base-list`, `app-base-detail`)
- **Domain Components**: Feature-specific components organized by domain
- **Standalone Components**: All components use Angular 17 standalone architecture
- **Material 3**: Consistent theming with theme variables, no hardcoded colors

### Service Layer

- **Domain Services**: API communication for each domain
- **Utility Services**: Shared functionality (auth, notifications, events)
- **Event-Driven Communication**: `EventBusService` for loose coupling

### State Management

- **Reactive Programming**: RxJS for asynchronous operations
- **Service State**: Domain services manage their own state
- **Authentication State**: Centralized in `AuthManagerService`

## Backend Architecture

### API Layer

- **Controllers**: RESTful endpoints with proper authorization
- **Models**: Request/Response models (NO DTO suffixes)
- **Validation**: Fluent validation and model binding
- **Error Handling**: Consistent error responses

### Business Logic Layer

- **Orchestration Services**: Complex business logic coordination
- **Domain Services**: Entity-specific operations
- **Background Services**: Asynchronous processing (data export, anonymization)

### Data Access Layer

- **Entity Framework Core**: Code-First approach
- **Repositories**: Data access abstraction
- **Migrations**: Database schema evolution
- **Audit Logging**: Comprehensive data access tracking

## Database Architecture

### Schema Organization

```
recipe/          # Recipe and ingredient management
plan/            # Nutritional plans and participants
shopping/        # Shopping lists and items
privacy/         # Consent and data processing logs
reference/       # Static reference data
```

### Key Entities

- **Recipe Management**: `RecipeEntity`, `IngredientEntity`, `RecipeIngredientEntity`
- **User Management**: `PersonEntity`, `PersonAttributeEntity`
- **Privacy**: `UserConsentEntity`, `DataProcessingLogEntity`, `PrivacyRequestEntity`
- **Household**: `HouseholdEntity`, `HouseholdMemberEntity`
- **Shopping**: `ShoppingListEntity`, `ShoppingItemEntity`
- **Meal Planning**: `MealPlanEntity`, `MealPlanEntryEntity`

## Security Architecture

### Authentication

- **Dual Bearer Support**: `IdentityConstants.BearerScheme` and `JwtBearerDefaults.AuthenticationScheme`
- **Token Expiration**: 24-hour Bearer token expiration
- **Claims-Based Authorization**: `CanManageCuration`, `CanManageUserRoles`

### Data Protection

- **Encryption**: TLS for data in transit, database encryption at rest
- **Privacy by Design**: GDPR compliance built into architecture
- **Audit Logging**: Comprehensive access and modification tracking

## Integration Architecture

### Mealie Integration

The system has successfully integrated key functionality from the Mealie recipe management platform:

#### Backend Integration ✅

- **Enhanced Recipe System**: RecipeEntity with Mealie features (time properties, serving info, social features)
- **New Entities**: 20+ entities for comments, ratings, assets, notes, timeline, share tokens, tags, categories
- **Household System**: Complete household management with invite tokens and member management
- **Shopping Lists**: Full shopping list functionality with items and labels
- **Meal Planning**: Comprehensive meal planning with rules and date-based entries

#### Frontend Foundation ✅

- **Routing**: Lazy-loaded feature routes for household, shopping, and meal plan functionality
- **Models**: TypeScript models with explicit property assignment
- **Services**: Angular services for API communication
- **Navigation**: Updated main navigation with new feature links

## Performance Architecture

### Database Optimization

- **Quality Filtering**: 490K ingredients filtered to 8,049 high-quality ingredients
- **Materialized Views**: Common query optimization
- **Indexes**: Performance indexes for search and filtering
- **Batch Processing**: Configurable batch sizes for data operations

### Frontend Performance

- **AOT Compilation**: Ahead-of-time compilation for faster loading
- **Lazy Loading**: Feature modules loaded on demand
- **Material 3**: Optimized theming with CSS custom properties
- **Source Maps**: Development debugging optimization

## Privacy Architecture

### GDPR Compliance

- **Consent Management**: Granular consent collection and storage
- **Data Subject Rights**: Access, rectification, erasure, portability
- **Audit Trail**: Comprehensive data processing logging
- **Retention Policies**: Automated data retention management

### Data Processing

- **Lawful Basis**: All processing documented with legal basis
- **Data Minimization**: Only necessary data collected
- **Purpose Limitation**: Data used only for stated purposes
- **Cross-Border Transfers**: Compliance with international data transfer requirements

## Development Architecture

### Code Organization

- **Domain-Driven**: Features organized by business domain
- **Convention-Based**: Strict naming conventions and patterns
- **Modular**: Independent development and testing of features
- **Documentation**: Comprehensive inline and external documentation

### Quality Assurance

- **Unit Testing**: Service and component testing
- **Integration Testing**: API endpoint testing
- **Privacy Reviews**: All features involving personal data
- **Code Reviews**: Pull request reviews with privacy focus

## Current Implementation Status

### Backend Status: COMPLETE ✅

- **Database Schema**: All entities implemented
- **API Controllers**: All endpoints with proper authorization
- **Orchestration Services**: Complete business logic layer
- **Data Models**: All request/response models implemented
- **Database Migrations**: All schema changes applied

### Frontend Status: PARTIALLY COMPLETE 🔄

- **Recipe Management**: ✅ IMPLEMENTED - Full CRUD functionality
- **Curation Queue**: ✅ COMPLETE - Admin interface with Material 3 theming
- **Authentication**: ✅ COMPLETE - Full authentication flow
- **Privacy Features**: ✅ COMPLETE - Data subject rights management
- **Household Management**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Shopping Lists**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Meal Planning**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Messaging System**: 🔄 PARTIAL - Backend complete, frontend inbox UI in progress

### Next Priorities

1. **Complete Frontend Components**: Household, shopping, and meal plan components
2. **Complete Curation Queue UI**: Finish admin interface
3. **Complete Messaging System**: Implement frontend messaging inbox
4. **Complete Multi-Participant Onboarding**: Finish remaining onboarding workflow
5. **Advanced Recipe Features**: Comments, ratings, assets, timeline, notes, tags, categories
6. **Integration Testing**: Comprehensive testing of all Mealie integration features
7. **Performance Optimization**: Database migrations, indexing, and query optimization

## Architecture Principles

### Design Principles

- **Separation of Concerns**: Clear boundaries between layers
- **Single Responsibility**: Each component has one clear purpose
- **Dependency Injection**: Loose coupling through DI containers
- **Event-Driven**: Asynchronous communication between services
- **Privacy by Design**: GDPR compliance built into architecture

### Development Principles

- **Convention Over Configuration**: Strict naming and organization conventions
- **Code Quality**: Comprehensive testing and code reviews
- **Documentation**: Living documentation updated with code changes
- **Security First**: Authentication and authorization at every layer
- **Performance Conscious**: Optimization at database and application levels
