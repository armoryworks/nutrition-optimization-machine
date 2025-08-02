# Troubleshooting Guide

This guide provides solutions for common issues encountered during NOM development. Each issue includes symptoms, causes, and step-by-step solutions.

## 🚨 **Common Issues**

### Component Not Loading

**Symptoms:**

- Component doesn't render
- Console errors about missing imports
- Angular compilation errors

**Causes:**

- Missing imports in standalone component
- Incorrect base component extension
- Missing dependencies

**Solutions:**

```typescript
// ✅ CORRECT - Proper imports
import { Component } from "@angular/core";
import {
  BasePageComponent,
  BasePageConfig,
} from "@app/common/components/base-page";

@Component({
  selector: "app-my-component",
  standalone: true,
  imports: [BasePageComponent], // Include base component
  template: `...`,
})
export class MyComponent extends BasePageComponent {
  // Implementation
}
```

### Loading States Not Working

**Symptoms:**

- Loading spinner doesn't show
- Error states not displayed
- Component appears frozen

**Causes:**

- Missing `isLoading` property
- Not calling `takeUntil` for subscriptions
- Missing error handling

**Solutions:**

```typescript
// ✅ CORRECT - Proper loading states
export class MyComponent extends BasePageComponent {
  isLoading = false;
  error: string | null = null;

  private loadData() {
    this.isLoading = true;
    this.error = null;

    this.myService
      .getData()
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
}
```

### Material 3 Theming Issues

**Symptoms:**

- Hardcoded colors visible
- Inconsistent styling
- Theme not applying correctly

**Causes:**

- Using hardcoded colors instead of theme variables
- Missing Material 3 imports
- Incorrect CSS variable usage

**Solutions:**

```scss
// ✅ CORRECT - Use theme variables
.my-component {
  background-color: var(--md-sys-color-surface);
  color: var(--md-sys-color-on-surface);
  border: 1px solid var(--md-sys-color-outline);
}

// ❌ INCORRECT - Hardcoded colors
.my-component {
  background-color: #ffffff;
  color: #000000;
  border: 1px solid #cccccc;
}
```

### Form Validation Not Working

**Symptoms:**

- Form submits invalid data
- Validation messages not showing
- Form state not updating

**Causes:**

- Missing validators
- Incorrect form group setup
- Missing error display

**Solutions:**

```typescript
// ✅ CORRECT - Proper form validation
export class MyFormComponent extends BaseFormComponent {
  form: FormGroup;

  ngOnInit() {
    this.form = this.fb.group({
      name: ["", [Validators.required, Validators.minLength(2)]],
      email: ["", [Validators.required, Validators.email]],
    });
  }

  onSubmit(formData: any) {
    if (this.form.valid) {
      // Submit logic
    }
  }
}
```

### Memory Leaks

**Symptoms:**

- Performance degradation over time
- Multiple API calls
- Console warnings about subscriptions

**Causes:**

- Missing `takeUntil` for subscriptions
- Not implementing `OnDestroy`
- Forgetting to unsubscribe

**Solutions:**

```typescript
// ✅ CORRECT - Proper subscription management
export class MyComponent
  extends BasePageComponent
  implements OnInit, OnDestroy
{
  private destroy$ = new Subject<void>();

  ngOnInit() {
    this.myService
      .getData()
      .pipe(takeUntil(this.destroy$))
      .subscribe((data) => {
        this.data = data;
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
```

### Navigation Issues

**Symptoms:**

- Back button not working
- Navigation not responding
- Route parameters missing

**Causes:**

- Missing `onBack()` implementation
- Incorrect route configuration
- Missing router injection

**Solutions:**

```typescript
// ✅ CORRECT - Proper navigation
export class MyComponent extends BasePageComponent {
  constructor(private router: Router) {
    super();
  }

  onBack() {
    this.router.navigate(["/previous-route"]);
  }

  onItemSelect(item: any) {
    this.router.navigate(["/detail", item.id]);
  }
}
```

## 🔧 **Debugging Techniques**

### Console Debugging

```typescript
// Add debugging to component lifecycle
export class MyComponent extends BasePageComponent {
  ngOnInit() {
    console.log("Component initialized");
    this.loadData();
  }

  private loadData() {
    console.log("Loading data...");
    this.myService.getData().subscribe({
      next: (data) => {
        console.log("Data loaded:", data);
        this.data = data;
      },
      error: (error) => {
        console.error("Error loading data:", error);
        this.error = error.message;
      },
    });
  }
}
```

### Network Debugging

```typescript
// Add HTTP interceptor for debugging
@Injectable()
export class DebugInterceptor implements HttpInterceptor {
  intercept(
    req: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    console.log("HTTP Request:", req.method, req.url);

    return next.handle(req).pipe(
      tap(
        (event) => {
          if (event instanceof HttpResponse) {
            console.log("HTTP Response:", event.status, event.url);
          }
        },
        (error) => {
          console.error("HTTP Error:", error);
        }
      )
    );
  }
}
```

### State Debugging

```typescript
// Debug component state changes
export class MyComponent extends BasePageComponent {
  private debugState() {
    console.log("Component State:", {
      isLoading: this.isLoading,
      error: this.error,
      dataLength: this.data?.length,
    });
  }

  private loadData() {
    this.isLoading = true;
    this.debugState();

    this.myService.getData().subscribe({
      next: (data) => {
        this.data = data;
        this.isLoading = false;
        this.debugState();
      },
      error: (error) => {
        this.error = error.message;
        this.isLoading = false;
        this.debugState();
      },
    });
  }
}
```

## 🚀 **Performance Issues**

### Slow Component Loading

**Symptoms:**

- Components take time to render
- Large bundle sizes
- Slow initial load

**Solutions:**

```typescript
// Use lazy loading for feature modules
const routes: Routes = [
  {
    path: "feature",
    loadChildren: () =>
      import("./feature/feature.module").then((m) => m.FeatureModule),
  },
];

// Use OnPush change detection for performance
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyComponent {
  // Implementation
}
```

### Memory Issues

**Symptoms:**

- Browser memory usage increases
- Performance degrades over time
- Multiple subscriptions

**Solutions:**

```typescript
// Use shareReplay for shared observables
export class MyService {
  private data$ = this.http.get("/api/data").pipe(shareReplay(1));

  getData() {
    return this.data$;
  }
}

// Use trackBy for ngFor performance
@Component({
  template: `
    <div *ngFor="let item of items; trackBy: trackByFn">
      {{ item.name }}
    </div>
  `,
})
export class MyComponent {
  trackByFn(index: number, item: any): any {
    return item.id;
  }
}
```

## 🔒 **Security Issues**

### Authentication Problems

**Symptoms:**

- Unauthorized API calls
- Token expiration issues
- Missing authorization headers

**Solutions:**

```typescript
// Add authorization interceptor
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(
    req: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    const token = this.authService.getToken();

    if (token) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
        },
      });
    }

    return next.handle(req);
  }
}
```

### Input Validation Issues

**Symptoms:**

- XSS vulnerabilities
- Invalid data being submitted
- Server errors from malformed input

**Solutions:**

```typescript
// Sanitize user input
import { DomSanitizer } from "@angular/platform-browser";

export class MyComponent {
  constructor(private sanitizer: DomSanitizer) {}

  sanitizeInput(input: string): string {
    return this.sanitizer.sanitize(SecurityContext.HTML, input) || "";
  }

  onSubmit(formData: any) {
    const sanitizedData = {
      ...formData,
      description: this.sanitizeInput(formData.description),
    };

    this.myService.create(sanitizedData).subscribe();
  }
}
```

## 📱 **Responsive Design Issues**

### Mobile Layout Problems

**Symptoms:**

- Layout breaks on mobile
- Touch targets too small
- Text not readable

**Solutions:**

```scss
// Responsive design with breakpoints
.my-component {
  display: flex;
  flex-direction: column;
  gap: 16px;

  @media (max-width: 768px) {
    gap: 12px;
    padding: 12px;
  }

  @media (max-width: 480px) {
    gap: 8px;
    padding: 8px;
  }
}

// Touch-friendly buttons
.my-button {
  min-height: 44px;
  min-width: 44px;
  padding: 12px 16px;

  @media (max-width: 768px) {
    min-height: 48px;
    padding: 14px 18px;
  }
}
```

## 🧪 **Testing Issues**

### Unit Test Failures

**Symptoms:**

- Tests failing after changes
- Mock services not working
- Component not rendering in tests

**Solutions:**

```typescript
// Proper test setup
describe("MyComponent", () => {
  let component: MyComponent;
  let fixture: ComponentFixture<MyComponent>;
  let mockService: jasmine.SpyObj<MyService>;

  beforeEach(async () => {
    const spy = jasmine.createSpyObj("MyService", ["getData"]);

    await TestBed.configureTestingModule({
      imports: [MyComponent],
      providers: [{ provide: MyService, useValue: spy }],
    }).compileComponents();

    fixture = TestBed.createComponent(MyComponent);
    component = fixture.componentInstance;
    mockService = TestBed.inject(MyService) as jasmine.SpyObj<MyService>;
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });
});
```

## 🔍 **Common Error Messages**

### "Cannot find module"

**Solution:** Check import paths and ensure modules are properly exported

### "Property does not exist on type"

**Solution:** Add proper type definitions or use type assertions

### "Expression has changed after it was checked"

**Solution:** Use `ChangeDetectorRef.detectChanges()` or move logic to `ngAfterViewInit`

### "Maximum call stack size exceeded"

**Solution:** Check for circular dependencies or infinite loops in subscriptions

### "Cannot read property of undefined"

**Solution:** Add null checks and proper initialization

## 📞 **Getting Help**

### When to Ask for Help

- Issue persists after trying solutions above
- Error messages are unclear
- Performance issues affect user experience
- Security concerns arise

### Information to Include

- Error messages and stack traces
- Steps to reproduce the issue
- Browser console logs
- Network tab information
- Component code and configuration

---

_Last Updated: July 30, 2025_  
_Version: 1.0_  
_Status: Active Development_
