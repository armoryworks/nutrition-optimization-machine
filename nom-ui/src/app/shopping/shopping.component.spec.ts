import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { ShoppingComponent } from './shopping.component';
import { VerificationFlowService } from './verification/verification-flow.service';
import { HouseholdStore } from '../core/services/household-store';
import { toLocalDateString } from '../core/utils/local-date';
import { HouseholdShoppingItem, NeedsVerification, ShoppingState } from '../core/models/household-shopping.model';

const today = toLocalDateString(new Date());

const week = {
  weekStart: today,
  weekEnd: today,
  days: [
    {
      date: today,
      dayOfWeek: 'Today',
      exclusions: [],
      cells: [
        {
          mealTypeId: 1102,
          mealType: 'Dinner',
          entries: [{ id: 1, recipeId: 5, recipeName: 'Grilled Chicken' }],
          totalCalories: null,
          totalProteinGrams: null,
          totalCarbGrams: null,
          totalFatGrams: null,
        },
      ],
    },
  ],
};

const emptyWeek = { ...week, days: [] };

const recipe = {
  id: 5,
  name: 'Grilled Chicken',
  ingredients: [{ ingredientId: 11, name: 'Chicken Breast', quantity: 500, measurement: 'g' }],
};

const measurements = [
  { id: 1, name: 'Gram', symbol: 'g' },
  { id: 2, name: 'Milliliter', symbol: 'ml' },
  { id: 3, name: 'Piece', symbol: 'pc' },
];

function adhoc(partial: Partial<HouseholdShoppingItem> & { id: number; name: string }): HouseholdShoppingItem {
  return {
    householdId: 7,
    ingredientId: null,
    quantity: null,
    unit: null,
    checked: false,
    createdDate: '2026-10-22T00:00:00Z',
    ingredientNeedsVerification: false,
    ...partial,
  };
}

function ask(ingredientId: number, shouldAsk: boolean): NeedsVerification {
  return { ingredientId, name: 'Chicken Breast', hasVerifiedNutrition: false, hasUpc: false, askedRecently: !shouldAsk, shouldAsk };
}

describe('ShoppingComponent', () => {
  let http: HttpTestingController;
  let flow: { run: ReturnType<typeof vi.fn> };

  function create(options: { state?: ShoppingState; planned?: boolean; needs?: NeedsVerification[] } = {}) {
    const planned = options.planned ?? true;
    flow = { run: vi.fn(() => Promise.resolve()) };
    TestBed.configureTestingModule({
      imports: [ShoppingComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        { provide: HouseholdStore, useValue: { getHouseholds: () => of([{ id: 7, name: 'Home' }]) } },
      ],
    });
    TestBed.overrideComponent(ShoppingComponent, {
      set: { providers: [{ provide: VerificationFlowService, useValue: flow }] },
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ShoppingComponent);
    fixture.detectChanges();

    http.expectOne((r) => r.url.endsWith('/GroceryExport/providers')).flush([]);
    for (const req of http.match((r) => r.url.endsWith('/MealPlan/week'))) req.flush(planned ? week : emptyWeek);
    http.expectOne((r) => r.url.endsWith('/Pantry')).flush([]);
    http.expectOne((r) => r.url.endsWith('/RetailPackaging')).flush([]);
    for (const req of http.match((r) => r.url.endsWith('/Measurement/all'))) req.flush(measurements);
    http.expectOne((r) => r.url.endsWith('/MealPlan/portions/range')).flush([]);
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/Shopping')).flush(options.state ?? { items: [], checks: [] });
    if (planned) http.expectOne((r) => r.url.endsWith('/Recipe/5')).flush(recipe);
    fixture.detectChanges();
    for (const req of http.match((r) => r.url.endsWith('/Shopping/needs-verification'))) req.flush(options.needs ?? []);
    for (const req of http.match((r) => r.url.endsWith('/RetailPackaging/lookup'))) req.flush({ results: [] });
    fixture.detectChanges();
    return fixture;
  }

  const items = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLElement>('[data-testid="shopping-item"]'));
  const checkbox = (li: HTMLElement) => li.querySelector<HTMLInputElement>('mat-checkbox input')!;
  const checkPost = () => http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/Shopping/checks'));

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  describe('shared check state', () => {
    it('renders the ticks the household already made on the server', () => {
      const fixture = create({ state: { items: [], checks: [{ itemKey: '11', checkedDate: '2026-10-22T10:00:00Z' }] } });
      const [chicken] = items(fixture.nativeElement);
      expect(chicken.classList).toContain('nom-shopping__item--checked');
      expect(fixture.componentInstance.checkedCount()).toBe(1);
    });

    it('ticks instantly and tells the server which line was ticked', () => {
      const fixture = create();
      const [chicken] = items(fixture.nativeElement);
      checkbox(chicken).click();
      fixture.detectChanges();

      expect(chicken.classList).toContain('nom-shopping__item--checked');
      const req = checkPost();
      expect(req.request.body).toEqual({ householdId: 7, itemKey: '11', checked: true });
      req.flush({ itemKey: '11', checked: true });
    });

    it('reverts the tick and offers Retry when the server cannot be reached', () => {
      const fixture = create();
      const snack = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
      const [chicken] = items(fixture.nativeElement);
      checkbox(chicken).click();
      fixture.detectChanges();
      checkPost().flush(null, { status: 0, statusText: 'Offline' });
      fixture.detectChanges();

      expect(chicken.classList).not.toContain('nom-shopping__item--checked');
      expect(snack).toHaveBeenCalledWith('Could not save that tick — check your connection', 'Retry', expect.anything());
    });

    it('moves ticks made before the server kept them over once, then forgets the key', () => {
      localStorage.setItem(`nom-shopping-7-${today}-4`, JSON.stringify(['11']));
      const fixture = create();
      const req = checkPost();
      expect(req.request.body).toEqual({ householdId: 7, itemKey: '11', checked: true });
      req.flush({ itemKey: '11', checked: true });
      expect(localStorage.getItem(`nom-shopping-7-${today}-4`)).toBeNull();
      expect(items(fixture.nativeElement)[0].classList).toContain('nom-shopping__item--checked');
    });
  });

  describe('verification prompt', () => {
    it('opens the sheet on a tick only when the server says the food needs verifying', () => {
      const fixture = create({ needs: [ask(11, true)] });
      checkbox(items(fixture.nativeElement)[0]).click();
      expect(flow.run).not.toHaveBeenCalled();
      checkPost().flush({ itemKey: '11', checked: true });
      expect(flow.run).toHaveBeenCalledWith({ ingredientId: 11, name: 'Chicken Breast', adhoc: false }, 7);
    });

    it('lets the answer on the tick win even before the askable list has loaded', () => {
      const fixture = create({ needs: [] });
      checkbox(items(fixture.nativeElement)[0]).click();
      checkPost().flush({ itemKey: '11', checked: true, verification: ask(11, true) });
      expect(flow.run).toHaveBeenCalledTimes(1);
    });

    it('does not open the sheet when the tick could not be saved', () => {
      const fixture = create({ needs: [ask(11, true)] });
      checkbox(items(fixture.nativeElement)[0]).click();
      checkPost().flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(flow.run).not.toHaveBeenCalled();
      expect(items(fixture.nativeElement)[0].classList).not.toContain('nom-shopping__item--checked');
    });

    it('stays quiet for a verified or recently asked food', () => {
      const fixture = create({ needs: [ask(11, false)] });
      checkbox(items(fixture.nativeElement)[0]).click();
      checkPost().flush({ itemKey: '11', checked: true });
      expect(flow.run).not.toHaveBeenCalled();
    });

    it('asks once per session, never on an untick', () => {
      const fixture = create({ needs: [ask(11, true)] });
      const box = checkbox(items(fixture.nativeElement)[0]);
      box.click();
      checkPost().flush({ itemKey: '11', checked: true, verification: ask(11, true) });
      fixture.detectChanges();
      box.click();
      checkPost().flush({ itemKey: '11', checked: false });
      fixture.detectChanges();
      box.click();
      checkPost().flush({ itemKey: '11', checked: true, verification: ask(11, true) });
      expect(flow.run).toHaveBeenCalledTimes(1);
    });
  });

  describe('items added by the household', () => {
    const state: ShoppingState = {
      items: [
        adhoc({ id: 1, name: 'Paper towels' }),
        adhoc({ id: 2, name: 'Bananas', ingredientId: 12, quantity: 6, unit: '' }),
        adhoc({ id: 4, name: 'Skyr', ingredientId: 99 }),
      ],
      checks: [],
    };

    it('files a non-food under Household, an unplaceable food under "Added by you" and a recognised one in its aisle', () => {
      const fixture = create({ state });
      const el = fixture.nativeElement as HTMLElement;
      const depts = Array.from(el.querySelectorAll('.nom-shopping__dept-name')).map((d) => d.textContent?.trim());
      expect(depts).toEqual(['Produce', 'Meat & Seafood', 'Household', 'Added by you']);
      const adhocRows = items(el).filter((li) => li.dataset['source'] === 'adhoc');
      expect(adhocRows).toHaveLength(3);
      expect(adhocRows[0].querySelector('[data-testid="shopping-item-qty"]')?.textContent?.trim()).toBe('6');
      expect(adhocRows[1].querySelector('[data-testid="shopping-item-qty"]')?.textContent?.trim()).toContain('qty');
    });

    it('saves an amount typed on an added line to the household row so every phone sees it', () => {
      const fixture = create({ state });
      fixture.componentInstance.saveEdit('item-4', '2 dozen');
      fixture.detectChanges();
      expect(items(fixture.nativeElement).find((li) => li.textContent?.includes('Skyr'))?.textContent).toContain('2 dozen');

      const req = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Shopping/items/4'));
      expect(req.request.body).toEqual({ quantity: 2, unit: 'dozen', ingredientId: 99 });
      req.flush(adhoc({ id: 4, name: 'Skyr', ingredientId: 99, quantity: 2, unit: 'dozen' }));
      fixture.detectChanges();
      expect(fixture.componentInstance.adhocItems().find((i) => i.id === 4)?.quantity).toBe(2);
      expect(fixture.componentInstance.quantityOverrides().has('item-4')).toBe(false);
      expect(items(fixture.nativeElement).find((li) => li.textContent?.includes('Skyr'))?.textContent).toContain('2 dozen');
    });

    it('unlinks the line when the sheets end with "not a food"', async () => {
      const fixture = create({ state, needs: [ask(99, true)] });
      flow.run.mockImplementation(() => Promise.resolve('not-food'));
      const skyr = items(fixture.nativeElement).find((li) => li.textContent?.includes('Skyr'))!;
      checkbox(skyr).click();
      checkPost().flush({ itemKey: 'item-4', itemId: 4, checked: true, verification: ask(99, true) });
      await Promise.resolve();
      expect(flow.run).toHaveBeenCalledWith({ ingredientId: 99, name: 'Skyr', adhoc: true }, 7);
      expect(fixture.componentInstance.adhocItems().find((i) => i.id === 4)?.ingredientId).toBeNull();
    });

    it('ticks an added item by its row id', () => {
      const fixture = create({ state });
      const towels = items(fixture.nativeElement).find((li) => li.textContent?.includes('Paper towels'))!;
      checkbox(towels).click();
      const req = checkPost();
      expect(req.request.body).toEqual({ householdId: 7, itemId: 1, checked: true });
      req.flush({ itemKey: 'item-1', itemId: 1, checked: true });
    });

    it('removes an added item and puts it back if the server refuses', () => {
      const fixture = create({ state });
      const towels = items(fixture.nativeElement).find((li) => li.textContent?.includes('Paper towels'))!;
      towels.querySelector<HTMLButtonElement>('[data-testid="shopping-item-remove-btn"]')!.click();
      fixture.detectChanges();
      expect(items(fixture.nativeElement)).toHaveLength(3);

      http.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/Shopping/items/1')).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(items(fixture.nativeElement)).toHaveLength(4);
    });

    it('is a list, not an empty state, when only added items exist', () => {
      const fixture = create({ planned: false, state: { items: [adhoc({ id: 1, name: 'Paper towels' })], checks: [] } });
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-testid="shopping-empty"]')).toBeNull();
      expect(items(el)).toHaveLength(1);
      expect(el.querySelector('[data-testid="shopping-add-item"]')).not.toBeNull();
    });

    it('puts a freshly added item on the list and remembers to ask about it', () => {
      const fixture = create();
      fixture.componentInstance.onItemAdded(adhoc({ id: 3, name: 'Skyr', ingredientId: 44, ingredientNeedsVerification: true }));
      fixture.detectChanges();
      expect(items(fixture.nativeElement)).toHaveLength(2);
      expect(fixture.componentInstance.askable().has(44)).toBe(true);
    });
  });
});
