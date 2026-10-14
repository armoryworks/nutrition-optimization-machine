import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Params, Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { Search } from './search.component';
import { RecipeSearchService } from '../core/services/recipe-search.service';
import { HouseholdStore } from '../core/services/household-store';
import { AuthService } from '../core/services/auth.service';
import { RecipeSearchRequest } from '../core/models/recipe-search-request.model';
import { RecipeSearchResponse } from '../core/models/recipe-search-response.model';

describe('Search', () => {
  let params: BehaviorSubject<Params>;
  let search: ReturnType<typeof vi.fn>;
  let getPopular: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function response(dietConflictsHidden: boolean): RecipeSearchResponse {
    return { results: [], totalCount: 0, pageNumber: 1, pageSize: 48, totalPages: 0, dietConflictsHidden };
  }

  function create(loggedIn: boolean, initial: Params, hidden = false) {
    params = new BehaviorSubject<Params>(initial);
    search = vi.fn(() => of(response(hidden)));
    getPopular = vi.fn(() => of(response(false)));
    TestBed.configureTestingModule({
      imports: [Search],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParams: params } },
        {
          provide: RecipeSearchService,
          useValue: { search, getPopular, getFilterOptions: () => of({ mealTypes: [], courses: [], kitchenTools: [] }) },
        },
        { provide: HouseholdStore, useValue: { getHouseholds: () => of([]) } },
        { provide: AuthService, useValue: { isLoggedIn: signal(loggedIn).asReadonly() } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    const fixture = TestBed.createComponent(Search);
    fixture.detectChanges();
    return fixture;
  }

  const lastRequest = (): RecipeSearchRequest => search.mock.calls.at(-1)![0];
  const el = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;

  it('lets the server decide the restriction filter by default and shows it as on when applied', () => {
    const fixture = create(true, { q: 'chicken' }, true);

    expect(lastRequest().hideDietConflicts).toBeUndefined();
    fixture.componentInstance.toggleFilterPanel();
    fixture.detectChanges();
    const toggle = el(fixture).querySelector('[data-testid="search-filter-conflicts-toggle"] button[role="switch"]')!;
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    expect(fixture.componentInstance.pills().map((p) => p.kind)).toEqual(['conflicts']);
    expect(el(fixture).querySelector('[data-testid="search-filter-clear-btn"]')).toBeNull();
  });

  it('sends an explicit choice and keeps it when other filters are cleared', () => {
    const fixture = create(true, { q: 'chicken', conflicts: 'show', maxTime: '30' }, false);

    expect(lastRequest().hideDietConflicts).toBe(false);
    fixture.componentInstance.clearFilters();
    expect(navigate.mock.calls.at(-1)![1].queryParams).toEqual(expect.objectContaining({ conflicts: 'show', maxTime: null }));

    fixture.componentInstance.setHideConflicts(true);
    expect(navigate.mock.calls.at(-1)![1].queryParams).toEqual(expect.objectContaining({ conflicts: 'hide' }));
  });

  it('searches, rather than listing popular recipes, for a signed-in browse so the filter applies', () => {
    create(true, {}, true);

    expect(search).toHaveBeenCalledTimes(1);
    expect(getPopular).not.toHaveBeenCalled();
  });

  it('never sends the restriction filter for a guest and hides its toggle', () => {
    const fixture = create(false, { q: 'chicken', conflicts: 'hide' });

    expect(lastRequest().hideDietConflicts).toBeUndefined();
    fixture.componentInstance.toggleFilterPanel();
    fixture.detectChanges();
    expect(el(fixture).querySelector('[data-testid="search-filter-conflicts-toggle"]')).toBeNull();
  });

  it('says when the restriction filter is what left the results empty and offers to show them', () => {
    const fixture = create(true, { q: 'bread' }, true);
    fixture.detectChanges();

    expect(el(fixture).querySelector('[data-testid="search-empty-conflicts-note"]')).not.toBeNull();
    (el(fixture).querySelector('[data-testid="search-empty-show-conflicts-btn"]') as HTMLButtonElement).click();
    expect(navigate.mock.calls.at(-1)![1].queryParams).toEqual(expect.objectContaining({ conflicts: 'show' }));
  });

  it('still lists popular recipes for a guest whose link carries the restriction filter', () => {
    create(false, { conflicts: 'hide' });

    expect(getPopular).toHaveBeenCalledTimes(1);
    expect(search).not.toHaveBeenCalled();
  });

  it('asks the server for the nutrient sorts', () => {
    create(false, { q: 'chicken', sort: 'protein' });
    expect(lastRequest().sortBy).toBe('protein');

    TestBed.resetTestingModule();
    create(false, { q: 'chicken', sort: 'calories' });
    expect(lastRequest().sortBy).toBe('calories');
  });
});
