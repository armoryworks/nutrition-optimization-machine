import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ShoppingAddItem, addItemOptions } from './shopping-add-item.component';
import { IngredientSearchResult } from '../core/models/ingredient-search-result.model';
import { HouseholdShoppingItem } from '../core/models/household-shopping.model';

const chicken: IngredientSearchResult = { id: 11, name: 'Chicken Breast', foodGroupName: 'Protein Foods' };

const saved: HouseholdShoppingItem = {
  id: 1,
  householdId: 7,
  name: 'Chicken Breast',
  ingredientId: 11,
  quantity: null,
  unit: null,
  checked: false,
  createdDate: '2026-10-22T00:00:00Z',
  ingredientNeedsVerification: false,
};

describe('addItemOptions', () => {
  it('offers the catalog hits and then "add as new" unless a hit has exactly that name', () => {
    expect(addItemOptions('chick', [chicken])).toEqual([
      { kind: 'catalog', hit: chicken },
      { kind: 'new', name: 'chick' },
    ]);
    expect(addItemOptions('chicken breast', [chicken])).toEqual([{ kind: 'catalog', hit: chicken }]);
    expect(addItemOptions('c', [])).toEqual([]);
  });
});

describe('ShoppingAddItem', () => {
  let http: HttpTestingController;

  function create(existing: number[] = []) {
    TestBed.configureTestingModule({
      imports: [ShoppingAddItem],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ShoppingAddItem);
    fixture.componentRef.setInput('householdId', 7);
    fixture.componentRef.setInput('existingIngredientIds', new Set(existing));
    fixture.detectChanges();
    return fixture;
  }

  const input = (el: HTMLElement) => el.querySelector<HTMLInputElement>('[data-testid="shopping-add-item-input"]')!;

  function type(el: HTMLElement, text: string) {
    const field = input(el);
    field.value = text;
    field.dispatchEvent(new Event('input'));
  }

  beforeEach(() => vi.useFakeTimers());

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('searches the catalog after a pause and offers the new-item choice on a miss', () => {
    const fixture = create();
    type(fixture.nativeElement, 'chick');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url.endsWith('/Ingredients/search') && r.params.get('q') === 'chick').flush([chicken]);
    expect(fixture.componentInstance.options()).toEqual([
      { kind: 'catalog', hit: chicken },
      { kind: 'new', name: 'chick' },
    ]);
  });

  it('adds a catalog hit to the household list and announces it', () => {
    const fixture = create();
    const added: HouseholdShoppingItem[] = [];
    fixture.componentInstance.added.subscribe((i) => added.push(i));
    type(fixture.nativeElement, 'chick');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url.endsWith('/Ingredients/search')).flush([chicken]);

    const snack = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    fixture.componentInstance.onOptionSelected({ option: { value: { kind: 'catalog', hit: chicken } } } as never);
    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/items'));
    expect(req.request.body).toEqual({ householdId: 7, name: 'Chicken Breast', ingredientId: 11, linkToCatalog: true });
    req.flush(saved, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(added).toEqual([saved]);
    expect(snack).toHaveBeenCalledWith('Added Chicken Breast', 'OK', expect.anything());
    expect(fixture.componentInstance.query()).toBe('');
    expect(input(fixture.nativeElement).value).toBe('');
  });

  it('says the item was already there when the server hands back the existing line', () => {
    const fixture = create();
    const snack = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    type(fixture.nativeElement, 'Skyr');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url.endsWith('/Ingredients/search')).flush([]);
    fixture.componentInstance.submit();

    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/items'));
    expect(req.request.body).toEqual({ householdId: 7, name: 'Skyr', ingredientId: null, linkToCatalog: true });
    req.flush({ ...saved, id: 3, name: 'Skyr', ingredientId: 44 });
    expect(snack).toHaveBeenCalledWith('Skyr is already on your list', 'OK', expect.anything());
  });

  it('creates a new household food when Enter is pressed on a name the catalog does not know', () => {
    const fixture = create();
    type(fixture.nativeElement, 'Paper towels');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url.endsWith('/Ingredients/search')).flush([]);
    fixture.detectChanges();

    input(fixture.nativeElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const req = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/items'));
    expect(req.request.body).toEqual({ householdId: 7, name: 'Paper towels', ingredientId: null, linkToCatalog: false });
    req.flush({ ...saved, id: 2, name: 'Paper towels', ingredientId: null }, { status: 201, statusText: 'Created' });
  });

  it('refuses a food already on the list without calling the server', () => {
    const fixture = create([11]);
    const snack = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    fixture.componentInstance.onOptionSelected({ option: { value: { kind: 'catalog', hit: chicken } } } as never);
    http.expectNone((r) => r.method === 'POST');
    expect(snack).toHaveBeenCalledWith('Chicken Breast is already on your list', 'OK', expect.anything());
  });

  it('keeps what was typed and shows why when the server refuses', () => {
    const fixture = create();
    type(fixture.nativeElement, 'Paper towels');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url.endsWith('/Ingredients/search')).flush([]);
    fixture.componentInstance.submit();
    http.expectOne((r) => r.method === 'POST').flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="shopping-add-item-error"]')?.textContent).toContain('Paper towels');
    expect(fixture.componentInstance.query()).toBe('Paper towels');
  });
});
