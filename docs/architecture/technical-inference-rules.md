# Technical Inference Rules and Specifications

## Overview

This document captures comprehensive technical inference rules and specifications derived from reverse engineering the NOM (Nutrition Optimization Machine) codebase. These rules provide guidance for maintaining consistency, implementing new features, and ensuring architectural integrity.

## Table of Contents

1. [Architecture Patterns](#architecture-patterns)
2. [Naming Conventions](#naming-conventions)
3. [Code Organization Rules](#code-organization-rules)
4. [Database Patterns](#database-patterns)
5. [API Design Rules](#api-design-rules)
6. [Security Patterns](#security-patterns)
7. [Performance Patterns](#performance-patterns)
8. [Testing Patterns](#testing-patterns)
9. [Frontend-Backend Integration](#frontend-backend-integration)
10. [Error Handling Patterns](#error-handling-patterns)
11. [Caching Strategies](#caching-strategies)
12. [Migration Patterns](#migration-patterns)

## Architecture Patterns

### 1. Layered Architecture

**Pattern:** Clear separation between API, Business Logic, and Data layers

```
Nom.Api (Controllers) → Nom.Orch (Services) → Nom.Data (Entities)
```

**Inference Rules:**

- Controllers should only handle HTTP concerns (routing, validation, responses)
- Business logic belongs in Orchestration Services
- Data access is handled through Entity Framework DbContext
- No direct database access from controllers

### 2. Service Architecture

**Pattern:** Orchestration Services with Dependency Injection

```csharp
// ✅ CORRECT: Service with proper dependencies
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
    private readonly ILogger<PersonOrchestrationService> _logger;
}
```

**Inference Rules:**

- All services must implement an interface
- Services are registered as Scoped by default
- Singleton services only for stateful operations (e.g., IKaggleRecipeIngestionService)
- Use constructor injection for dependencies

### 3. Repository Pattern

**Pattern:** Direct DbContext usage (no separate repository layer)

```csharp
// ✅ CORRECT: Direct DbContext usage
public async Task<PersonEntity?> GetPersonAsync(long id)
{
    return await _dbContext.Persons
        .Include(p => p.Attributes)
        .Include(p => p.Restrictions)
        .FirstOrDefaultAsync(p => p.Id == id);
}
```

**Inference Rules:**

- Use DbContext directly in orchestration services
- No abstract repository interfaces
- Leverage EF Core's powerful query capabilities
- Use compiled queries for frequently executed operations

## Naming Conventions

### 1. C# Naming Rules

**Service Naming:**

- Interface: `I[ServiceName]Service` (e.g., `IPersonOrchestrationService`)
- Implementation: `[ServiceName]Service` (e.g., `PersonOrchestrationService`)

**Entity Naming:**

- Entity: `[EntityName]Entity` (e.g., `PersonEntity`)
- Model: `[EntityName]Model` (e.g., `PersonModel`)
- Request: `[Action][EntityName]Request` (e.g., `UpdatePersonRequest`)
- Response: `[Action][EntityName]Response` (e.g., `PersonCreateResponseModel`)

**Controller Naming:**

- Controller: `[EntityName]Controller` (e.g., `PersonController`)

**Inference Rules:**

- NEVER use DTO suffixes (DTO, Dto, dto)
- Use Model, Request, Response suffixes instead
- All names use PascalCase
- Be descriptive and specific

### 2. Database Naming Rules

**Table Naming:**

- Tables: `PascalCase` matching entity names (e.g., `Person`, `Recipe`)
- Junction tables: `Entity1Entity2` (e.g., `PersonPlan`)

**Column Naming:**

- Columns: `PascalCase` matching property names
- Foreign keys: `ReferencedEntityId` (e.g., `PersonId`)
- Primary keys: `Id` (simple and consistent)

**Schema Organization:**

- `auth`: Identity and authentication tables
- `person`: User profiles and attributes
- `plan`: Nutritional plans and goals
- `privacy`: GDPR compliance tables
- `recipe`: Recipe and ingredient management
- `curation`: Content curation tables
- `communication`: User messaging tables
- `reference`: Lookup data tables
- `audit`: System audit trail tables

### 3. TypeScript Naming Rules

**Model Naming:**

- Interface: `[EntityName]Model` (e.g., `PersonModel`)
- Request: `[Action][EntityName]RequestModel` (e.g., `UpdatePersonRequestModel`)
- Response: `[Action][EntityName]ResponseModel` (e.g., `PersonCreateResponseModel`)

**Service Naming:**

- Service: `[EntityName]Service` (e.g., `PersonService`)

**Component Naming:**

- Component: `[FeatureName]Component` (e.g., `PersonEditComponent`)
- File: `[feature-name].component.ts` (e.g., `person-edit.component.ts`)

## Code Organization Rules

### 1. Backend Organization

**Project Structure:**

```
Nom.Api/
├── Controllers/           # API endpoints
├── Authentication/        # Auth configuration
├── Middleware/           # Custom middleware
└── Program.cs           # Application entry point

Nom.Orch/
├── Services/             # Business logic services
├── Interfaces/           # Service contracts
├── Models/              # Request/Response models
├── UtilityServices/     # Utility services
├── UtilityInterfaces/   # Utility interfaces
└── Enums/              # Enumerations

Nom.Data/
├── Person/              # Domain entities
├── Plan/
├── Privacy/
├── Recipe/
├── Curation/
├── Communication/
├── Reference/
├── Audit/
├── Nutrient/
├── Shopping/
└── ApplicationDbContext.cs
```

**Inference Rules:**

- Each domain has its own directory
- Models are organized by domain in Nom.Orch
- Entities are organized by domain in Nom.Data
- Controllers are organized by domain in Nom.Api

### 2. Frontend Organization

**Project Structure:**

```
src/app/
├── common/              # Shared utilities and components
├── shared/              # Shared models
├── auth/                # Authentication components
├── person/              # Domain-specific modules
├── recipe/              # Recipe domain
├── curation/            # Curation domain
├── communication/       # Communication domain
├── admin/               # Admin functionality
├── user/                # User management
├── privacy/             # Privacy domain
├── plan/                # Plan domain
├── restriction/         # Restriction domain
├── onboarding/          # Onboarding domain
├── nutrient/            # Nutrient domain
└── guards/              # Route guards
```

**Inference Rules:**

- Each domain has its own directory
- Common components go in `common/`
- Shared models go in `shared/`
- Each domain follows consistent structure

## Database Patterns

### 1. Entity Framework Patterns

**Base Entity Pattern:**

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

**Entity Pattern:**

```csharp
[Table("Person", Schema = "person")]
public class PersonEntity : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public virtual ICollection<PlanParticipantEntity> PlanParticipations { get; set; } = new List<PlanParticipantEntity>();
}
```

**Inference Rules:**

- All entities must inherit from `BaseEntity`
- All entities must have explicit table and schema attributes
- Navigation properties must be virtual for lazy loading
- Use `ICollection<T>` for navigation properties
- Initialize collections in property declaration

### 2. DbContext Organization

**Pattern:** Organized by domain with regions

```csharp
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    #region Person
    public DbSet<PersonEntity> Persons { get; set; } = default!;
    public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
    #endregion

    #region Privacy
    public DbSet<UserConsentEntity> UserConsents { get; set; } = default!;
    public DbSet<DataProcessingLogEntity> DataProcessingLogs { get; set; } = default!;
    #endregion
}
```

**Inference Rules:**

- Use regions to organize DbSets by domain
- All DbSets must be marked as `default!`
- Use explicit schema organization
- Configure relationships in `OnModelCreating`

### 3. Migration Patterns

**Custom Migration Pattern:**

```csharp
public static class CustomMigration
{
    private const long SystemPersonId = 1L;
    private const long MeasurementTypeGramId = 4003L;

    public static void ApplyCustomUpOperations(this MigrationBuilder migrationBuilder)
    {
        SeedInitialSystemPerson(migrationBuilder);
        AddReferenceGroups(migrationBuilder);
        AddMeasurementTypes(migrationBuilder);
    }
}
```

**Inference Rules:**

- Use constants for seeded data IDs
- Separate up and down operations
- Use explicit ID ranges for different data types
- Follow the seeding pattern for all reference data

## API Design Rules

### 1. Controller Patterns

**Controller Structure:**

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
}
```

**Inference Rules:**

- All controllers must have `[Authorize]` attribute
- All controllers must inherit from `BaseApiController`
- Use dependency injection for services
- Include proper logging

### 2. HTTP Status Code Usage

**Pattern:** Consistent HTTP status code usage

```csharp
// Success responses
return Ok(response);                    // 200 - Successful GET/PUT/PATCH
return CreatedAtAction(...);           // 201 - Successful POST
return Accepted(response);              // 202 - Async operation accepted

// Error responses
return BadRequest(ModelState);          // 400 - Bad Request
return Unauthorized();                 // 401 - Unauthorized
return Forbid();                       // 403 - Forbidden
return NotFound();                     // 404 - Not Found
return StatusCode(500, error);         // 500 - Internal Server Error
```

**Inference Rules:**

- Use appropriate status codes for responses
- Include proper response type attributes
- Validate ModelState before processing
- Handle exceptions consistently

### 3. Response Patterns

**Success Response:**

```csharp
return Ok(new { Message = "Operation successful", Data = result });
```

**Error Response:**

```csharp
return StatusCode(StatusCodes.Status500InternalServerError,
    new { Message = "An internal error occurred." });
```

**Async Response:**

```csharp
return Accepted(new PrivacyRequestStatusResponse
{
    RequestId = requestId,
    Status = "Queued"
});
```

## Security Patterns

### 1. Authentication Configuration

**Pattern:** Dual Bearer token support

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
    options.DefaultScheme = IdentityConstants.BearerScheme;
}).AddBearerToken(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromHours(24);
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = false,
        ClockSkew = TimeSpan.Zero
    };
});
```

**Inference Rules:**

- Use dual Bearer token support
- Set appropriate token expiration
- Configure proper validation parameters
- Use HTTPS in production

### 2. Authorization Policies

**Pattern:** Claims-based authorization

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanManageCuration", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManageCuration", "true"));

    options.AddPolicy("CanManageUserRoles", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManageUserRoles", "true"));
});
```

**Inference Rules:**

- Use claims-based authorization for fine-grained access control
- Define policies for different permission levels
- Use proper authentication schemes
- Validate all user input

### 3. Security Middleware

**Pattern:** Comprehensive security middleware stack

```csharp
// Add security middleware in order
app.UseMiddleware<AuditLoggingMiddleware>();
app.UseMiddleware<InputValidationMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<FileUploadSecurityMiddleware>();
app.UseContainerSecurity(); // Container security middleware

app.UseAuthentication();
app.UseAuthorization();
```

**Inference Rules:**

- Include comprehensive security middleware
- Order middleware correctly
- Implement rate limiting
- Validate file uploads

## Performance Patterns

### 1. Query Optimization

**Compiled Queries:**

```csharp
private static readonly Func<ApplicationDbContext, long, Task<PersonEntity?>>
    GetPersonByIdQuery = EF.CompileAsyncQuery(
        (ApplicationDbContext context, long id) =>
            context.Persons
                .Include(p => p.Attributes)
                .Include(p => p.Restrictions)
                .FirstOrDefault(p => p.Id == id));
```

**Efficient Loading:**

```csharp
var personModels = await _dbContext.Persons
    .Where(p => p.PlanParticipations.Any(pp => pp.PlanId == planId))
    .Select(p => new PersonModel
    {
        Id = p.Id,
        Name = p.Name,
        UserId = p.UserId,
        CreatedDate = p.CreatedDate
    })
    .ToListAsync();
```

**Inference Rules:**

- Use compiled queries for frequently executed operations
- Use `AsNoTracking()` for read-only operations
- Use projections (`Select`) to limit data transfer
- Use `Include()` for related data, not lazy loading

### 2. Caching Strategies

**Reference Data Caching:**

```csharp
public async Task<List<ReferenceModel>> GetReferenceDataAsync()
{
    if (_cache.TryGetValue(REFERENCE_CACHE_KEY, out List<ReferenceModel>? cached))
    {
        return cached!;
    }

    var data = await _dbContext.References.ToListAsync();
    var models = data.Select(r => new ReferenceModel(r)).ToList();

    var cacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
        .SetSlidingExpiration(TimeSpan.FromMinutes(10));

    _cache.Set(REFERENCE_CACHE_KEY, models, cacheOptions);
    return models;
}
```

**Inference Rules:**

- Cache reference data for 30 minutes
- Cache session data for 24 hours
- Cache rate limiting data for 1 minute
- Always use `MemoryCacheEntryOptions` for cache configuration

### 3. Memory Management

**Pattern:** Efficient memory usage with proper disposal

```csharp
public async Task<List<PersonModel>> GetLargePersonListAsync()
{
    var personModels = new List<PersonModel>();

    await foreach (var person in _dbContext.Persons
        .AsNoTracking()
        .Select(p => new PersonModel
        {
            Id = p.Id,
            Name = p.Name,
            UserId = p.UserId
        })
        .AsAsyncEnumerable())
    {
        personModels.Add(person);

        // Process in batches to avoid memory issues
        if (personModels.Count % 1000 == 0)
        {
            // Process batch
        }
    }

    return personModels;
}
```

**Inference Rules:**

- Use streaming for large datasets
- Process data in batches
- Use proper disposal patterns
- Monitor memory usage

## Testing Patterns

### 1. Unit Testing

**Pattern:** In-memory database for testing

```csharp
[Test]
public async Task UpsertPersonAsync_WithValidRequest_ReturnsSuccessResponse()
{
    // Arrange
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    using var context = new ApplicationDbContext(options);
    var httpContextAccessor = new HttpContextAccessor();
    var logger = new Mock<ILogger<PersonOrchestrationService>>();
    var privacyService = new Mock<IPrivacyOrchestrationService>();

    var service = new PersonOrchestrationService(
        context, httpContextAccessor, privacyService.Object, logger.Object);

    var request = new PersonCreateModel { PersonName = "Test User" };

    // Act
    var result = await service.UpsertPersonAsync(request);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("Test User", result.Name);
}
```

**Inference Rules:**

- Use in-memory database for unit tests
- Mock external dependencies
- Test both success and failure scenarios
- Use descriptive test names

### 2. Integration Testing

**Pattern:** Test database for integration tests

```csharp
[Test]
public async Task PersonController_UpsertPerson_ReturnsCreatedResponse()
{
    // Arrange
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    using var context = new ApplicationDbContext(options);
    var service = new PersonOrchestrationService(context, ...);
    var controller = new PersonController(service, ...);

    var request = new PersonCreateModel { PersonName = "Test User" };

    // Act
    var result = await controller.UpsertPerson(request);

    // Assert
    Assert.IsInstanceOf<CreatedAtActionResult>(result);
}
```

**Inference Rules:**

- Test complete request-response cycles
- Use test databases for integration tests
- Verify proper HTTP status codes
- Test authentication and authorization

## Frontend-Backend Integration

### 1. Model Consistency

**Pattern:** Consistent model structure between frontend and backend

```typescript
// Frontend TypeScript
export interface PersonModel {
  id: number;
  name: string;
  userId?: string;
  createdDate: Date;
  createdByPersonId?: number;
  lastModifiedDate?: Date;
  lastModifiedByPersonId?: number;
}
```

```csharp
// Backend C#
public class PersonModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime CreatedDate { get; set; }
    public long? CreatedByPersonId { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public long? LastModifiedByPersonId { get; set; }
}
```

**Inference Rules:**

- Maintain consistent property names
- Use appropriate data types
- Include all necessary properties
- Handle nullable properties correctly

### 2. API Communication

**Pattern:** Consistent API service structure

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

**Inference Rules:**

- Use consistent API URL patterns
- Include proper error handling
- Use TypeScript interfaces for type safety
- Follow RESTful conventions

## Error Handling Patterns

### 1. Service Error Handling

**Pattern:** Consistent error handling with logging

```csharp
public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
{
    try
    {
        _logger.LogInformation("Upserting person with name {Name}", request.PersonName);

        // Business logic here

        _logger.LogInformation("Successfully created person {PersonId}", newPerson.Id);
        return new PersonCreateResponseModel { Id = newPerson.Id, Name = newPerson.Name };
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Database error while upserting person {Name}", request.PersonName);
        throw new InvalidOperationException("Failed to save person data", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error while upserting person {Name}", request.PersonName);
        throw;
    }
}
```

**Inference Rules:**

- All orchestration services must have try-catch blocks
- Database exceptions must be logged and re-thrown as `InvalidOperationException`
- Use structured logging with parameters
- Include correlation IDs for request tracing

### 2. Controller Error Handling

**Pattern:** Consistent API error responses

```csharp
[HttpPost]
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
    catch (InvalidOperationException ex)
    {
        _logger.LogWarning(ex, "Invalid operation in UpsertPerson");
        return BadRequest(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "An error occurred in UpsertPerson.");
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An internal error occurred." });
    }
}
```

**Inference Rules:**

- All controllers must validate `ModelState` before processing
- All API responses must include proper HTTP status codes
- Handle different exception types appropriately
- Provide meaningful error messages

## Migration Patterns

### 1. Database Migration

**Pattern:** Custom migration with seeding

```csharp
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
```

**Inference Rules:**

- Use constants for seeded data IDs
- Separate up and down operations
- Use explicit ID ranges for different data types
- Follow the seeding pattern for all reference data

### 2. Code Migration

**Pattern:** Gradual migration to new patterns

```csharp
// Migration checklist
// [ ] Extract inline templates to separate .html files
// [ ] Replace *ngIf with @if
// [ ] Replace *ngFor with @for
// [ ] Replace *ngSwitch with @switch
// [ ] Add track expressions to all @for loops
// [ ] Update component decorators to use templateUrl
// [ ] Test all conditional rendering and iteration
```

**Inference Rules:**

- Migrate one component type at a time
- Start with the most commonly used components
- Preserve existing functionality while adding new features
- Test thoroughly after each migration

## Comprehensive Inference Rules

### 1. Service Registration Rules

**Rule:** All orchestration services must be registered as Scoped
**Exception:** Services managing static state (e.g., IKaggleRecipeIngestionService) should be Singleton

**Rule:** Service interfaces must follow the pattern `I[ServiceName]Service`
**Rule:** Service implementations must follow the pattern `[ServiceName]Service`

### 2. Entity Framework Rules

**Rule:** All entities must inherit from `BaseEntity`
**Rule:** All entities must have explicit table and schema attributes
**Rule:** Navigation properties must be virtual for lazy loading
**Rule:** Use `Include()` for related data, not lazy loading in production

### 3. Caching Rules

**Rule:** Reference data should be cached for 30 minutes
**Rule:** Session data should be cached for 24 hours
**Rule:** Rate limiting data should be cached for 1 minute
**Rule:** Always use `MemoryCacheEntryOptions` for cache configuration

### 4. Error Handling Rules

**Rule:** All orchestration services must have try-catch blocks
**Rule:** Database exceptions must be logged and re-thrown as `InvalidOperationException`
**Rule:** All controllers must validate `ModelState` before processing
**Rule:** All API responses must include proper HTTP status codes

### 5. Performance Rules

**Rule:** Use compiled queries for frequently executed operations
**Rule:** Use `AsNoTracking()` for read-only operations
**Rule:** Use projections (`Select`) to limit data transfer
**Rule:** Use batch operations for bulk data operations

### 6. Security Rules

**Rule:** All controllers must have `[Authorize]` attribute
**Rule:** Use claims-based authorization for fine-grained access control
**Rule:** All user input must be validated
**Rule:** Use HTTPS in production

### 7. Logging Rules

**Rule:** All service methods must log at Information level for successful operations
**Rule:** All exceptions must be logged at Error level
**Rule:** Use structured logging with parameters
**Rule:** Include correlation IDs for request tracing

### 8. Database Rules

**Rule:** Use explicit transactions for multi-table operations
**Rule:** Use proper foreign key relationships
**Rule:** Use appropriate indexes for frequently queried columns
**Rule:** Use schema organization for logical separation

### 9. API Design Rules

**Rule:** Use RESTful conventions for endpoint design
**Rule:** Include proper response type attributes
**Rule:** Use consistent error response format
**Rule:** Include API documentation with Swagger

### 10. Testing Rules

**Rule:** All business logic must have unit tests
**Rule:** Use in-memory database for unit tests
**Rule:** Mock external dependencies
**Rule:** Test both success and failure scenarios

### 11. Frontend Rules

**Rule:** Use base components for consistency
**Rule:** Follow Material 3 theming guidelines
**Rule:** Use modern Angular control flow syntax
**Rule:** Implement proper loading states and error handling

### 12. Code Quality Rules

**Rule:** Follow naming conventions strictly
**Rule:** Use explicit property assignment in constructors
**Rule:** Include proper XML documentation
**Rule:** Maintain consistent code formatting

---

_This document should be updated as the project evolves and new patterns emerge._
