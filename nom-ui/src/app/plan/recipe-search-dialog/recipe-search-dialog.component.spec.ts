import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { OverlayContainer } from '@angular/cdk/overlay';
import { RecipeSearchDialog, RecipeSearchDialogData } from './recipe-search-dialog.component';
import { RecipeSearchResult } from '../../core/models/recipe-search-result.model';
import { RecipeDietMatchModel } from '../../core/models/recipe.model';

const data: RecipeSearchDialogData = {
  householdId: 5,
  date: '2026-10-12',
  mealTypeId: 1102,
  mealType: 'Dinner',
  entries: [],
};

const parmesan = {
  id: 42,
  name: 'Spaghetti Parmesan',
  dietConflicts: [] as RecipeDietMatchModel[],
} as RecipeSearchResult;

const celiac: RecipeDietMatchModel = { restrictionName: 'Celiac Disease', severity: 5, ingredientName: 'Spaghetti' };
const mildDislike: RecipeDietMatchModel = { restrictionName: 'Dislikes', severity: 2, ingredientName: 'Parmesan' };

describe('RecipeSearchDialog adding a recipe', () => {
  let http: HttpTestingController;
  let overlay: HTMLElement;

  function create() {
    TestBed.configureTestingModule({
      imports: [RecipeSearchDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const fixture = TestBed.createComponent(RecipeSearchDialog);
    fixture.detectChanges();
    return fixture;
  }

  const q = (id: string) => overlay.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  const dietRequest = () => http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/Recipe/42/diet'));
  const createRequest = () => http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/MealPlan'));

  afterEach(() => http.verify());

  it('adds a recipe without a severe conflict in one tap', () => {
    const fixture = create();
    fixture.componentInstance.addRecipe(parmesan);
    dietRequest().flush([mildDislike]);

    expect(q('diet-conflict-dialog')).toBeNull();
    const post = createRequest();
    expect(post.request.body.recipeId).toBe(42);
    post.flush({ id: 900, recipeId: 42, recipeName: parmesan.name, title: parmesan.name, notes: null });

    expect(fixture.componentInstance.entries().map((e) => e.id)).toEqual([900]);
    expect(fixture.componentInstance.entries()[0].dietConflicts).toEqual([]);
  });

  it('asks before adding a severe conflict and adds nothing on Cancel', async () => {
    const fixture = create();
    fixture.componentInstance.addRecipe(parmesan);
    dietRequest().flush([celiac, mildDislike]);
    fixture.detectChanges();

    expect(q('diet-conflict-dialog')).toBeTruthy();
    expect(q('diet-conflict-message')!.textContent).toContain('Contains Spaghetti — conflicts with Celiac Disease');
    expect(q('diet-conflict-message')!.textContent).not.toContain('Dislikes');

    q('diet-conflict-cancel-btn')!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    http.expectNone((r) => r.method === 'POST');
    expect(fixture.componentInstance.entries()).toEqual([]);
    expect(fixture.componentInstance.busy()).toBe(false);
  });

  it('adds a severe conflict only after an explicit "Add anyway", keeping the conflict on the entry', async () => {
    const fixture = create();
    fixture.componentInstance.addRecipe(parmesan);
    dietRequest().flush([celiac]);
    fixture.detectChanges();

    q('diet-conflict-add-btn')!.click();
    fixture.detectChanges();
    await fixture.whenStable();

    createRequest().flush({ id: 901, recipeId: 42, recipeName: parmesan.name, title: parmesan.name, notes: null });
    expect(fixture.componentInstance.entries()[0].dietConflicts).toEqual([celiac]);
  });

  it('falls back to the conflicts the search reported when the diet check fails', async () => {
    const fixture = create();
    fixture.componentInstance.addRecipe({ ...parmesan, dietConflicts: [celiac] });
    dietRequest().flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(q('diet-conflict-dialog')).toBeTruthy();
    http.expectNone((r) => r.method === 'POST');
    q('diet-conflict-cancel-btn')!.click();
    await fixture.whenStable();
  });

  it('finds prepared foods for the plan, marks them Prepared and adds one as a food of its own', () => {
    vi.useFakeTimers();
    try {
      const fixture = create();
      fixture.componentInstance.onWholeFoodInput('hamburger');
      vi.advanceTimersByTime(300);

      const search = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/ingredients/search'));
      expect(search.request.params.get('q')).toBe('hamburger');
      expect(search.request.params.get('prepared')).toBe('true');
      const burger = { id: 7, name: "McDONALD'S, Hamburger", fdcId: '170717', matchedAlias: null, foodGroupId: null, foodGroupName: null, isWholeFood: null, isPrepared: true };
      search.flush([burger]);
      http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/MealPlan/prepared-check')).flush([{ ingredientId: 7, held: null, names: [] }]);
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelectorAll('[data-testid="whole-food-result-item"]').length).toBe(1);
      expect(el.querySelector('[data-testid="whole-food-result-prepared"]')!.textContent).toContain('Prepared');

      fixture.componentInstance.addWholeFood(burger);
      const post = createRequest();
      expect(post.request.body.ingredientId).toBe(7);
      expect(post.request.body.recipeId).toBeNull();
      post.flush({ id: 902, title: burger.name, notes: null });
      expect(fixture.componentInstance.entries()[0].isPrepared).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it('checks prepared meals against the household and shows why one is held', () => {
    vi.useFakeTimers();
    try {
      const fixture = create();
      fixture.componentInstance.onWholeFoodInput('entree');
      vi.advanceTimersByTime(300);

      http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/ingredients/search')).flush([
        { id: 11, name: 'Lasagna, cheese, frozen', fdcId: null, matchedAlias: null, foodGroupId: null, foodGroupName: null, isWholeFood: null, isPrepared: true },
        { id: 12, name: 'Pad thai, restaurant', fdcId: null, matchedAlias: null, foodGroupId: null, foodGroupName: null, isWholeFood: null, isPrepared: true },
        { id: 13, name: 'Rice bowl (Acme)', fdcId: null, matchedAlias: null, foodGroupId: null, foodGroupName: null, isWholeFood: null, isPrepared: true },
        { id: 14, name: 'Apples, raw', fdcId: null, matchedAlias: null, foodGroupId: null, foodGroupName: 'Fruits', isWholeFood: true, isPrepared: false },
      ]);
      const check = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/MealPlan/prepared-check'));
      expect(check.request.params.get('householdId')).toBe('5');
      expect(check.request.params.get('ingredientIds')).toBe('11,12,13');
      check.flush([
        { ingredientId: 11, held: 'contents_restricted', names: ['contains milk'] },
        { ingredientId: 12, held: 'contents_none', names: [] },
        { ingredientId: 13, held: null, names: [] },
      ]);
      fixture.detectChanges();

      const held = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="whole-food-result-held"]')).map((e) => e.textContent?.trim());
      expect(held.length).toBe(2);
      expect(held[0]).toContain('contains milk conflicts with a restriction or diet');
      expect(held[1]).toContain('its ingredients are not known');
    } finally {
      vi.useRealTimers();
    }
  });

  const lasagna = { id: 11, name: 'Lasagna, cheese, frozen', fdcId: null, matchedAlias: null, foodGroupId: null, foodGroupName: null, isWholeFood: null, isPrepared: true };

  function searchLasagna(check: { ingredientId: number; held: string | null; names: string[]; severe?: boolean }) {
    const fixture = create();
    fixture.componentInstance.onWholeFoodInput('lasagna');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/ingredients/search')).flush([lasagna]);
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/MealPlan/prepared-check')).flush([check]);
    fixture.detectChanges();
    return fixture;
  }

  it('asks a household with a severe restriction for "Add anyway", naming the reason, before it adds a held prepared meal, and adds nothing on Cancel', async () => {
    vi.useFakeTimers();
    try {
      const fixture = searchLasagna({ ingredientId: 11, held: 'contents_restricted', names: ['contains milk'], severe: true });
      fixture.componentInstance.addWholeFood(lasagna);
      fixture.detectChanges();

      expect(q('prepared-held-dialog')).toBeTruthy();
      expect(q('prepared-held-message')!.textContent).toContain('contains milk conflicts with a restriction or diet');
      http.expectNone((r) => r.method === 'POST');

      q('prepared-held-cancel-btn')!.click();
      fixture.detectChanges();
      vi.useRealTimers();
      await fixture.whenStable();

      http.expectNone((r) => r.method === 'POST');
      expect(fixture.componentInstance.entries()).toEqual([]);
      expect(fixture.componentInstance.busy()).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it('adds a held prepared meal for a severe household only after an explicit "Add anyway"', async () => {
    vi.useFakeTimers();
    try {
      const fixture = searchLasagna({ ingredientId: 11, held: 'contents_none', names: [], severe: true });
      fixture.componentInstance.addWholeFood(lasagna);
      fixture.detectChanges();
      expect(q('prepared-held-message')!.textContent).toContain('its ingredients are not known');

      q('prepared-held-add-btn')!.click();
      fixture.detectChanges();
      vi.useRealTimers();
      await fixture.whenStable();

      const post = createRequest();
      expect(post.request.body.ingredientId).toBe(11);
      post.flush({ id: 903, title: lasagna.name, notes: null });
      expect(fixture.componentInstance.entries()[0].ingredientId).toBe(11);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows the reason but asks nothing when a held prepared meal is held only for a diet', () => {
    vi.useFakeTimers();
    try {
      const fixture = searchLasagna({ ingredientId: 11, held: 'contents_restricted', names: ['contains milk'], severe: false });
      expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="whole-food-result-held"]')!.textContent).toContain('contains milk');

      fixture.componentInstance.addWholeFood(lasagna);
      fixture.detectChanges();

      expect(q('prepared-held-dialog')).toBeNull();
      createRequest().flush({ id: 904, title: lasagna.name, notes: null });
    } finally {
      vi.useRealTimers();
    }
  });

  it('checks a prepared meal whose check has not come back before it adds it', () => {
    const fixture = create();
    fixture.componentInstance.addWholeFood(lasagna);
    const check = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/MealPlan/prepared-check'));
    expect(check.request.params.get('ingredientIds')).toBe('11');
    check.flush([{ ingredientId: 11, held: 'restricted_food', names: [], severe: true }]);
    fixture.detectChanges();

    expect(q('prepared-held-dialog')).toBeTruthy();
    http.expectNone((r) => r.method === 'POST');
    q('prepared-held-cancel-btn')!.click();
  });

  it('shows how many sources agree on a branded food, and nothing for a food with its USDA record alone', () => {
    vi.useFakeTimers();
    try {
      const fixture = create();
      fixture.componentInstance.onWholeFoodInput('granola bar');
      vi.advanceTimersByTime(300);

      const search = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/ingredients/search'));
      search.flush([
        { id: 8, name: 'Oat granola bar (Acme)', fdcId: '2000002', matchedAlias: null, foodGroupId: null, foodGroupName: 'Sweets/Snacks', isWholeFood: true, isPrepared: false, agreeingSources: 3 },
        { id: 9, name: 'Nut granola bar (Acme)', fdcId: '2000003', matchedAlias: null, foodGroupId: null, foodGroupName: 'Sweets/Snacks', isWholeFood: true, isPrepared: false, agreeingSources: 1 },
      ]);
      fixture.detectChanges();

      const badges = (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="whole-food-result-sources"]');
      expect(badges.length).toBe(1);
      expect(badges[0].textContent).toContain('3 sources agree');
    } finally {
      vi.useRealTimers();
    }
  });

  it('routes "Surprise me" through the same check', () => {
    const fixture = create();
    fixture.componentInstance.surpriseMe();
    http.expectOne((r) => r.method === 'GET' && r.url.includes('/random')).flush({ results: [parmesan] });
    dietRequest().flush([celiac]);
    fixture.detectChanges();

    expect(q('diet-conflict-dialog')).toBeTruthy();
    http.expectNone((r) => r.method === 'POST');
    q('diet-conflict-cancel-btn')!.click();
  });
});
