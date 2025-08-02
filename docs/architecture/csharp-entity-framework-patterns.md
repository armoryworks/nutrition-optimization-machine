# C#/Entity Framework Architecture Patterns

## Overview

This document provides comprehensive technical specifications for the C#/Entity Framework patterns used in the NOM (Nutrition Optimization Machine) project. It covers architecture patterns, best practices, and implementation guidelines for the backend services.

## Table of Contents

1. [Repository Pattern Implementation](#repository-pattern-implementation)
2. [Unit of Work Pattern](#unit-of-work-pattern)
3. [Dependency Injection Patterns](#dependency-injection-patterns)
4. [Caching Strategies](#caching-strategies)
5. [Query Optimization Patterns](#query-optimization-patterns)
6. [Error Handling Patterns](#error-handling-patterns)
7. [Service Architecture Patterns](#service-architecture-patterns)
8. [Database Schema Patterns](#database-schema-patterns)
9. [API Controller Patterns](#api-controller-patterns)
10. [Security Patterns](#security-patterns)
11. [Performance Patterns](#performance-patterns)
12. [Testing Patterns](#testing-patterns)

## Repository Pattern Implementation

### Current Approach: Direct DbContext Usage

NOM uses Entity Framework Core's DbContext directly in orchestration services rather than implementing a separate repository pattern. This approach provides:

**Advantages:**

- Simpler architecture with fewer abstractions
- Direct access to EF Core's powerful query capabilities
- Better performance with compiled queries
- Easier unit testing with in-memory providers
- Reduced complexity and maintenance overhead

**Pattern:**

```csharp
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PersonOrchestrationService> _logger;

    public PersonOrchestrationService(
        ApplicationDbContext dbContext,
        ILogger<PersonOrchestrationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PersonEntity?> GetPersonAsync(long id)
    {
        return await _dbContext.Persons
            .Include(p => p.Attributes)
            .Include(p => p.Restrictions)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<PersonEntity>> GetPersonsByPlanIdAsync(long planId)
    {
        return await _dbContext.PlanParticipants
            .Include(pp => pp.Person)
            .Include(pp => pp.Plan)
            .Where(pp => pp.PlanId == planId)
            .Select(pp => pp.Person)
            .ToListAsync();
    }
}
```

### Query Patterns

**Include Pattern:**

```csharp
// ✅ CORRECT: Use Include for related data
var person = await _dbContext.Persons
    .Include(p => p.Attributes)
    .Include(p => p.Restrictions)
    .Include(p => p.PlanParticipations)
        .ThenInclude(pp => pp.Plan)
    .FirstOrDefaultAsync(p => p.Id == personId);
```

**Projection Pattern:**

```csharp
// ✅ CORRECT: Use Select for efficient projections
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

**Filtering Pattern:**

```csharp
// ✅ CORRECT: Use Where for efficient filtering
var activePersons = await _dbContext.Persons
    .Where(p => p.UserId != null && p.CreatedDate >= DateTime.UtcNow.AddDays(-30))
    .ToListAsync();
```

## Unit of Work Pattern

### DbContext as Unit of Work

NOM uses Entity Framework Core's DbContext as the Unit of Work pattern:

**Benefits:**

- Automatic transaction management
- Change tracking across entities
- Single responsibility for data persistence
- Consistent with EF Core patterns

**Usage Pattern:**

```csharp
public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
{
    using var transaction = await _dbContext.Database.BeginTransactionAsync();
    try
    {
        // Multiple operations within single transaction
        var person = await CreateOrUpdatePersonAsync(request);
        await _privacyOrchestrationService.CreateDefaultConsentsAsync(person.Id);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return new PersonCreateResponseModel { PersonId = person.Id };
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

**Transaction Patterns:**

```csharp
// ✅ CORRECT: Explicit transaction management
public async Task<bool> TransferPersonAsync(long fromPersonId, long toPersonId)
{
    using var transaction = await _dbContext.Database.BeginTransactionAsync();
    try
    {
        var fromPerson = await _dbContext.Persons.FindAsync(fromPersonId);
        var toPerson = await _dbContext.Persons.FindAsync(toPersonId);

        if (fromPerson == null || toPerson == null)
            return false;

        // Transfer logic here
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

## Dependency Injection Patterns

### Service Registration

**Orchestration Services:**

```csharp
// Program.cs
builder.Services.AddOrchestrationServices(); // Automatic registration

// Manual registration if needed
builder.Services.AddScoped<IPersonOrchestrationService, PersonOrchestrationService>();
builder.Services.AddScoped<IPrivacyOrchestrationService, PrivacyOrchestrationService>();
builder.Services.AddScoped<IRecipeOrchestrationService, RecipeOrchestrationService>();
```

**DbContext Registration:**

```csharp
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NomConnection"),
        b => b.MigrationsAssembly("Nom.Data")));
```

**Service Lifetimes:**

- **DbContext**: Scoped (per HTTP request)
- **Orchestration Services**: Scoped (per HTTP request)
- **Controllers**: Transient (per request)
- **Singleton Services**: For stateful services (e.g., IKaggleRecipeIngestionService)

### Service Collection Extensions

**Automatic Registration Pattern:**

```csharp
public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
{
    var assembly = Assembly.GetAssembly(typeof(ServiceCollectionExtensions));

    var serviceRegistrations = assembly.GetExportedTypes()
        .Where(type => type.IsInterface &&
                      type.Namespace?.Contains("Interfaces") == true &&
                      type.Name.EndsWith("Service"))
        .Select(interfaceType => new
        {
            Interface = interfaceType,
            Implementation = assembly.GetExportedTypes()
                            .FirstOrDefault(implType => !implType.IsAbstract &&
                                                       !implType.IsInterface &&
                                                       implType.Name == interfaceType.Name.Substring(1))
        })
        .Where(x => x.Implementation != null);

    foreach (var registration in serviceRegistrations)
    {
        // Special handling for singleton services
        if (registration.Interface.Name == "IKaggleRecipeIngestionService")
        {
            services.AddSingleton(registration.Interface, registration.Implementation!);
        }
        else
        {
            services.AddScoped(registration.Interface, registration.Implementation!);
        }
    }

    return services;
}
```

## Caching Strategies

### Reference Data Caching

**Pattern:** Cache frequently accessed reference data

```csharp
public class ReferenceOrchestrationService : IReferenceOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ReferenceOrchestrationService> _logger;

    private const string REFERENCE_CACHE_KEY = "reference_data";
    private const int CACHE_DURATION_MINUTES = 30;

    public ReferenceOrchestrationService(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        ILogger<ReferenceOrchestrationService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<ReferenceModel>> GetReferenceDataAsync()
    {
        if (_cache.TryGetValue(REFERENCE_CACHE_KEY, out List<ReferenceModel>? cached))
        {
            _logger.LogDebug("Returning cached reference data");
            return cached!;
        }

        var data = await _dbContext.References
            .Include(r => r.Groups)
            .ToListAsync();

        var models = data.Select(r => new ReferenceModel(r)).ToList();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES))
            .SetSlidingExpiration(TimeSpan.FromMinutes(10));

        _cache.Set(REFERENCE_CACHE_KEY, models, cacheOptions);

        _logger.LogInformation("Cached {Count} reference items", models.Count);
        return models;
    }

    public void ClearReferenceCache()
    {
        _cache.Remove(REFERENCE_CACHE_KEY);
        _logger.LogInformation("Cleared reference data cache");
    }
}
```

### Session Management Caching

**Pattern:** Cache user session data

```csharp
public class SessionManagementService : ISessionManagementService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SessionManagementService> _logger;

    private const int MAX_CONCURRENT_SESSIONS = 3;
    private const int SESSION_TIMEOUT_MINUTES = 1440; // 24 hours
    private const string SESSION_CACHE_PREFIX = "session:";
    private const string USER_SESSIONS_PREFIX = "user_sessions:";

    public async Task<SessionInfo> CreateSessionAsync(string userId, string deviceInfo, string ipAddress)
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionInfo = new SessionInfo
        {
            SessionId = sessionId,
            UserId = userId,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        var cacheKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
        var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";

        // Check concurrent session limit
        var userSessions = await GetUserSessionsAsync(userId);
        if (userSessions.Count >= MAX_CONCURRENT_SESSIONS)
        {
            // Remove oldest session
            var oldestSession = userSessions.OrderBy(s => s.CreatedAt).First();
            await RemoveSessionAsync(oldestSession.SessionId);
        }

        // Add new session
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES));

        _cache.Set(cacheKey, sessionInfo, cacheOptions);

        // Update user sessions list
        userSessions.Add(sessionInfo);
        _cache.Set(userSessionsKey, userSessions, cacheOptions);

        return sessionInfo;
    }
}
```

### Rate Limiting Caching

**Pattern:** Cache rate limiting data

```csharp
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimitStore;

    private const int MaxRequestsPerMinute = 100;
    private const int MaxRequestsPerHour = 1000;
    private const int MaxRequestsPerDay = 10000;

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var cacheKey = $"rate_limit:{clientId}";

        if (_cache.TryGetValue(cacheKey, out RateLimitInfo? rateLimitInfo))
        {
            if (IsRateLimitExceeded(rateLimitInfo))
            {
                context.Response.StatusCode = 429; // Too Many Requests
                await context.Response.WriteAsync("Rate limit exceeded");
                return;
            }

            // Update rate limit info
            rateLimitInfo.RequestCount++;
            rateLimitInfo.LastRequestTime = DateTime.UtcNow;
        }
        else
        {
            rateLimitInfo = new RateLimitInfo
            {
                RequestCount = 1,
                FirstRequestTime = DateTime.UtcNow,
                LastRequestTime = DateTime.UtcNow
            };
        }

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(1));

        _cache.Set(cacheKey, rateLimitInfo, cacheOptions);

        await _next(context);
    }
}
```

## Query Optimization Patterns

### Compiled Queries

**Pattern:** Use compiled queries for frequently executed operations

```csharp
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private static readonly Func<ApplicationDbContext, long, Task<PersonEntity?>>
        GetPersonByIdQuery = EF.CompileAsyncQuery(
            (ApplicationDbContext context, long id) =>
                context.Persons
                    .Include(p => p.Attributes)
                    .Include(p => p.Restrictions)
                    .FirstOrDefault(p => p.Id == id));

    private static readonly Func<ApplicationDbContext, string, Task<PersonEntity?>>
        GetPersonByUserIdQuery = EF.CompileAsyncQuery(
            (ApplicationDbContext context, string userId) =>
                context.Persons
                    .Include(p => p.PlanParticipations)
                    .ThenInclude(pp => pp.Plan)
                    .FirstOrDefault(p => p.UserId == userId));

    public async Task<PersonEntity?> GetPersonByIdAsync(long id)
    {
        return await GetPersonByIdQuery(_dbContext, id);
    }

    public async Task<PersonEntity?> GetPersonByUserIdAsync(string userId)
    {
        return await GetPersonByUserIdQuery(_dbContext, userId);
    }
}
```

### Efficient Loading Patterns

**Pattern:** Use explicit loading for large datasets

```csharp
public async Task<List<PersonModel>> GetPersonsByPlanIdAsync(long planId)
{
    // Load only necessary data
    var participants = await _dbContext.PlanParticipants
        .Include(pp => pp.Person)
        .Where(pp => pp.PlanId == planId)
        .Select(pp => new PersonModel
        {
            Id = pp.Person.Id,
            Name = pp.Person.Name,
            UserId = pp.Person.UserId,
            CreatedDate = pp.Person.CreatedDate
        })
        .ToListAsync();

    return participants;
}
```

### Batch Operations

**Pattern:** Use batch operations for bulk data operations

```csharp
public async Task<int> BulkUpdatePersonNamesAsync(Dictionary<long, string> personUpdates)
{
    var updatedCount = 0;

    foreach (var update in personUpdates)
    {
        var person = await _dbContext.Persons.FindAsync(update.Key);
        if (person != null)
        {
            person.Name = update.Value;
            updatedCount++;
        }
    }

    await _dbContext.SaveChangesAsync();
    return updatedCount;
}
```

## Error Handling Patterns

### Orchestration Service Error Handling

**Pattern:** Consistent error handling with logging

```csharp
public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
{
    try
    {
        _logger.LogInformation("Upserting person with name {Name}", request.PersonName);

        var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("User is not authenticated.");
        }

        var existingPerson = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (existingPerson != null)
        {
            existingPerson.Name = request.PersonName;
            _dbContext.Persons.Update(existingPerson);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Successfully updated person {PersonId}", existingPerson.Id);
            return new PersonCreateResponseModel
            {
                Id = existingPerson.Id,
                Name = existingPerson.Name,
                UserId = existingPerson.UserId
            };
        }

        var newPerson = new PersonEntity
        {
            Name = request.PersonName,
            UserId = userId,
            CreatedByPersonId = 1L // System person
        };

        _dbContext.Persons.Add(newPerson);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Successfully created person {PersonId}", newPerson.Id);
        return new PersonCreateResponseModel
        {
            Id = newPerson.Id,
            Name = newPerson.Name,
            UserId = newPerson.UserId
        };
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

### Controller Error Handling

**Pattern:** Consistent API error responses

```csharp
[HttpPost]
[ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> UpsertPerson([FromBody] PersonCreateModel model)
{
    if (!ModelState.IsValid)
    {
        return BadRequest(ModelState);
    }

    try
    {
        var response = await _personOrchestrationService.UpsertPersonAsync(model);

        bool wasCreated = !HttpContext.Response.Headers.ContainsKey("Location");
        if (wasCreated)
        {
            return CreatedAtAction(nameof(GetPersonById), new { id = response.Id }, response);
        }
        else
        {
            return Ok(response);
        }
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

## Service Architecture Patterns

### Orchestration Service Pattern

**Pattern:** Business logic orchestration with dependency injection

```csharp
public class PersonOrchestrationService : IPersonOrchestrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
    private readonly ILogger<PersonOrchestrationService> _logger;

    public PersonOrchestrationService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IPrivacyOrchestrationService privacyOrchestrationService,
        ILogger<PersonOrchestrationService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _privacyOrchestrationService = privacyOrchestrationService;
        _logger = logger;
    }

    // Service methods with proper error handling and logging
}
```

### Service Interface Pattern

**Pattern:** Clear service contracts

```csharp
public interface IPersonOrchestrationService
{
    Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request);
    Task<PersonEntity> SetupNewRegisteredPersonAsync(string identityUserId, string personName);
    Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request);
    long GetCurrentPersonId();
    Task<PersonModel> GetPersonByUserIdAsync(string userId);
    Task<PersonModel> GetPersonByIdAsync(long personId);
    Task<List<PersonModel>> GetPersonsByPlanIdAsync(long planId);
    Task<PersonModel> UpdatePersonAsync(UpdatePersonRequest request);
    Task<bool> DeletePersonAsync(long personId);
}
```

## Database Schema Patterns

### Entity Pattern

**Pattern:** Consistent entity structure with audit fields

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

[Table("Person", Schema = "person")]
public class PersonEntity : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public virtual ICollection<PlanParticipantEntity> PlanParticipations { get; set; } = new List<PlanParticipantEntity>();
    public virtual ICollection<PersonAttributeEntity> Attributes { get; set; } = new List<PersonAttributeEntity>();
    public virtual ICollection<RestrictionEntity> Restrictions { get; set; } = new List<RestrictionEntity>();
}
```

### Schema Organization

**Pattern:** Organized by domain with clear separation

```csharp
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    // Organized by domain with regions
    #region Person
    public DbSet<PersonEntity> Persons { get; set; } = default!;
    public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
    #endregion

    #region Privacy
    public DbSet<UserConsentEntity> UserConsents { get; set; } = default!;
    public DbSet<DataProcessingLogEntity> DataProcessingLogs { get; set; } = default!;
    #endregion

    #region Recipe
    public DbSet<RecipeEntity> Recipes { get; set; } = default!;
    public DbSet<IngredientEntity> Ingredients { get; set; } = default!;
    public DbSet<RecipeIngredientEntity> RecipeIngredients { get; set; } = default!;
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("auth");

        #region Person Namespace Fluent API Configurations
        modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
        modelBuilder.Entity<PersonAttributeEntity>().ToTable("PersonAttribute", schema: "person");
        #endregion
    }
}
```

## API Controller Patterns

### Controller Structure

**Pattern:** Consistent controller structure with proper authorization

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

    [HttpPost]
    [ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertPerson([FromBody] PersonCreateModel model)
    {
        // Implementation
    }
}
```

### HTTP Status Code Usage

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

## Security Patterns

### Authentication Configuration

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

### Authorization Policies

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

### Security Middleware

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

## Performance Patterns

### Memory Management

**Pattern:** Efficient memory usage with proper disposal

```csharp
public async Task<List<PersonModel>> GetLargePersonListAsync()
{
    // Use streaming for large datasets
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

### Connection Management

**Pattern:** Proper connection string management

```csharp
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NomConnection"),
        b => b.MigrationsAssembly("Nom.Data")
              .EnableRetryOnFailure(
                  maxRetryCount: 3,
                  errorCodesToAdd: null,
                  maxRetryDelay: TimeSpan.FromSeconds(30))
              .CommandTimeout(30)));
```

## Testing Patterns

### Unit Testing

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

### Integration Testing

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

## Inference Rules and Technical Specifications

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

---

_This document should be updated as the project evolves and new patterns emerge._
