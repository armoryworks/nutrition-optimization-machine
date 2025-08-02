# Documentation

Welcome to the NOM (Nutritional Optimization Machine) documentation. This directory contains comprehensive documentation organized by category to help developers, stakeholders, and AI tools understand the system architecture, requirements, and implementation status.

## 📚 Documentation Categories

### 🏗️ Architecture

- **[System Architecture](architecture/system-architecture.md)** - High-level technical architecture and current implementation status
- **[Component Architecture](architecture/component-architecture.md)** - Frontend component patterns and base component usage
- **[Component Quick Reference](architecture/component-quick-reference.md)** - Quick lookup guide for base component selection and usage
- **[C#/Entity Framework Patterns](architecture/csharp-entity-framework-patterns.md)** - Comprehensive backend architecture patterns and best practices
- **[Technical Inference Rules](architecture/technical-inference-rules.md)** - Complete technical specifications derived from codebase analysis

### 📋 Requirements

- **[Functional Requirements](requirements/functional-requirements.md)** - Detailed functional requirements organized by domain and implementation status
- **[Non-Functional Requirements](requirements/non-functional-requirements.md)** - Performance, security, privacy, and usability requirements
- **[User Roles & Personas](requirements/user-roles-personas.md)** - Comprehensive user role definitions and permission matrix
- **[Business Rules](requirements/business-rules.md)** - Core business domains and governing rules for data integrity
- **[Implementation Status](requirements/implementation-status.md)** - Detailed implementation status across all system components

### 🛠️ Development

- **[Conventions](development/conventions.md)** - Coding standards, naming conventions, and architectural patterns
- **[Guidelines](development/guidelines.md)** - General development guidelines and best practices
- **[Decision Trees](development/decision-trees.md)** - Clear guidance for common development decisions
- **[Code Patterns](development/code-patterns.md)** - Reusable code patterns and templates
- **[Troubleshooting](development/troubleshooting.md)** - Solutions for common development issues
- **[API Reference](development/api-reference.md)** - Backend API endpoints and usage

### 🔄 Workflows

- **[Development Workflow](workflows/development-workflow.md)** - Development process, code review, and quality assurance
- **[In-Process Tasks](workflows/in-process-tasks.md)** - Tracking for ongoing migrations and development tasks

## 🎯 Quick Start for AI Tools

For AI tools like Cursor AI, start with these key documents:

1. **[AI Development Guide](ai-development-guide.md)** - Specific instructions and patterns for AI tools
2. **[System Architecture](architecture/system-architecture.md)** - Understand the overall technical structure
3. **[C#/Entity Framework Patterns](architecture/csharp-entity-framework-patterns.md)** - Backend architecture patterns and best practices
4. **[Technical Inference Rules](architecture/technical-inference-rules.md)** - Complete technical specifications and rules
5. **[Implementation Status](requirements/implementation-status.md)** - See what's been completed and what remains
6. **[Functional Requirements](requirements/functional-requirements.md)** - Understand the system's intended functionality
7. **[Component Architecture](architecture/component-architecture.md)** - Learn the frontend component patterns
8. **[Component Quick Reference](architecture/component-quick-reference.md)** - Quick lookup guide for base components
9. **[Conventions](development/conventions.md)** - Follow the established coding standards

## 📊 Current Project Status

### Backend Status: COMPLETE ✅

- All database entities implemented
- All API controllers with proper authorization
- All orchestration services with business logic
- All data models for API communication
- All database migrations applied
- **NEW**: Comprehensive C#/Entity Framework patterns documented
- **NEW**: Technical inference rules and specifications established

### Frontend Status: PARTIALLY COMPLETE 🔄

- **Recipe Management**: ✅ IMPLEMENTED - Full CRUD functionality
- **Curation Queue**: ✅ COMPLETE - Admin interface with Material 3 theming
- **Authentication**: ✅ COMPLETE - Full authentication flow
- **Privacy Features**: ✅ COMPLETE - Data subject rights management
- **Household Management**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Shopping Lists**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Meal Planning**: 🔄 FOUNDATION - Backend complete, frontend components in progress
- **Messaging System**: 🔄 PARTIAL - Backend complete, frontend inbox UI in progress

## 🚀 Key Features

### ✅ Completed Features

- **User Onboarding**: Multi-step onboarding with dietary restriction collection
- **Authentication**: Dual Bearer token support with 24-hour expiration
- **Privacy Compliance**: Full GDPR compliance with data subject rights
- **Recipe Management**: Complete CRUD with ingredient search and modal creation
- **Curation System**: Admin interface for content review and approval
- **Data Import**: Quality-filtered ingredient import with AI enhancement
- **Mealie Integration**: Household, shopping, and meal planning backend
- **Technical Documentation**: Comprehensive backend architecture patterns and specifications

### 🔄 In Progress

- **Frontend Components**: Household, shopping, and meal planning UI
- **Messaging System**: Complete frontend messaging interface
- **Multi-Participant Onboarding**: Finish remaining onboarding workflow

## 🎨 Design System

### Frontend Architecture

- **Framework**: Angular 17 with Standalone Components
- **UI Library**: Angular Material 3
- **Styling**: SCSS with BEM methodology
- **Base Components**: Reusable UI patterns (`app-base-page`, `app-base-form`, `app-base-list`, `app-base-detail`)

### Backend Architecture

- **Framework**: ASP.NET Core Web API
- **ORM**: Entity Framework Core
- **Database**: PostgreSQL 17
- **Authentication**: ASP.NET Core Identity with JWT Bearer tokens
- **Architecture**: Domain-driven design with orchestration services
- **Patterns**: Repository pattern via direct DbContext usage
- **Caching**: Memory cache with configurable expiration
- **Performance**: Compiled queries and efficient loading patterns

## 🔒 Security & Privacy

### Security Features

- **Dual Bearer Support**: `IdentityConstants.BearerScheme` and `JwtBearerDefaults.AuthenticationScheme`
- **Claims-Based Authorization**: `CanManageCuration`, `CanManageUserRoles` policies
- **Token Expiration**: 24-hour Bearer token expiration
- **Input Validation**: Comprehensive input sanitization and validation
- **Rate Limiting**: Request rate limiting with memory cache
- **Security Middleware**: Comprehensive security middleware stack

### Privacy Compliance

- **GDPR Compliance**: Full GDPR compliance implementation
- **Consent Management**: Granular consent collection and withdrawal
- **Data Subject Rights**: Access, Rectification, Erasure, Portability
- **Audit Logging**: Comprehensive data processing audit trails

## 📈 Performance Optimizations

### Database Performance

- **Quality Filtering**: 490K ingredients filtered to 8,049 high-quality ingredients
- **Materialized Views**: Common query optimization for frequently accessed data
- **Performance Indexes**: Optimized indexes for search and filtering operations
- **PostgreSQL 17 MERGE**: Replaced ON CONFLICT with MERGE statements for better performance
- **Compiled Queries**: Pre-compiled queries for frequently executed operations
- **Efficient Loading**: Projections and AsNoTracking for read-only operations

### Frontend Performance

- **AOT Compilation**: Ahead-of-time compilation for faster loading
- **Lazy Loading**: Feature modules loaded on demand
- **Material 3**: Optimized theming with CSS custom properties
- **Code Splitting**: Efficient bundle splitting for better caching

### Caching Strategies

- **Reference Data**: 30-minute cache for frequently accessed reference data
- **Session Data**: 24-hour cache for user session information
- **Rate Limiting**: 1-minute cache for rate limiting data
- **Memory Management**: Efficient memory usage with proper disposal patterns

## 🤖 AI Tool Optimization

This documentation is structured to be AI-friendly with:

- **Clear Hierarchies**: Logical organization by category and domain
- **Status Indicators**: Clear completion status for all features
- **Implementation Details**: Specific technical implementation information
- **Code Examples**: Practical examples and patterns
- **Cross-References**: Links between related documents
- **Structured Data**: Tables and lists for easy parsing
- **Technical Rules**: Comprehensive inference rules and specifications

### For AI Development Assistance

When working with AI tools, reference these documents in order:

1. Start with **[AI Development Guide](ai-development-guide.md)** for specific AI instructions
2. Review **[System Architecture](architecture/system-architecture.md)** for technical overview
3. Study **[C#/Entity Framework Patterns](architecture/csharp-entity-framework-patterns.md)** for backend patterns
4. Reference **[Technical Inference Rules](architecture/technical-inference-rules.md)** for complete specifications
5. Check **[Implementation Status](requirements/implementation-status.md)** for current progress
6. Review **[Functional Requirements](requirements/functional-requirements.md)** for feature understanding
7. Follow **[Conventions](development/conventions.md)** for coding standards
8. Use **[Component Architecture](architecture/component-architecture.md)** for frontend patterns
9. Reference **[Component Quick Reference](architecture/component-quick-reference.md)** for quick lookups

## 📝 Documentation Standards

### Writing Guidelines

- **Clear and Concise**: Use simple, direct language
- **Status Indicators**: Always include implementation status
- **Code Examples**: Provide practical, working examples
- **Cross-References**: Link related documents and sections
- **AI-Friendly**: Structure content for easy parsing by AI tools
- **Technical Depth**: Include comprehensive technical specifications
- **Pattern Documentation**: Document all architectural patterns and rules

### Maintenance

- **Living Documentation**: Updated with code changes
- **Version Control**: All documentation in version control
- **Review Process**: Documentation reviewed with code changes
- **Accessibility**: Clear navigation and search capabilities
- **Technical Accuracy**: Regular validation of technical specifications

## 🔗 Related Resources

- **[Main README](../README.md)** - Project overview and getting started
- **[Conventions](../docs/development/conventions.md)** - Coding standards and patterns
- **[Component Architecture](../docs/architecture/component-architecture.md)** - Frontend component patterns
- **[Implementation Status](../docs/requirements/implementation-status.md)** - Current development status
- **[C#/Entity Framework Patterns](../docs/architecture/csharp-entity-framework-patterns.md)** - Backend architecture patterns
- **[Technical Inference Rules](../docs/architecture/technical-inference-rules.md)** - Complete technical specifications

---

_Last Updated: July 30, 2025_  
_Version: 3.0_  
_Status: Active Development with Comprehensive Technical Documentation_
