import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: {
    accessToken: string | null;
    attemptTokenRefresh: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    auth = {
      accessToken: 'token-1',
      attemptTokenRefresh: vi.fn(() =>
        of({ accessToken: 'token-2', refreshToken: 'r2', expiresIn: 3600, tokenType: 'Bearer' }),
      ),
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('attaches the bearer token to API requests', () => {
    http.get('/api/Recipe/1').subscribe();
    const req = controller.expectOne('/api/Recipe/1');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');
    req.flush({});
  });

  it('does not attach the token to login/register style endpoints', () => {
    http.post('/api/auth/login', {}).subscribe();
    const req = controller.expectOne('/api/auth/login');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('attaches the token to 2FA and manage endpoints (regression: broken 2FA)', () => {
    http.get('/api/auth/2fa/status').subscribe();
    const req = controller.expectOne('/api/auth/2fa/status');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');
    req.flush({});

    http.post('/api/auth/refresh-claims', {}).subscribe();
    const req2 = controller.expectOne('/api/auth/refresh-claims');
    expect(req2.request.headers.get('Authorization')).toBe('Bearer token-1');
    req2.flush({});
  });

  it('refreshes once on 401 and retries with the new token', () => {
    http.get('/api/Recipe/1').subscribe();
    controller.expectOne('/api/Recipe/1').flush(null, { status: 401, statusText: 'Unauthorized' });

    const retried = controller.expectOne('/api/Recipe/1');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer token-2');
    retried.flush({});
    expect(auth.attemptTokenRefresh).toHaveBeenCalledTimes(1);
  });

  it('shares a single in-flight refresh across concurrent 401s', () => {
    // Deferred refresh so both 401s land while the refresh is still pending.
    const pending = new Subject<{ accessToken: string; refreshToken: string }>();
    auth.attemptTokenRefresh = vi.fn(() => pending.asObservable());

    http.get('/api/Recipe/1').subscribe();
    http.get('/api/Recipe/2').subscribe();
    controller.expectOne('/api/Recipe/1').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/Recipe/2').flush(null, { status: 401, statusText: 'Unauthorized' });

    pending.next({ accessToken: 'token-2', refreshToken: 'r2' });
    pending.complete();

    controller.expectOne('/api/Recipe/1').flush({});
    controller.expectOne('/api/Recipe/2').flush({});
    expect(auth.attemptTokenRefresh).toHaveBeenCalledTimes(1);
  });
});
