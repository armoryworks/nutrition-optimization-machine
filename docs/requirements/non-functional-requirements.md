# Non-Functional Requirements

## Overview

This document outlines the non-functional requirements for the Nutritional Optimization Machine (NOM) platform, organized by category and implementation status. Requirements are categorized as COMPLETE, IMPLEMENTED, PARTIAL, or NOT IMPLEMENTED based on current development status.

## Performance Requirements

### Objective

To ensure the system responds quickly and efficiently to user interactions, providing a smooth user experience.

### Requirements

| ID        | Requirement         | Status      | Details                                                            |
| --------- | ------------------- | ----------- | ------------------------------------------------------------------ |
| NFR-6.1.1 | Responsive UI       |  COMPLETE | User interface loads quickly and responds smoothly to interactions |
| NFR-6.1.2 | Efficient API Calls |  COMPLETE | Backend API endpoints respond within acceptable timeframes         |

### Performance Optimizations Implemented

#### Database Performance

- **Quality Filtering**: 490K ingredients filtered to 8,049 high-quality ingredients
- **Materialized Views**: Common query optimization for frequently accessed data
- **Performance Indexes**: Optimized indexes for search and filtering operations
- **Batch Processing**: Configurable batch sizes (5,000 records) for data operations
- **PostgreSQL 17 MERGE**: Replaced ON CONFLICT with MERGE statements for better performance

#### Frontend Performance

- **AOT Compilation**: Ahead-of-time compilation for faster loading
- **Lazy Loading**: Feature modules loaded on demand
- **Material 3**: Optimized theming with CSS custom properties
- **Source Maps**: Development debugging optimization
- **Standalone Components**: Better tree-shaking and modularity

## Security Requirements

### Objective

To protect sensitive user data and ensure secure system access.

### Requirements

| ID        | Requirement             | Status      | Details                                                                              |
| --------- | ----------------------- | ----------- | ------------------------------------------------------------------------------------ |
| NFR-6.2.1 | Data Protection         |  COMPLETE | All sensitive user data transmitted and stored securely                              |
| NFR-6.2.2 | Input Sanitization      |  COMPLETE | User inputs properly sanitized and validated                                         |
| NFR-6.2.3 | Access Control          |  COMPLETE | Proper authorization checks for data access and actions                              |
| NFR-6.2.4 | Encryption              |  COMPLETE | Personal data encrypted in transit (TLS) and at rest                                 |
| NFR-6.2.5 | Authentication Security |  COMPLETE | Secure authentication with password complexity and 2FA                               |
| NFR-6.2.6 | User ID Security        |  COMPLETE | Frontend never sends user IDs of the in-context user, backend gets from auth context |

### Security Implementations

#### Authentication & Authorization

- **Dual Bearer Support**: `IdentityConstants.BearerScheme` and `JwtBearerDefaults.AuthenticationScheme`
- **Token Expiration**: 24-hour Bearer token expiration
- **Claims-Based Authorization**: `CanManageCuration`, `CanManageUserRoles` policies
- **Two-Factor Authentication**: Optional 2FA with authenticator app support
- **Password Security**: Complexity requirements and secure reset mechanisms
- **User ID Security**: Frontend models never include user identification fields of the in-context user (AuthorId, CreatedById, UserId, PersonId), backend services receive user ID from authentication context

#### Data Protection

- **TLS Encryption**: All data in transit encrypted with TLS
- **Database Encryption**: Sensitive data encrypted at rest
- **Input Validation**: Comprehensive input sanitization and validation
- **XSS Prevention**: Proper output encoding and input filtering
- **SQL Injection Prevention**: Parameterized queries and EF Core protection

## Privacy & Compliance Requirements

### Objective

To ensure compliance with applicable data privacy regulations and provide users with control over their personal data.

### Requirements

| ID        | Requirement        | Status      | Details                                                    |
| --------- | ------------------ | ----------- | ---------------------------------------------------------- |
| NFR-6.3.1 | GDPR Compliance    |  COMPLETE | Full compliance with GDPR requirements for EU users        |
| NFR-6.3.2 | Consent Management |  COMPLETE | Granular, documented, and easily withdrawable consents     |
| NFR-6.3.3 | Data Portability   |  COMPLETE | Users can export data in standard, machine-readable format |
| NFR-6.3.4 | Right to Erasure   |  COMPLETE | Secure deletion of personal data upon user request         |
| NFR-6.3.5 | Audit Logging      |  COMPLETE | All access to and modification of personal data logged     |
| NFR-6.3.6 | Privacy by Design  |  COMPLETE | Privacy considerations integrated into system architecture |

### Privacy Implementations

#### GDPR Compliance

- **Consent Management**: Granular consent collection and storage in `UserConsentEntity`
- **Data Subject Rights**: Complete implementation of GDPR rights (Access, Rectification, Erasure, Portability)
- **Audit Trail**: Comprehensive logging in `DataProcessingLogEntity`
- **Retention Policies**: Automated data retention aligned with privacy policies
- **Privacy Dashboard**: User-friendly interface for managing privacy settings

#### Data Processing

- **Lawful Basis**: All processing documented with valid legal basis
- **Data Minimization**: Only necessary personal data collected
- **Purpose Limitation**: Data used only for stated purposes
- **Cross-Border Transfers**: Compliance with international data transfer requirements

## Maintainability & Scalability Requirements

### Objective

To ensure the system can be easily maintained, extended, and scaled to meet growing demands.

### Requirements

| ID        | Requirement            | Status      | Details                                                       |
| --------- | ---------------------- | ----------- | ------------------------------------------------------------- |
| NFR-6.4.1 | Modular Architecture   |  COMPLETE | Domain-driven architecture for independent development        |
| NFR-6.4.2 | Consistent Conventions |  COMPLETE | Strict adherence to naming conventions and design patterns    |
| NFR-6.4.3 | Separation of Concerns |  COMPLETE | Clear boundaries between UI, business logic, and data access  |
| NFR-6.4.4 | Extensibility          |  COMPLETE | Easy addition of new features without significant refactoring |

### Maintainability Implementations

#### Architecture Patterns

- **Domain-Driven Design**: Features organized by business domain
- **Layered Architecture**: Clear separation between presentation, business, and data layers
- **Event-Driven Communication**: Loose coupling through `EventBusService`
- **Dependency Injection**: IoC containers for service management

#### Code Quality

- **Naming Conventions**: Strict PascalCase, camelCase, and kebab-case patterns
- **NO DTO Rule**: Consistent use of `Model`, `Request`, `Response` suffixes
- **BEM Methodology**: Maintainable CSS/SCSS structure
- **Material 3 Theming**: Consistent UI with theme variables
- **Standalone Components**: Angular 22 modular architecture

#### Extensibility Features

- **Reference Data System**: Extensible system for managing predefined lists
- **Plugin Architecture**: Service-based architecture for easy feature addition
- **API Versioning**: Support for API versioning when needed
- **Database Migrations**: Code-First approach for schema evolution

## Usability Requirements

### Objective

To provide an intuitive and accessible user experience across all devices and user types.

### Requirements

| ID        | Requirement                            | Status      | Details                                                 |
| --------- | -------------------------------------- | ----------- | ------------------------------------------------------- |
| NFR-6.5.1 | Intuitive Workflow                     |  COMPLETE | Easy to understand and navigate for all users           |
| NFR-6.5.2 | Clear Feedback                         |  COMPLETE | Immediate and clear feedback on user actions            |
| NFR-6.5.3 | Responsive Design                      |  COMPLETE | Seamless adaptation to various screen sizes             |
| NFR-6.5.4 | Privacy Transparency                   |  COMPLETE | Clear, understandable privacy policies                  |
| NFR-6.5.5 | Material 3 Theming & Responsive Design |  COMPLETE | Theme variables, light/dark themes, full responsiveness |

### Usability Implementations

#### Foundational Design Principles

All UI design and development is governed by five immutable principles (see `nom-ui/docs/DESIGN-SPECIFICATION.md`, Section 1):

- **8pt Grid System**: All spatial decisions use 8px atomic unit. Consistent spacing across all components.
- **Major Third (1.25) Type Scale**: Mathematical typographic hierarchy (H1: ~39px, H2: ~25px, H3: 20px, Body: 16px). Headers enable 3-second page skim.
- **60-30-10 Color Rule**: 60% neutral surfaces, 30% secondary content, 10% action accent. Color is semantic (state), not decorative.
- **Information Architecture**: Z-pattern (desktop), prioritized vertical stack (mobile). Assistant-first flow with proactive content.
- **Micro-interactions**: Standard 300ms cubic-bezier transitions, 44px minimum touch targets, explicit hover/focus/active/disabled states on all interactive elements.

#### User Experience

- **Multi-Step Onboarding**: Guided workflow with clear progress indicators
- **Form Validation**: Real-time validation with clear error messages
- **Loading States**: Visual feedback during asynchronous operations
- **Error Handling**: User-friendly error messages and recovery options
- **Accessibility**: WCAG 2.1 AA compliance with proper ARIA labels, keyboard navigation, and 4.5:1 minimum contrast
- **Elevation System**: Soft shadows communicate interactivity and depth (cards at rest, elevated on hover, modals above content)

#### Responsive Design

- **Mobile-First**: Responsive design starting from mobile devices
- **Material 3**: Modern design system with consistent theming
- **Touch-Friendly**: All interactive elements have 44x44px minimum touch targets (Fitts's Law)
- **Cross-Browser**: Compatibility across major browsers
- **Progressive Enhancement**: Core functionality works without JavaScript
- **Reflow Logic**: Desktop caps text at 45-75 characters; mobile transitions to single-column vertical stack

#### Privacy Transparency

- **Clear Language**: Privacy policies written in understandable language
- **Granular Control**: Users can manage specific privacy settings
- **Visual Indicators**: Clear status indicators for privacy settings
- **Easy Access**: Privacy controls easily accessible throughout the application

## Reliability Requirements

### Objective

To ensure the system operates reliably and handles errors gracefully.

### Requirements

| ID        | Requirement          | Status      | Details                                                 |
| --------- | -------------------- | ----------- | ------------------------------------------------------- |
| NFR-6.6.1 | Error Handling       |  COMPLETE | Graceful handling of errors with user-friendly messages |
| NFR-6.6.2 | Data Integrity       |  COMPLETE | Consistent data state and validation                    |
| NFR-6.6.3 | Backup & Recovery    |  PARTIAL  | Data backup and recovery procedures                     |
| NFR-6.6.4 | Monitoring & Logging |  COMPLETE | Comprehensive system monitoring and logging             |

### Reliability Implementations

#### Error Handling

- **Frontend Error Handling**: Comprehensive error boundaries and user-friendly messages
- **Backend Error Handling**: Consistent error responses with proper HTTP status codes
- **Validation**: Client-side and server-side validation for data integrity
- **Retry Logic**: Automatic retry for transient failures
- **Graceful Degradation**: System continues to function with reduced features

#### Data Integrity

- **Database Constraints**: Foreign key constraints and data validation
- **Transaction Management**: ACID compliance for critical operations
- **Audit Logging**: Comprehensive tracking of data changes
- **Data Validation**: Multi-layer validation (client, API, database)

#### Monitoring & Logging

- **Application Logging**: Structured logging with different levels
- **Performance Monitoring**: Response time and resource usage tracking
- **Error Tracking**: Comprehensive error logging and alerting
- **User Activity**: Privacy-compliant activity logging

## Scalability Requirements

### Objective

To ensure the system can handle growing user loads and data volumes.

### Requirements

| ID        | Requirement          | Status      | Details                                          |
| --------- | -------------------- | ----------- | ------------------------------------------------ |
| NFR-6.7.1 | Database Scalability |  COMPLETE | Database can handle increased data volumes       |
| NFR-6.7.2 | API Scalability      |  COMPLETE | API endpoints can handle increased request loads |
| NFR-6.7.3 | Frontend Scalability |  COMPLETE | Frontend can handle increased user interactions  |
| NFR-6.7.4 | Caching Strategy     |  PARTIAL  | Efficient caching for improved performance       |

### Scalability Implementations

#### Database Scalability

- **Quality Filtering**: Reduced data volume from 490K to 8,049 ingredients
- **Materialized Views**: Pre-computed views for common queries
- **Performance Indexes**: Optimized indexes for search and filtering
- **Batch Processing**: Efficient bulk operations for data import
- **Connection Pooling**: Optimized database connection management

#### API Scalability

- **Async Operations**: Non-blocking API operations
- **Pagination**: Efficient data pagination for large datasets
- **Rate Limiting**: Protection against API abuse
- **Caching Headers**: HTTP caching for static resources
- **Background Processing**: Asynchronous processing for heavy operations

#### Frontend Scalability

- **Lazy Loading**: On-demand module loading
- **Code Splitting**: Efficient bundle splitting
- **Virtual Scrolling**: Efficient rendering of large lists
- **Progressive Loading**: Content loaded progressively
- **Service Workers**: Offline capability and caching

## Interoperability Requirements

### Objective

To ensure the system can integrate with external systems and data sources.

### Requirements

| ID        | Requirement           | Status      | Details                                  |
| --------- | --------------------- | ----------- | ---------------------------------------- |
| NFR-6.8.1 | API Standards         |  COMPLETE | RESTful API following industry standards |
| NFR-6.8.2 | Data Import/Export    |  COMPLETE | Standard formats for data exchange       |
| NFR-6.8.3 | External Integrations |  COMPLETE | Integration with USDA FoodData Central   |
| NFR-6.8.4 | Third-Party Services  |  PARTIAL  | Integration with external services       |

### Interoperability Implementations

#### API Standards

- **RESTful Design**: Standard HTTP methods and status codes
- **JSON Format**: Standard JSON for data exchange
- **OpenAPI Documentation**: Comprehensive API documentation
- **Versioning Support**: API versioning when needed
- **CORS Configuration**: Cross-origin resource sharing support

#### Data Integration

- **USDA FDC Integration**: Complete integration with FoodData Central
- **Data Import System**: Comprehensive data import with quality filtering
- **Export Functionality**: GDPR-compliant data export
- **Standard Formats**: CSV, JSON export capabilities
- **Quality Filtering**: Intelligent data filtering and validation

## Implementation Status Summary

### Backend Status: COMPLETE 

- **Performance**: Optimized database queries and API responses
- **Security**: Comprehensive authentication and authorization
- **Privacy**: Full GDPR compliance implementation
- **Maintainability**: Domain-driven architecture with clear separation
- **Reliability**: Comprehensive error handling and logging
- **Scalability**: Optimized for growing data volumes
- **Interoperability**: RESTful APIs and external integrations

### Frontend Status: COMPLETE 

- **Performance**: AOT compilation and lazy loading
- **Security**: Secure authentication and input validation
- **Privacy**: Privacy dashboard and data controls
- **Maintainability**: Modular component architecture
- **Usability**: Material 3 theming and responsive design
- **Reliability**: Comprehensive error handling and user feedback
- **Scalability**: Efficient rendering and progressive loading

### Next Priorities

1. **Complete Backup & Recovery**: Implement comprehensive backup procedures
2. **Enhance Caching Strategy**: Implement advanced caching for performance
3. **Third-Party Integrations**: Expand external service integrations
4. **Advanced Monitoring**: Implement comprehensive system monitoring
5. **Performance Optimization**: Continuous performance improvements
6. **Security Audits**: Regular security assessments and updates
