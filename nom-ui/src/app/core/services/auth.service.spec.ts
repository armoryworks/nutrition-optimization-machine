import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let controller: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('authToken', 'stale-access');
    localStorage.setItem('refreshToken', 'stale-refresh');
    localStorage.setItem('username', 'a@b.c');
    localStorage.setItem('displayName', 'A');
    localStorage.setItem('personId', '7');
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    localStorage.clear();
  });

  it('a refused refresh ends the session it was refreshing', () => {
    auth.attemptTokenRefresh().subscribe();
    controller.expectOne('/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('authToken')).toBeNull();
  });

  it('a refused refresh leaves a session that was replaced while it was in flight', () => {
    auth.attemptTokenRefresh().subscribe();
    const refresh = controller.expectOne('/api/auth/refresh');

    auth.redeemHandoffCode('abc').subscribe();
    controller
      .expectOne('/api/auth/handoff/redeem')
      .flush({ accessToken: 'fresh-access', refreshToken: 'fresh-refresh', expiresIn: 3600, tokenType: 'Bearer' });
    controller.expectOne('/api/auth/manage/info').flush({ email: 'a@b.c' });
    controller.expectOne('/api/Person/me').flush({ id: 7, name: 'A' });
    refresh.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isLoggedIn()).toBe(true);
    expect(localStorage.getItem('authToken')).toBe('fresh-access');
    expect(auth.personId()).toBe(7);
  });

  it('does not restore a stored session when booting on the handoff page', () => {
    TestBed.resetTestingModule();
    history.replaceState(null, '', '/auth/handoff');
    localStorage.setItem('authToken', 'stale-access');
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    try {
      const fresh = TestBed.inject(AuthService);
      expect(fresh.isLoggedIn()).toBe(false);
      expect(localStorage.getItem('authToken')).toBeNull();
    } finally {
      history.replaceState(null, '', '/');
    }
  });

  it('discardSession drops the stored tokens without a server call', () => {
    auth.discardSession();

    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('refreshToken')).toBeNull();
  });
});
