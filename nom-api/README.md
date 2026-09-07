# NOM API - Backend Service

The backend API service for the Nutrition Optimization Machine (NOM), built with .NET 10 and Entity Framework Core.

[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16+-blue.svg)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-blue.svg)](Dockerfile)
[![Health Checks](https://img.shields.io/badge/Health-Monitored-green.svg)](/health)

## **Architecture**

### **Project Structure**

```
nom-api/
├──  Nom.Api/              #  API controllers & middleware
│   ├── Controllers/         # RESTful API endpoints
│   ├── Middleware/          # Security, logging, rate limiting
│   ├── Core/               # Base classes & abstractions
│   └── Program.cs          # Application startup
├──  Nom.Data/             #  Entity Framework & database
│   ├── Entities/           # Database entities (TPH pattern)
│   ├── Migrations/         # EF Core migrations
│   └── ApplicationDbContext.cs
├──  Nom.Orch/             #  Business logic & orchestration
│   ├── Services/           # Business logic services
│   ├── Models/             # Request/response models
│   └── Interfaces/         # Service contracts
├──  Nom.Import/           #  Data import & seeding
│   ├── Services/           # Import services
│   └── DataImportScripts/  # SQL seeding scripts
└──  Nom.Api.Tests/        #  Unit & integration tests
    ├── Services/           # Service tests
    └── Integration/        # Integration tests
```

## **Quick Start**

### **Development Setup**

```bash
# Clone and navigate
cd nom-api

# Restore dependencies
dotnet restore

# Update database
dotnet ef database update

# Run development server
dotnet run --project Nom.Api
```

### **Docker Development**

```bash
# Build and run with Docker
docker build -t nom-api .
docker run -p 8080:8080 nom-api

# Or use docker-compose (from project root)
docker-compose up nom-api
```

## **Configuration**

### **Environment Variables**

| Variable                           | Description                          | Default     | Required |
| ---------------------------------- | ------------------------------------ | ----------- | -------- |
| `ASPNETCORE_ENVIRONMENT`           | Environment (Development/Production) | Development | No       |
| `ConnectionStrings__NomConnection` | PostgreSQL connection string         | -           | Yes      |
| `JWT__SecretKey`                   | JWT signing key                      | -           | Yes      |
| `JWT__Issuer`                      | JWT token issuer                     | NOMApi      | No       |
| `JWT__Audience`                    | JWT token audience                   | NOMAngular  | No       |
| `AllowedOrigins`                   | CORS allowed origins                 | -           | Yes      |

### **Database Configuration**

```json
{
  "ConnectionStrings": {
    "NomConnection": "Host=localhost;Database=nom;Username=nom;Password=your_password"
  }
}
```

## **Architecture Patterns**

### **Domain-Driven Design**

The API follows DDD principles with clear separation of concerns:

- **Controllers** - HTTP endpoint handling
- **Orchestration Services** - Business logic coordination
- **Data Layer** - Entity Framework Core with PostgreSQL
- **Core Abstractions** - Base classes and interfaces

### **Table-Per-Hierarchy (TPH)**

Entities use TPH pattern for efficient inheritance:

```csharp
// Base entity
public abstract class MeasurementEntity : BaseEntity

// Concrete implementations
public class BaseMeasurementEntity : MeasurementEntity
public class IngredientMeasurementEntity : MeasurementEntity
public class NutrientMeasurementEntity : MeasurementEntity
```

### **Middleware Pipeline**

Security-first middleware pipeline:

```csharp
app.UseSecurityHeaders();           // Security headers (CSP, HSTS)
app.UseMiddleware<AuditLoggingMiddleware>();     // Request/response logging
app.UseMiddleware<RateLimitingMiddleware>();     // Rate limiting
app.UseMiddleware<FileUploadSecurityMiddleware>(); // File upload security
app.UseContainerSecurity();         // Container security
app.UseAuthentication();            // JWT authentication
app.UseAuthorization();             // Claims-based authorization
```

## **Security Features**

### **Authentication & Authorization**

- **JWT Bearer Tokens** - Secure token-based authentication
- **Claims-Based Authorization** - Role and permission-based access
- **Token Expiration** - 24-hour token lifecycle
- **Dual Bearer Support** - ASP.NET Identity + JWT

### **Security Middleware**

- **Security Headers** - CSP, HSTS, XSS protection, frame options
- **Rate Limiting** - Sophisticated request throttling with burst protection
- **Audit Logging** - Complete request/response logging for compliance
- **Input Validation** - Comprehensive request validation
- **Container Security** - Container-specific security hardening

### **Security Headers**

```http
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
X-XSS-Protection: 1; mode=block
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: default-src 'self'; ...
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

## **API Endpoints**

### **Health & Monitoring**

| Endpoint           | Method | Description           | Auth Required |
| ------------------ | ------ | --------------------- | ------------- |
| `/health`          | GET    | Complete health check | No            |
| `/health/ready`    | GET    | Readiness probe       | No            |
| `/health/live`     | GET    | Liveness probe        | No            |
| `/health/detailed` | GET    | Detailed health info  | No            |

### **Core Features**

| Domain          | Endpoints            | Description                       |
| --------------- | -------------------- | --------------------------------- |
| **Recipes**     | `/api/recipes/*`     | Recipe CRUD, search, suggestions  |
| **Ingredients** | `/api/ingredients/*` | Ingredient management & nutrition |
| **Meal Plans**  | `/api/meal-plan/*`   | Meal planning & scheduling        |
| **Shopping**    | `/api/shopping/*`    | Shopping list management          |
| **Users**       | `/api/user/*`        | User management & profiles        |
| **Privacy**     | `/api/privacy/*`     | GDPR compliance endpoints         |
| **Curation**    | `/api/curation/*`    | Content moderation                |

### **Advanced Features**

| Feature             | Endpoints                   | Description                       |
| ------------------- | --------------------------- | --------------------------------- |
| **AI Suggestions**  | `/api/recipe-suggestions/*` | AI-powered recipe recommendations |
| **Web Scraping**    | `/api/recipe-scraping/*`    | Import recipes from web           |
| **Bulk Operations** | `/api/recipe-bulk/*`        | Batch recipe operations           |
| **Households**      | `/api/household/*`          | Multi-user household management   |
| **Messaging**       | `/api/messaging/*`          | In-app messaging system           |

## **Testing**

### **Test Categories**

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### **Test Infrastructure**

- **xUnit** - Modern .NET testing framework
- **FluentAssertions** - Readable test assertions
- **Moq** - Mocking framework
- **AutoFixture** - Test data generation
- **In-Memory Database** - Fast test execution
- **WebApplicationFactory** - Integration testing

### **Integration Tests**

```bash
# Run integration test suite
./run-integration-tests.sh

# Test specific domains
dotnet test --filter "FullyQualifiedName~RecipeManagementIntegrationTests"
dotnet test --filter "FullyQualifiedName~HouseholdManagementIntegrationTests"
```

## **Health Monitoring**

### **Health Check Endpoints**

The API provides comprehensive health monitoring:

```bash
# Complete health status
curl http://localhost:8080/health

# Readiness check (for load balancers)
curl http://localhost:8080/health/ready

# Liveness check (for container orchestrators)
curl http://localhost:8080/health/live
```

### **Health Check Components**

- **Database Connectivity** - PostgreSQL connection health
- **Redis Cache** - Cache service availability (if configured)
- **Application Health** - Basic application responsiveness
- **External Services** - Third-party service dependencies

## **Performance Features**

### **Database Optimization**

- **Compiled Queries** - Pre-compiled EF queries for performance
- **Efficient Loading** - AsNoTracking for read-only operations
- **Proper Indexing** - Optimized database indexes
- **Connection Pooling** - Efficient database connection management

### **Caching Strategy**

- **Memory Caching** - In-memory caching for reference data
- **Redis Caching** - Distributed caching for sessions
- **Cache Invalidation** - Smart cache invalidation strategies
- **Performance Monitoring** - Cache hit/miss metrics

## **Docker Deployment**

### **Multi-Stage Dockerfile**

The API uses an optimized multi-stage build:

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# ... build steps

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# ... runtime configuration
```

### **Production Features**

- **Non-Root User** - Security-hardened container
- **Health Checks** - Built-in Docker health checks
- **Minimal Base Image** - Optimized for production
- **Multi-Architecture** - Supports AMD64 and ARM64

## **Development Tools**

### **Database Management**

```bash
# Create new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# Reset database (development only)
./refresh_db_and_migration.sh
```

### **Code Generation**

```bash
# Generate controller
dotnet new webapi -n NewController

# Generate service
dotnet new classlib -n NewService
```

## **Documentation**

### **API Documentation**

- **[OpenAPI/Swagger](http://localhost:8080/swagger)** - Interactive API documentation
- **[API Reference](../docs/API_REFERENCE.md)** - Complete endpoint documentation
- **[Architecture Guide](../docs/architecture/csharp-entity-framework-patterns.md)** - Backend patterns

### **Specialized Documentation**

- **[Measurement System](README_MEASUREMENT_SYSTEM.md)** - Measurement unit management
- **[Data Import](Nom.Import/README_ENHANCED_IMPORT.md)** - Data import utilities
- **[Security Inventory](SECURITY_INVENTORY.md)** - Security implementation details

## **Contributing**

### **Development Standards**

1. **Follow Architecture Patterns** - Use established DDD patterns
2. **Maintain Security** - All endpoints must be properly secured
3. **Test Thoroughly** - Include unit and integration tests
4. **Document Changes** - Update OpenAPI documentation
5. **Performance First** - Consider performance implications

### **Code Quality**

- **File Separation** - One class per file (strictly enforced)
- **Naming Conventions** - Follow established patterns
- **Error Handling** - Consistent error responses
- **Logging** - Structured logging throughout
- **Validation** - Input validation on all endpoints

## 🆘 **Troubleshooting**

### **Common Issues**

1. **Database Connection** - Check PostgreSQL service and connection string
2. **JWT Issues** - Verify JWT secret key configuration
3. **CORS Errors** - Check AllowedOrigins configuration
4. **Health Check Failures** - Review health check endpoint responses

### **Development Support**

- **Documentation**: [../docs/README.md](../docs/README.md)
- **Troubleshooting**: [../docs/development/troubleshooting.md](../docs/development/troubleshooting.md)
- **Testing Guide**: [../nom-test/README.md](../nom-test/README.md)

---

**The NOM API is production-ready and secure!** 
