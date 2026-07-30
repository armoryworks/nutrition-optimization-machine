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
    http.expectOne(r => r.url.endsWith('/Household')).flush([]);

    store.invalidate();
    store.getHouseholds().subscribe();
    http.expectOne(r => r.url.endsWith('/Household')).flush([]);
  });

  it('does not cache failures', () => {
    store.getHouseholds().subscribe({ error: () => undefined });
    http.expectOne(r => r.url.endsWith('/Household')).flush(null, { status: 500, statusText: 'Server Error' });

    store.getHouseholds().subscribe();
    http.expectOne(r => r.url.endsWith('/Household')).flush([]);
  });
});
