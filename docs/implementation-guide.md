# Implementation Guide: Abstractions and Patterns

## Overview

This guide provides step-by-step instructions for implementing the new abstractions, factory patterns, pub-sub patterns, and component wrappers across the NOM project. All abstraction classes are prefixed with an underscore (\_) to clearly delineate them as infrastructure components.

## Backend Implementation

### 1. Using Base Orchestration Service

#### Before (Original PersonOrchestrationService):

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

    public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
    {
        // Repetitive implementation...
    }
}
```

#### After (Using Base Orchestration Service):

```csharp
public class PersonOrchestrationService : _BaseOrchestrationService<PersonEntity, PersonCreateModel, PersonUpdateModel, PersonResponseModel, long>
{
    private readonly IPrivacyOrchestrationService _privacyOrchestrationService;

    public _PersonOrchestrationService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IPrivacyOrchestrationService privacyOrchestrationService,
        ILogger<_PersonOrchestrationService> logger)
        : base(dbContext, httpContextAccessor, logger)
    {
        _privacyOrchestrationService = privacyOrchestrationService;
    }

    protected override DbSet<PersonEntity> EntitySet => _dbContext.Persons;

    protected override PersonResponseModel MapToResponseModel(PersonEntity entity)
    {
        return new PersonResponseModel
        {
            Id = entity.Id,
            Name = entity.Name,
            UserId = entity.UserId,
            CreatedDate = entity.CreatedDate
        };
    }

    protected override PersonEntity MapToEntity(PersonCreateModel createModel)
    {
        return new PersonEntity
        {
            Name = createModel.PersonName,
            UserId = GetCurrentUserId(),
            CreatedByPersonId = 1L
        };
    }

    protected override void UpdateEntity(PersonEntity entity, PersonUpdateModel updateModel)
    {
        entity.Name = updateModel.Name;
        entity.LastModifiedDate = DateTime.UtcNow;
        entity.LastModifiedByPersonId = GetCurrentPersonId();
    }

    protected override long GetEntityId(PersonEntity entity) => entity.Id;
    protected override void SetEntityId(PersonEntity entity, long id) => entity.Id = id;

    protected override async Task PreCreateAsync(PersonCreateModel createModel, PersonEntity entity)
    {
        // Custom pre-creation logic
        await _privacyOrchestrationService.CreateDefaultConsentsAsync(entity.Id);
    }

    protected override async Task<(bool IsValid, List<string> Errors)> ValidateCreateInternalAsync(PersonCreateModel createModel)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(createModel.PersonName))
        {
            errors.Add("Person name is required");
        }

        var existingPerson = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.UserId == GetCurrentUserId());

        if (existingPerson != null)
        {
            errors.Add("Person already exists for this user");
        }

        return (errors.Count == 0, errors);
    }
}
```

### 2. Using Factory Pattern

#### Registering Services:

```csharp
// In Program.cs or Startup.cs
public static IServiceCollection AddAbstractionServices(this IServiceCollection services)
{
    // Register the factory
    services.AddSingleton<IOrchestrationServiceFactory, OrchestrationServiceFactory>();

    // Register factory options
    services.Configure<OrchestrationServiceFactoryOptions>(options =>
    {
        options.EnableAutoDiscovery = true;
        options.EnableCaching = true;
        options.EnableValidation = true;
        options.EnableLogging = true;
    });

    // Register orchestration services
    services.AddScoped<_PersonOrchestrationService>();
    services.AddScoped<_RecipeOrchestrationService>();
    services.AddScoped<_HouseholdOrchestrationService>();

    return services;
}
```

#### Using the Factory:

```csharp
public class PersonController : BaseApiController
{
    private readonly IOrchestrationServiceFactory _serviceFactory;

    public PersonController(IOrchestrationServiceFactory serviceFactory)
    {
        _serviceFactory = serviceFactory;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePerson([FromBody] PersonCreateModel model)
    {
        var personService = _serviceFactory.CreateService<_PersonOrchestrationService>();
        var result = await personService.CreateAsync(model);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPerson(long id)
    {
        var personService = _serviceFactory.CreateService<_PersonOrchestrationService>();
        var result = await personService.GetByIdAsync(id);
        return result != null ? Ok(result) : NotFound();
    }
}
```

### 3. Using Pub-Sub Pattern

#### Creating Events:

```csharp
public class PersonCreatedEvent : _BaseEvent
{
    public override string EventType => "PersonCreated";
    public long PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    public _PersonCreatedEvent(long personId, string personName, string userId)
    {
        PersonId = personId;
        PersonName = personName;
        UserId = userId;
        Source = "PersonOrchestrationService";
    }
}

public class PersonUpdatedEvent : _BaseEvent
{
    public override string EventType => "PersonUpdated";
    public long PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;

    public _PersonUpdatedEvent(long personId, string personName)
    {
        PersonId = personId;
        PersonName = personName;
        Source = "PersonOrchestrationService";
    }
}
```

#### Creating Event Handlers:

```csharp
public class PersonCreatedEventHandler : _BaseEventHandler<_PersonCreatedEvent>
{
    private readonly ILogger<_PersonCreatedEventHandler> _logger;
    private readonly IEmailService _emailService;

    public _PersonCreatedEventHandler(
        ILogger<_PersonCreatedEventHandler> logger,
        IEmailService emailService)
    {
        _logger = logger;
        _emailService = emailService;
    }

    public override async Task HandleAsync(_PersonCreatedEvent @event)
    {
        _logger.LogInformation("Handling person created event for person {PersonId}", @event.PersonId);

        // Send welcome email
        await _emailService.SendWelcomeEmailAsync(@event.UserId, @event.PersonName);

        // Update analytics
        await UpdateAnalyticsAsync(@event);
    }

    private async Task UpdateAnalyticsAsync(_PersonCreatedEvent @event)
    {
        // Analytics logic
        await Task.CompletedTask;
    }
}
```

#### Using Event Bus:

```csharp
public class PersonOrchestrationService : _BaseOrchestrationService<PersonEntity, PersonCreateModel, PersonUpdateModel, PersonResponseModel, long>
{
    private readonly _IEventBus _eventBus;

    public _PersonOrchestrationService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        _IEventBus eventBus,
        ILogger<_PersonOrchestrationService> logger)
        : base(dbContext, httpContextAccessor, logger)
    {
        _eventBus = eventBus;
    }

    protected override async Task PostCreateAsync(PersonEntity entity, PersonCreateModel createModel)
    {
        // Publish event
        var @event = new _PersonCreatedEvent(entity.Id, entity.Name, entity.UserId);
        await _eventBus.PublishAsync(@event);
    }

    protected override async Task PostUpdateAsync(PersonEntity entity, PersonUpdateModel updateModel)
    {
        // Publish event
        var @event = new _PersonUpdatedEvent(entity.Id, entity.Name);
        await _eventBus.PublishAsync(@event);
    }
}
```

## Frontend Implementation

### 1. Using Base Service

#### Before (Original RecipeService):

```typescript
@Injectable({
  providedIn: "root",
})
export class RecipeService {
  private apiUrl = `${environment.apiUrl}/recipe`;

  constructor(private http: HttpClient) {}

  getRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(this.apiUrl);
  }

  getRecipe(id: number): Observable<RecipeModel> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`);
  }

  createRecipe(recipe: RecipeCreateModel): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, recipe);
  }
}
```

#### After (Using Base Service):

```typescript
@Injectable({
  providedIn: "root",
})
export class RecipeService
  extends _BaseService
  implements _ICrudService<RecipeModel, number>
{
  readonly serviceName = "RecipeService";
  readonly isInitialized = true;

  private apiUrl = `${environment.apiUrl}/recipe`;

  constructor(private http: HttpClient, private logger: Logger) {
    super();
  }

  async initialize(): Promise<void> {
    this.logInfo("Initializing RecipeService");
    // Initialization logic
  }

  async dispose(): Promise<void> {
    this.logInfo("Disposing RecipeService");
    // Cleanup logic
  }

  getHealthStatus(): Observable<_ServiceHealthStatus> {
    return of({
      isHealthy: true,
      serviceName: this.serviceName,
      timestamp: new Date(),
      errors: [],
    });
  }

  getAll(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(this.apiUrl).pipe(
      catchError((error) => {
        this.handleError(error, "getAll");
        return throwError(() => error);
      })
    );
  }

  getById(id: number): Observable<RecipeModel | null> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`).pipe(
      catchError((error) => {
        this.handleError(error, "getById");
        return throwError(() => error);
      })
    );
  }

  create(item: Partial<RecipeModel>): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, item).pipe(
      catchError((error) => {
        this.handleError(error, "create");
        return throwError(() => error);
      })
    );
  }

  update(id: number, item: Partial<RecipeModel>): Observable<RecipeModel> {
    return this.http.put<RecipeModel>(`${this.apiUrl}/${id}`, item).pipe(
      catchError((error) => {
        this.handleError(error, "update");
        return throwError(() => error);
      })
    );
  }

  delete(id: number): Observable<boolean> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      map(() => true),
      catchError((error) => {
        this.handleError(error, "delete");
        return throwError(() => error);
      })
    );
  }

  exists(id: number): Observable<boolean> {
    return this.getById(id).pipe(map((recipe) => recipe !== null));
  }

  getPaged(
    page: number,
    pageSize: number
  ): Observable<_PagedResult<RecipeModel>> {
    return this.http
      .get<RecipeModel[]>(`${this.apiUrl}?page=${page}&pageSize=${pageSize}`)
      .pipe(
        map((recipes) => ({
          items: recipes,
          totalCount: recipes.length,
          page,
          pageSize,
          totalPages: Math.ceil(recipes.length / pageSize),
          hasNextPage: page * pageSize < recipes.length,
          hasPreviousPage: page > 1,
        })),
        catchError((error) => {
          this.handleError(error, "getPaged");
          return throwError(() => error);
        })
      );
  }

  search(query: string): Observable<RecipeModel[]> {
    return this.http
      .get<RecipeModel[]>(`${this.apiUrl}/search?q=${query}`)
      .pipe(
        catchError((error) => {
          this.handleError(error, "search");
          return throwError(() => error);
        })
      );
  }

  validate(item: Partial<RecipeModel>): Observable<_ValidationResult> {
    const errors: string[] = [];
    const warnings: string[] = [];

    if (!item.name || item.name.trim().length === 0) {
      errors.push("Recipe name is required");
    }

    if (item.name && item.name.length < 3) {
      warnings.push("Recipe name should be at least 3 characters long");
    }

    return of({
      isValid: errors.length === 0,
      errors,
      warnings,
      value: item,
    });
  }
}
```

### 2. Using Component Wrappers

#### Creating a Custom Input Component:

```typescript
@Component({
  selector: "nom-recipe-name-input",
  template: `
    <mat-form-field [appearance]="appearance" [class]="getCssClasses()">
      <mat-label>{{ label }}</mat-label>
      <input
        matInput
        [type]="type"
        [placeholder]="placeholder"
        [required]="required"
        [disabled]="disabled"
        [readonly]="readonly"
        [maxlength]="maxlength"
        [minlength]="minlength"
        [pattern]="pattern"
        [autocomplete]="autocomplete"
        [autofocus]="autofocus"
        [spellcheck]="spellcheck"
        [autocapitalize]="autocapitalize"
        [autocorrect]="autocorrect"
        [tabindex]="tabindex"
        [formControl]="formControl"
        (focus)="onFocus($event)"
        (blur)="onBlur($event)"
        (click)="onClick($event)"
        (dblclick)="onDblClick($event)"
        (keydown)="onKeyDown($event)"
        (keyup)="onKeyUp($event)"
        [ngStyle]="getInputStyles()"
      />

      <mat-icon matPrefix *ngIf="showPrefixIcon">{{ prefixIcon }}</mat-icon>
      <mat-icon matSuffix *ngIf="showSuffixIcon">{{ suffixIcon }}</mat-icon>

      <button
        matSuffix
        mat-icon-button
        *ngIf="showClearButton && value"
        (click)="onClear()"
        type="button"
      >
        <mat-icon>clear</mat-icon>
      </button>

      <mat-hint *ngIf="showHint">{{ hint }}</mat-hint>
      <mat-error *ngIf="showError">{{ error }}</mat-error>
      <mat-error *ngIf="showSuccess">{{ success }}</mat-error>
    </mat-form-field>
  `,
  styleUrls: ["./recipe-name-input.component.scss"],
})
export class RecipeNameInputComponent extends _BaseInputComponent {
  constructor() {
    super();
    this.label = "Recipe Name";
    this.placeholder = "Enter recipe name";
    this.required = true;
    this.maxlength = 100;
    this.minlength = 3;
    this.pattern = "^[a-zA-Z0-9\\s\\-'\"]+$";
    this.autocomplete = "off";
    this.showHint = true;
    this.hint = "Enter a descriptive name for your recipe";
    this.showClearButton = true;
    this.showPrefixIcon = true;
    this.prefixIcon = "restaurant";
  }

  protected override validateValue(value: any): void {
    super.validateValue(value);

    // Custom validation for recipe names
    if (value && value.length > 0) {
      if (value.length < 3) {
        this.error = "Recipe name must be at least 3 characters long";
        this.showError = true;
      } else if (!/^[a-zA-Z0-9\s\-'"]+$/.test(value)) {
        this.error = "Recipe name contains invalid characters";
        this.showError = true;
      } else {
        this.showError = false;
        this.error = "";
      }
    }
  }
}
```

### 3. Using Factory Pattern

#### Creating Service Factory:

```typescript
@Injectable({
  providedIn: "root",
})
export class ServiceFactory {
  private serviceCache = new Map<string, any>();

  constructor(private injector: Injector, private logger: Logger) {}

  createService<T>(serviceType: new (...args: any[]) => T): T {
    const serviceName = serviceType.name;

    if (this.serviceCache.has(serviceName)) {
      return this.serviceCache.get(serviceName);
    }

    try {
      const service = this.injector.get(serviceType);
      this.serviceCache.set(serviceName, service);
      this.logger.log(`Created service: ${serviceName}`);
      return service;
    } catch (error) {
      this.logger.error(`Failed to create service: ${serviceName}`, error);
      throw error;
    }
  }

  createCrudService<T, TId = number>(
    entityName: string,
    apiUrl: string
  ): _ICrudService<T, TId> {
    return new _BaseCrudService<T, TId>(
      entityName,
      apiUrl,
      this.injector.get(HttpClient)
    );
  }

  clearCache(): void {
    this.serviceCache.clear();
    this.logger.log("Service cache cleared");
  }
}
```

#### Using Service Factory:

```typescript
@Component({
  selector: "nom-recipe-list",
  template: `...`,
})
export class RecipeListComponent implements OnInit {
  private recipeService: _ICrudService<RecipeModel, number>;

  constructor(private serviceFactory: _ServiceFactory) {
    this.recipeService = serviceFactory.createService(_RecipeService);
  }

  ngOnInit(): void {
    this.loadRecipes();
  }

  private loadRecipes(): void {
    this.recipeService.getAll().subscribe(
      (recipes) => {
        // Handle recipes
      },
      (error) => {
        // Handle error
      }
    );
  }
}
```

## Application-Wide Implementation

### 1. Backend Application-Wide

#### Update Program.cs:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add abstraction services
builder.Services.AddAbstractionServices();

// Add event bus
builder.Services.AddSingleton<_IEventBus, _EventBus>();

// Add orchestration service factory
builder.Services.AddSingleton<IOrchestrationServiceFactory, OrchestrationServiceFactory>();

// Register event handlers
builder.Services.AddScoped<_IEventHandler<_PersonCreatedEvent>, _PersonCreatedEventHandler>();
builder.Services.AddScoped<_IEventHandler<_PersonUpdatedEvent>, _PersonUpdatedEventHandler>();

var app = builder.Build();

// Initialize event bus
var eventBus = app.Services.GetRequiredService<_IEventBus>();
// Register event handlers
```

#### Update All Controllers:

```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BaseController : ControllerBase
{
    protected readonly IOrchestrationServiceFactory _serviceFactory;
    protected readonly _IEventBus _eventBus;
    protected readonly ILogger _logger;

    public _BaseController(
        IOrchestrationServiceFactory serviceFactory,
        _IEventBus eventBus,
        ILogger logger)
    {
        _serviceFactory = serviceFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected long GetCurrentPersonId()
    {
        var personIdClaim = User.Claims.FirstOrDefault(c => c.Type == "PersonId")?.Value;
        if (long.TryParse(personIdClaim, out long personId))
        {
            return personId;
        }
        throw new UnauthorizedAccessException("PersonId claim is missing");
    }

    protected async Task<IActionResult> HandleServiceOperation<TResult>(
        Func<Task<TResult>> operation,
        string operationName)
    {
        try
        {
            var result = await operation();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {OperationName}", operationName);
            return StatusCode(500, new { message = "An internal error occurred" });
        }
    }
}
```

### 2. Frontend Application-Wide

#### Update app.config.ts:

```typescript
import { ApplicationConfig } from "@angular/core";
import { provideRouter } from "@angular/router";
import { provideHttpClient } from "@angular/common/http";
import { provideAnimations } from "@angular/platform-browser/animations";

import { routes } from "./app.routes";
import { _ServiceFactory } from "./_Abstractions/_Factories/_ServiceFactory";
import { _EventBus } from "./_Abstractions/_Events/_EventBus";

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(),
    provideAnimations(),
    _ServiceFactory,
    _EventBus,
  ],
};
```

#### Update All Components:

```typescript
@Component({
  selector: "nom-base-component",
  template: "",
})
export class BaseComponent implements OnInit, OnDestroy {
  protected destroy$ = new Subject<void>();

  constructor(
    protected serviceFactory: _ServiceFactory,
    protected eventBus: _EventBus,
    protected logger: Logger
  ) {}

  ngOnInit(): void {
    this.initialize();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  protected initialize(): void {
    // Override in derived classes
  }

  protected logInfo(message: string, data?: any): void {
    this.logger.log(message, data);
  }

  protected logError(message: string, error?: any): void {
    this.logger.error(message, error);
  }

  protected handleError(error: any, context?: string): void {
    this.logError(`Error in ${context}`, error);
    // Additional error handling logic
  }
}
```

## Benefits Achieved

### 1. Maintainability

- **Centralized Patterns**: All common functionality is centralized in base classes
- **Reduced Duplication**: Eliminates repetitive code across services and components
- **Consistent Behavior**: Standardized error handling, logging, and validation
- **Easy Updates**: Changes to common patterns only need to be made once

### 2. Scalability

- **Factory Pattern**: Easy to create new services and components
- **Dependency Injection**: Proper service lifecycle management
- **Event-Driven Architecture**: Loose coupling between components
- **Caching**: Built-in caching for improved performance

### 3. Testability

- **Mockable Abstractions**: Easy to mock and test
- **Isolated Concerns**: Clear separation of responsibilities
- **Standardized Testing**: Common testing patterns
- **Error Handling**: Consistent error handling for testing

### 4. Developer Experience

- **Clear Patterns**: Well-defined patterns to follow
- **IntelliSense Support**: Better IDE support with abstractions
- **Documentation**: Comprehensive documentation and examples
- **Consistency**: Consistent code style and structure

### 5. Performance

- **Optimized Patterns**: Efficient implementations for common operations
- **Caching**: Built-in caching mechanisms
- **Lazy Loading**: Services created on-demand
- **Memory Management**: Proper resource cleanup

## Migration Checklist

### Backend Migration

- [ ] Create abstraction layer files
- [ ] Update existing orchestration services to use base classes
- [ ] Implement factory patterns
- [ ] Add pub-sub patterns
- [ ] Update dependency injection registration
- [ ] Update all controllers to use new patterns
- [ ] Add comprehensive error handling
- [ ] Implement logging throughout
- [ ] Add unit tests for new abstractions
- [ ] Update documentation

### Frontend Migration

- [ ] Create abstraction layer files
- [ ] Update existing services to use base classes
- [ ] Implement component wrappers
- [ ] Add factory patterns
- [ ] Implement pub-sub patterns
- [ ] Update dependency injection
- [ ] Add comprehensive error handling
- [ ] Implement logging throughout
- [ ] Add unit tests for new abstractions
- [ ] Update documentation

### Application-Wide

- [ ] Update all modules to use new patterns
- [ ] Implement comprehensive testing
- [ ] Performance optimization
- [ ] Security review
- [ ] Documentation updates
- [ ] Training and onboarding materials

## Conclusion

This implementation provides a solid foundation for the NOM project with:

1. **Comprehensive Abstractions**: All common patterns abstracted into reusable base classes
2. **Factory Patterns**: Easy service and component creation with dependency injection
3. **Pub-Sub Patterns**: Loose coupling through event-driven architecture
4. **Component Wrappers**: Reusable UI components with consistent behavior
5. **Application-Wide Implementation**: Consistent patterns across the entire application

The underscore prefix convention clearly delineates infrastructure components, making the codebase more organized and easier to navigate. The phased implementation approach ensures minimal disruption to ongoing development while providing immediate benefits as each phase is completed.
