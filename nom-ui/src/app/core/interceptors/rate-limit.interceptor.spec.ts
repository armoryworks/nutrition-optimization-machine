import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { rateLimitInterceptor } from './rate-limit.interceptor';

describe('rateLimitInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    vi.useFakeTimers();
    snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([rateLimitInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    vi.useRealTimers();
  });

  const throttle = (url: string, retryAfter: string) =>
    controller.expectOne(url).flush('Rate limit exceeded.', {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': retryAfter },
    });

  it('retries a throttled GET once after Retry-After without bothering the user', () => {
    let result: unknown;
    http.get('/api/Household').subscribe(r => (result = r));
    throttle('/api/Household', '2');

    controller.expectNone('/api/Household');
    vi.advanceTimersByTime(2000);
    controller.expectOne('/api/Household').flush([{ id: 3 }]);

    expect(result).toEqual([{ id: 3 }]);
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('tells the user to slow down when the retry is throttled too', () => {
    let error: unknown;
    http.get('/api/Household').subscribe({ error: e => (error = e) });
    throttle('/api/Household', '1');
    vi.advanceTimersByTime(1000);
    throttle('/api/Household', '1');

    expect(error).toBeTruthy();
    expect(snackBar.open).toHaveBeenCalledTimes(1);
  });

  it('does not repeat a throttled POST and tells the user', () => {
    let error: unknown;
    http.post('/api/Recipe/1/image', {}).subscribe({ error: e => (error = e) });
    throttle('/api/Recipe/1/image', '1');
    vi.advanceTimersByTime(5000);

    controller.expectNone('/api/Recipe/1/image');
    expect(error).toBeTruthy();
    expect(snackBar.open).toHaveBeenCalledTimes(1);
  });

  it('leaves other errors alone', () => {
    http.get('/api/Recipe/1').subscribe({ error: () => undefined });
    controller.expectOne('/api/Recipe/1').flush('', { status: 500, statusText: 'Server Error' });
    expect(snackBar.open).not.toHaveBeenCalled();
  });
});
