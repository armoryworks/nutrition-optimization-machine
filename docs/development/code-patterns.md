# Code Pattern Library

This document provides reusable code patterns for common development scenarios in the NOM project. These patterns are optimized for AI tools and follow established conventions.

## 🎯 **Service Patterns**

### Standard Service Template

```typescript
import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { BehaviorSubject, Observable } from "rxjs";
import { map, tap } from "rxjs/operators";
import { environment } from "@env/environment";

@Injectable({
  providedIn: "root",
})
export class MyService {
  private apiUrl = `${environment.apiUrl}/my-endpoint`;
  private dataSubject = new BehaviorSubject<any[]>([]);
  public data$ = this.dataSubject.asObservable();

  constructor(private http: HttpClient) {}

  // GET all items
  getAll(): Observable<any[]> {
    return this.http
      .get<any[]>(this.apiUrl)
      .pipe(tap((data) => this.dataSubject.next(data)));
  }

  // GET single item
  getById(id: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  // POST new item
  create(item: any): Observable<any> {
    return this.http.post<any>(this.apiUrl, item).pipe(
      tap((newItem) => {
        const current = this.dataSubject.value;
        this.dataSubject.next([...current, newItem]);
      })
    );
  }

  // PUT update item
  update(id: string, item: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, item).pipe(
      tap((updatedItem) => {
        const current = this.dataSubject.value;
        const index = current.findIndex((item) => item.id === id);
        if (index !== -1) {
          current[index] = updatedItem;
          this.dataSubject.next([...current]);
        }
      })
    );
  }

  // DELETE item
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      tap(() => {
        const current = this.dataSubject.value;
        this.dataSubject.next(current.filter((item) => item.id !== id));
      })
    );
  }

  // Search with debounce
  search(term: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/search`, {
      params: { q: term },
    });
  }
}
```

### Orchestration Service Pattern

```typescript
import { Injectable } from "@angular/core";
import { Observable, combineLatest } from "rxjs";
import { map, switchMap } from "rxjs/operators";

@Injectable({
  providedIn: "root",
})
export class MyOrchestrationService {
  constructor(
    private primaryService: PrimaryService,
    private secondaryService: SecondaryService
  ) {}

  // Complex operation combining multiple services
  performComplexOperation(data: any): Observable<any> {
    return this.primaryService.process(data).pipe(
      switchMap((result) =>
        combineLatest([
          this.secondaryService.validate(result),
          this.secondaryService.enrich(result),
        ]).pipe(
          map(([validation, enrichment]) => ({
            ...result,
            validation,
            enrichment,
          }))
        )
      )
    );
  }

  // Batch operations
  performBatchOperations(items: any[]): Observable<any[]> {
    return combineLatest(
      items.map((item) => this.primaryService.process(item))
    );
  }
}
```

## 🎨 **Component Patterns**

### Standard Page Component

```typescript
import { Component, OnInit, OnDestroy } from "@angular/core";
import { takeUntil } from "rxjs/operators";
import { Subject } from "rxjs";
import {
  BasePageComponent,
  BasePageConfig,
} from "@app/common/components/base-page";

@Component({
  selector: "app-my-page",
  standalone: true,
  imports: [BasePageComponent],
  template: `
    <app-base-page
      [config]="pageConfig"
      [isLoading]="isLoading"
      [error]="error"
      (back)="onBack()"
      (refresh)="onRefresh()"
      (retry)="onRetry()"
    >
      <div class="my-page__content">
        <!-- Page content here -->
      </div>
    </app-base-page>
  `,
  styleUrls: ["./my-page.component.scss"],
})
export class MyPageComponent
  extends BasePageComponent
  implements OnInit, OnDestroy
{
  private destroy$ = new Subject<void>();
  isLoading = false;
  error: string | null = null;
  data: any[] = [];

  pageConfig: BasePageConfig = {
    title: "My Page",
    showBackButton: true,
    actions: [
      { label: "Add New", icon: "add", action: () => this.onAdd() },
      { label: "Export", icon: "download", action: () => this.onExport() },
    ],
  };

  constructor(private myService: MyService) {
    super();
  }

  ngOnInit() {
    this.loadData();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData() {
    this.isLoading = true;
    this.error = null;

    this.myService
      .getAll()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.data = data;
          this.isLoading = false;
        },
        error: (error) => {
          this.error = error.message;
          this.isLoading = false;
        },
      });
  }

  onBack() {
    this.router.navigate(["/previous-route"]);
  }

  onRefresh() {
    this.loadData();
  }

  onRetry() {
    this.error = null;
    this.loadData();
  }

  onAdd() {
    this.router.navigate(["/add-route"]);
  }

  onExport() {
    // Export logic
  }
}
```

### Form Component with Validation

```typescript
import { Component, OnInit } from "@angular/core";
import { FormBuilder, FormGroup, Validators } from "@angular/forms";
import {
  BaseFormComponent,
  BaseFormConfig,
} from "@app/common/components/base-form";

@Component({
  selector: "app-my-form",
  standalone: true,
  imports: [BaseFormComponent],
  template: `
    <app-base-form
      [config]="formConfig"
      [form]="form"
      (submit)="onSubmit($event)"
      (cancel)="onCancel()"
    >
    </app-base-form>
  `,
})
export class MyFormComponent extends BaseFormComponent implements OnInit {
  form: FormGroup;

  formConfig: BaseFormConfig = {
    title: "My Form",
    submitLabel: "Save",
    cancelLabel: "Cancel",
    fields: [
      { name: "name", label: "Name", type: "text", required: true },
      { name: "email", label: "Email", type: "email", required: true },
      {
        name: "description",
        label: "Description",
        type: "textarea",
        required: false,
      },
    ],
  };

  constructor(
    private fb: FormBuilder,
    private myService: MyService,
    private notificationService: NotificationService
  ) {
    super();
  }

  ngOnInit() {
    this.initForm();
  }

  private initForm() {
    this.form = this.fb.group({
      name: ["", [Validators.required, Validators.minLength(2)]],
      email: ["", [Validators.required, Validators.email]],
      description: ["", [Validators.maxLength(500)]],
    });
  }

  onSubmit(formData: any) {
    if (this.form.valid) {
      this.myService.create(formData).subscribe({
        next: () => {
          this.notificationService.showSuccess("Item created successfully");
          this.router.navigate(["/success-route"]);
        },
        error: (error) => {
          this.notificationService.showError(error.message);
        },
      });
    }
  }

  onCancel() {
    this.router.navigate(["/cancel-route"]);
  }
}
```

### List Component with Search

```typescript
import { Component, OnInit, OnDestroy } from "@angular/core";
import { takeUntil, debounceTime, distinctUntilChanged } from "rxjs/operators";
import { Subject } from "rxjs";
import {
  BaseListComponent,
  BaseListConfig,
} from "@app/common/components/base-list";

@Component({
  selector: "app-my-list",
  standalone: true,
  imports: [BaseListComponent],
  template: `
    <app-base-list
      [config]="listConfig"
      [items]="items"
      [isLoading]="isLoading"
      [error]="error"
      (search)="onSearch($event)"
      (itemSelect)="onItemSelect($event)"
      (refresh)="onRefresh()"
    >
    </app-base-list>
  `,
})
export class MyListComponent
  extends BaseListComponent
  implements OnInit, OnDestroy
{
  private destroy$ = new Subject<void>();
  private searchSubject = new Subject<string>();

  isLoading = false;
  error: string | null = null;
  items: any[] = [];
  searchTerm = "";

  listConfig: BaseListConfig = {
    title: "My List",
    searchPlaceholder: "Search items...",
    columns: [
      { field: "name", header: "Name", sortable: true },
      { field: "status", header: "Status", sortable: true },
      { field: "createdDate", header: "Created", sortable: true },
    ],
    actions: [{ label: "Add New", icon: "add", action: () => this.onAdd() }],
  };

  constructor(private myService: MyService) {
    super();
  }

  ngOnInit() {
    this.setupSearch();
    this.loadData();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private setupSearch() {
    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe((term) => {
        this.performSearch(term);
      });
  }

  private loadData() {
    this.isLoading = true;
    this.error = null;

    this.myService
      .getAll()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.items = data;
          this.isLoading = false;
        },
        error: (error) => {
          this.error = error.message;
          this.isLoading = false;
        },
      });
  }

  private performSearch(term: string) {
    if (!term.trim()) {
      this.loadData();
      return;
    }

    this.isLoading = true;
    this.myService
      .search(term)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.items = data;
          this.isLoading = false;
        },
        error: (error) => {
          this.error = error.message;
          this.isLoading = false;
        },
      });
  }

  onSearch(term: string) {
    this.searchTerm = term;
    this.searchSubject.next(term);
  }

  onItemSelect(item: any) {
    this.router.navigate(["/detail", item.id]);
  }

  onRefresh() {
    this.loadData();
  }

  onAdd() {
    this.router.navigate(["/add"]);
  }
}
```

## 🔧 **Utility Patterns**

### Error Handler Service

```typescript
import { Injectable } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { NotificationService } from "./notification.service";

@Injectable({
  providedIn: "root",
})
export class ErrorHandlerService {
  constructor(private notificationService: NotificationService) {}

  handleError(error: any, context?: string): string {
    let message = "An unexpected error occurred";

    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 400:
          message = "Invalid request. Please check your input.";
          break;
        case 401:
          message = "Authentication required. Please log in.";
          break;
        case 403:
          message = "Access denied. You do not have permission.";
          break;
        case 404:
          message = "The requested resource was not found.";
          break;
        case 500:
          message = "Server error. Please try again later.";
          break;
        default:
          message = error.error?.message || "Network error occurred.";
      }
    } else if (error.message) {
      message = error.message;
    }

    this.notificationService.showError(message);
    console.error(`Error in ${context || "unknown context"}:`, error);

    return message;
  }

  handleValidationErrors(errors: any): string[] {
    const messages: string[] = [];

    Object.keys(errors).forEach((key) => {
      const fieldErrors = errors[key];
      if (Array.isArray(fieldErrors)) {
        messages.push(...fieldErrors);
      } else if (typeof fieldErrors === "string") {
        messages.push(fieldErrors);
      }
    });

    return messages;
  }
}
```

### Loading State Manager

```typescript
import { Injectable } from "@angular/core";
import { BehaviorSubject } from "rxjs";

export interface LoadingState {
  isLoading: boolean;
  message?: string;
  progress?: number;
}

@Injectable({
  providedIn: "root",
})
export class LoadingStateService {
  private loadingSubject = new BehaviorSubject<LoadingState>({
    isLoading: false,
  });
  public loading$ = this.loadingSubject.asObservable();

  show(message?: string) {
    this.loadingSubject.next({ isLoading: true, message });
  }

  hide() {
    this.loadingSubject.next({ isLoading: false });
  }

  updateProgress(progress: number) {
    const current = this.loadingSubject.value;
    this.loadingSubject.next({ ...current, progress });
  }

  updateMessage(message: string) {
    const current = this.loadingSubject.value;
    this.loadingSubject.next({ ...current, message });
  }
}
```

## 🎨 **Styling Patterns**

### Component SCSS Template

```scss
// Component-specific styles following BEM methodology
.my-component {
  // Base component styles
  display: flex;
  flex-direction: column;
  height: 100%;

  // Header section
  &__header {
    padding: 16px;
    border-bottom: 1px solid var(--md-sys-color-outline);
    background-color: var(--md-sys-color-surface);
  }

  // Content section
  &__content {
    flex: 1;
    padding: 16px;
    overflow-y: auto;
  }

  // Actions section
  &__actions {
    display: flex;
    gap: 8px;
    justify-content: flex-end;
    padding: 16px;
    border-top: 1px solid var(--md-sys-color-outline);
    background-color: var(--md-sys-color-surface);
  }

  // Loading state
  &--loading {
    opacity: 0.6;
    pointer-events: none;
  }

  // Error state
  &--error {
    border: 1px solid var(--md-sys-color-error);
  }

  // Responsive design
  @media (max-width: 768px) {
    &__header,
    &__content,
    &__actions {
      padding: 12px;
    }
  }
}
```

### Material 3 Theme Integration

```scss
// Custom component with Material 3 theming
.custom-card {
  background-color: var(--md-sys-color-surface);
  color: var(--md-sys-color-on-surface);
  border-radius: var(--md-sys-shape-corner-medium);
  box-shadow: var(--md-sys-elevation-level1);
  border: 1px solid var(--md-sys-color-outline);

  &:hover {
    box-shadow: var(--md-sys-elevation-level2);
  }

  &__title {
    color: var(--md-sys-color-primary);
    font-size: var(--md-sys-typescale-title-medium-size);
    font-weight: var(--md-sys-typescale-title-medium-weight);
  }

  &__content {
    color: var(--md-sys-color-on-surface-variant);
    font-size: var(--md-sys-typescale-body-medium-size);
  }

  &__actions {
    display: flex;
    gap: 8px;
    justify-content: flex-end;
    margin-top: 16px;
  }
}
```

## 🔄 **RxJS Patterns**

### Data Loading with Caching

```typescript
import { Injectable } from "@angular/core";
import { BehaviorSubject, Observable, timer } from "rxjs";
import { switchMap, shareReplay, catchError } from "rxjs/operators";

@Injectable({
  providedIn: "root",
})
export class CachedDataService {
  private cache = new Map<string, { data: any; timestamp: number }>();
  private readonly CACHE_DURATION = 5 * 60 * 1000; // 5 minutes

  getData<T>(key: string, dataLoader: () => Observable<T>): Observable<T> {
    const cached = this.cache.get(key);
    const now = Date.now();

    if (cached && now - cached.timestamp < this.CACHE_DURATION) {
      return new Observable((observer) => {
        observer.next(cached.data);
        observer.complete();
      });
    }

    return dataLoader().pipe(
      tap((data) => {
        this.cache.set(key, { data, timestamp: now });
      }),
      shareReplay(1)
    );
  }

  clearCache(key?: string) {
    if (key) {
      this.cache.delete(key);
    } else {
      this.cache.clear();
    }
  }
}
```

### Retry with Exponential Backoff

```typescript
import { Injectable } from "@angular/core";
import { Observable, throwError, timer } from "rxjs";
import { retryWhen, mergeMap } from "rxjs/operators";

@Injectable({
  providedIn: "root",
})
export class RetryService {
  retryWithBackoff<T>(
    source: Observable<T>,
    maxRetries: number = 3,
    baseDelay: number = 1000
  ): Observable<T> {
    return source.pipe(
      retryWhen((errors) =>
        errors.pipe(
          mergeMap((error, index) => {
            if (index >= maxRetries) {
              return throwError(error);
            }
            const delay = baseDelay * Math.pow(2, index);
            return timer(delay);
          })
        )
      )
    );
  }
}
```

## 🧪 **Testing Patterns**

### Service Test Template

```typescript
import { TestBed } from "@angular/core/testing";
import {
  HttpClientTestingModule,
  HttpTestingController,
} from "@angular/common/http/testing";
import { MyService } from "./my.service";

describe("MyService", () => {
  let service: MyService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [MyService],
    });

    service = TestBed.inject(MyService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it("should be created", () => {
    expect(service).toBeTruthy();
  });

  it("should get all items", () => {
    const mockData = [{ id: "1", name: "Test" }];

    service.getAll().subscribe((data) => {
      expect(data).toEqual(mockData);
    });

    const req = httpMock.expectOne("/api/my-endpoint");
    expect(req.request.method).toBe("GET");
    req.flush(mockData);
  });

  it("should create new item", () => {
    const newItem = { name: "New Item" };
    const createdItem = { id: "1", ...newItem };

    service.create(newItem).subscribe((data) => {
      expect(data).toEqual(createdItem);
    });

    const req = httpMock.expectOne("/api/my-endpoint");
    expect(req.request.method).toBe("POST");
    expect(req.request.body).toEqual(newItem);
    req.flush(createdItem);
  });
});
```

### Component Test Template

```typescript
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { RouterTestingModule } from "@angular/router/testing";
import { MyComponent } from "./my.component";
import { MyService } from "./my.service";
import { of } from "rxjs";

describe("MyComponent", () => {
  let component: MyComponent;
  let fixture: ComponentFixture<MyComponent>;
  let mockService: jasmine.SpyObj<MyService>;

  beforeEach(async () => {
    const spy = jasmine.createSpyObj("MyService", ["getAll"]);

    await TestBed.configureTestingModule({
      imports: [MyComponent, RouterTestingModule],
      providers: [{ provide: MyService, useValue: spy }],
    }).compileComponents();

    fixture = TestBed.createComponent(MyComponent);
    component = fixture.componentInstance;
    mockService = TestBed.inject(MyService) as jasmine.SpyObj<MyService>;
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });

  it("should load data on init", () => {
    const mockData = [{ id: "1", name: "Test" }];
    mockService.getAll.and.returnValue(of(mockData));

    component.ngOnInit();

    expect(mockService.getAll).toHaveBeenCalled();
    expect(component.data).toEqual(mockData);
    expect(component.isLoading).toBeFalse();
  });

  it("should handle error on data load", () => {
    const error = new Error("Test error");
    mockService.getAll.and.returnValue(throwError(() => error));

    component.ngOnInit();

    expect(component.error).toBe(error.message);
    expect(component.isLoading).toBeFalse();
  });
});
```

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
