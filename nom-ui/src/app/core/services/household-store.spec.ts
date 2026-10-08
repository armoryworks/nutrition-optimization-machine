import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { HouseholdStore } from './household-store';

describe('HouseholdStore', () => {
  let store: HouseholdStore;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(HouseholdStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('fetches once and replays the cached list to later subscribers', () => {
    const results: unknown[] = [];
    store.getHouseholds().subscribe(r => results.push(r));

    const req = http.expectOne(r => r.url.endsWith('/Household'));
    req.flush([{ id: 1, name: 'Home' }]);

    store.getHouseholds().subscribe(r => results.push(r));
    http.expectNone(r => r.url.endsWith('/Household'));
    expect(results).toHaveLength(2);
  });

  it('refetches after invalidate()', () => {
    store.getHouseholds().subscribe();
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 1, name: 'Home' }]);

    store.invalidate();
    store.getHouseholds().subscribe();
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 1, name: 'Home' }]);
  });

  it('does not cache failures', () => {
    store.getHouseholds().subscribe({ error: () => undefined });
    http.expectOne(r => r.url.endsWith('/Household')).flush(null, { status: 500, statusText: 'Server Error' });

    store.getHouseholds().subscribe();
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 1, name: 'Home' }]);
  });

  it('shares one request between the reactive stream and getHouseholds()', () => {
    const reactive: unknown[] = [];
    store.households$reactive.subscribe(r => reactive.push(r));
    store.getHouseholds().subscribe();
    TestBed.tick();

    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 1, name: 'Home' }]);
    http.expectNone(r => r.url.endsWith('/Household'));
    expect(reactive).toEqual([[{ id: 1, name: 'Home' }]]);
  });

  it('refetches the reactive stream once after invalidate()', () => {
    const reactive: unknown[] = [];
    store.households$reactive.subscribe(r => reactive.push(r));
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 1, name: 'Old' }]);

    store.invalidate();
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 2, name: 'New' }]);
    store.getHouseholds().subscribe();
    http.expectNone(r => r.url.endsWith('/Household'));
    expect(reactive).toEqual([[{ id: 1, name: 'Old' }], [{ id: 2, name: 'New' }]]);
  });

  it('refreshes stale claims and refetches when the list comes back empty', () => {
    const results: unknown[] = [];
    store.getHouseholds().subscribe(r => results.push(r));

    http.expectOne(r => r.url.endsWith('/Household')).flush([]);
    http.expectOne(r => r.url.endsWith('/auth/refresh-claims'))
      .flush({ tokenType: 'Bearer', accessToken: 'fresh', refreshToken: 'r', expiresIn: 86400 });
    http.expectOne(r => r.url.endsWith('/Household')).flush([{ id: 3, name: 'Mine' }]);

    expect(results).toEqual([[{ id: 3, name: 'Mine' }]]);
  });

  it('returns the empty list without refetching when the claims refresh fails', () => {
    const results: unknown[] = [];
    store.getHouseholds().subscribe(r => results.push(r));

    http.expectOne(r => r.url.endsWith('/Household')).flush([]);
    http.expectOne(r => r.url.endsWith('/auth/refresh-claims')).flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectNone(r => r.url.endsWith('/Household'));

    expect(results).toEqual([[]]);
  });
});
