import { TestBed } from '@angular/core/testing';
import { NutritionLabel } from './nutrition-label.component';

describe('NutritionLabel', () => {
  function render(nutrition: { nutrientName: string; amount: number; unit: string }[], uncounted: { name: string; reason: 'no-data' | 'no-conversion' }[]) {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', nutrition);
    fixture.componentRef.setInput('uncounted', uncounted);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('says the totals are low and names each ingredient that was not counted', () => {
    const el = render(
      [{ nutrientName: 'Energy', amount: 55, unit: 'kcal' }],
      [{ name: '2 Cup Black Beans', reason: 'no-conversion' }, { name: '1 Piece Mystery Spice', reason: 'no-data' }],
    );

    const notice = el.querySelector('[data-testid="nutrition-label-uncounted"]')!;
    expect(notice.textContent).toContain('2 ingredients are not counted');
    const items = Array.from(el.querySelectorAll('[data-testid="nutrition-label-uncounted-item"]')).map(li => li.textContent);
    expect(items[0]).toContain('2 Cup Black Beans');
    expect(items[0]).toContain('grams or ounces');
    expect(items[1]).toContain('no nutrition data');
  });

  it('shows no notice when every ingredient counted', () => {
    const el = render([{ nutrientName: 'Energy', amount: 300, unit: 'kcal' }], []);

    expect(el.querySelector('[data-testid="nutrition-label-uncounted"]')).toBeNull();
  });

  it('shows a % Daily Value only where the API gave one and marks a partial nutrient', () => {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', [
      { nutrientName: 'Saturated Fat', amount: 3, unit: 'g', dailyValuePercent: 15 },
      { nutrientName: 'Fat', amount: 9.5, unit: 'g', dailyValuePercent: 12 },
      { nutrientName: 'Sodium', amount: 420, unit: 'mg', dailyValuePercent: null, partial: true },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const rows = Array.from(el.querySelectorAll('[data-testid="nutrition-label-nutrient-item"]'));
    const fat = rows.find(r => r.textContent!.includes('Total Fat'))!;
    expect(fat.textContent).toContain('9.5g');
    expect(fat.querySelector('[data-testid="nutrition-label-dv"]')?.textContent).toContain('12%');
    const sodium = rows.find(r => r.textContent!.includes('Sodium'))!;
    expect(sodium.querySelector('[data-testid="nutrition-label-dv"]')).toBeNull();
    expect(sodium.querySelector('[data-testid="nutrition-label-partial-mark"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="nutrition-label-partial-note"]')?.textContent).toContain('minimum');
  });

  it('marks partial calories and never lets a fat subtype stand in for total fat', () => {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', [
      { nutrientName: 'Calories', amount: 310, unit: 'kcal', partial: true },
      { nutrientName: 'Saturated Fat', amount: 3, unit: 'g', dailyValuePercent: 15 },
      { nutrientName: 'Trans Fat', amount: 0.1, unit: 'g' },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="nutrition-label-calories-partial-mark"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="nutrition-label-partial-note"]')).not.toBeNull();
    const rows = Array.from(el.querySelectorAll('[data-testid="nutrition-label-nutrient-item"]')).map(r => r.textContent ?? '');
    expect(rows.some(t => t.includes('Total Fat'))).toBe(false);
    expect(rows.some(t => t.includes('Saturated Fat'))).toBe(true);
  });

  it('lists vitamins and minerals in the vitamins view, small amounts to two decimals', () => {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', [
      { nutrientName: 'Zinc', amount: 1.16, unit: 'mg', dailyValuePercent: 11 },
      { nutrientName: 'Vitamin C', amount: 31.25, unit: 'mg', dailyValuePercent: 35 },
      { nutrientName: 'Vitamin B12', amount: 0.24, unit: 'µg', partial: true },
      { nutrientName: 'Vitamin D', amount: 2, unit: 'µg', dailyValuePercent: 10 },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('[data-testid="nutrition-view-micros-btn"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const rows = Array.from(el.querySelectorAll('[data-testid="nutrition-label-nutrient-item"]')).map(r => (r.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(rows.length).toBe(4);
    expect(rows[0]).toContain('Vitamin D 2µg');
    expect(rows[1]).toContain('Vitamin C 31.3mg');
    expect(rows[1]).toContain('35%');
    expect(rows[2]).toContain('Vitamin B12 0.24µg');
    expect(rows[2]).toContain('†');
    expect(rows[3]).toContain('Zinc 1.2mg');
  });

  it('names where the numbers come from', () => {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', [{ nutrientName: 'Calories', amount: 300, unit: 'kcal' }]);
    fixture.componentRef.setInput('source', { kind: 'usda', usdaLines: 4, countedLines: 4 });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="nutrition-label-source"]')?.textContent).toContain('USDA FoodData Central');

    fixture.componentRef.setInput('source', { kind: 'mixed', usdaLines: 3, countedLines: 5 });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="nutrition-label-source"]')?.textContent).toContain('3 of 5 counted ingredients');

    fixture.componentRef.setInput('source', { kind: 'recipe', usdaLines: 0, countedLines: 0 });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="nutrition-label-source"]')?.textContent).toContain('entered with this recipe');
    expect(el.querySelector('[data-testid="nutrition-label-partial-note"]')).toBeNull();

    fixture.componentRef.setInput('source', null);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="nutrition-label-source"]')).toBeNull();
  });

  it('explains an empty label by the ingredients that could not be counted', () => {
    const el = render([], [{ name: '2 Cup Black Beans', reason: 'no-data' }]);

    expect(el.textContent).toContain('not yet available');
    expect(el.querySelector('[data-testid="nutrition-label-uncounted-item"]')?.textContent).toContain('Black Beans');
  });

  function renderAlcohol(alcohol: Record<string, unknown> | null) {
    const fixture = TestBed.createComponent(NutritionLabel);
    fixture.componentRef.setInput('nutrition', [{ nutrientName: 'Calories', amount: 300, unit: 'kcal' }]);
    fixture.componentRef.setInput('alcohol', alcohol);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const wine = {
    drinks: ['Dry White Wine'], flavourings: [], uncounted: [], asWrittenGrams: 3.04, retainedGrams: 1.06, flavouringGrams: 0,
    perServing: true, basis: 'timed', method: 'simmer', minutes: 30, retainedPercent: 35, usdaCode: '5005', notRead: false,
  };

  it('says how much alcohol stays after simmering, from the USDA retention table', () => {
    const el = renderAlcohol(wine);

    const note = el.querySelector('[data-testid="nutrition-alcohol-note"]')!;
    expect(note.querySelector('[data-testid="nutrition-alcohol-note-drinks"]')?.textContent).toContain('Contains alcohol: Dry White Wine. As written, about 3 g alcohol per serving.');
    expect(note.querySelector('[data-testid="nutrition-alcohol-note-cooking"]')?.textContent)
      .toContain('Simmered about 30 minutes, so about 35% stays (USDA retention table): about 1.1 g per serving. Calories count only the alcohol that stays.');
  });

  it('says "up to" when no cooking time is given, and all of it when nothing is heated', () => {
    let el = renderAlcohol({ ...wine, basis: 'up-to', minutes: null, retainedPercent: 85, retainedGrams: 2.58 });
    expect(el.querySelector('[data-testid="nutrition-alcohol-note-cooking"]')?.textContent).toContain('Cooking time not stated, so up to 85% may stay');

    el = renderAlcohol({ ...wine, basis: 'no-heat', method: null, minutes: null, retainedPercent: 100, retainedGrams: 3.04 });
    expect(el.querySelector('[data-testid="nutrition-alcohol-note-cooking"]')?.textContent).toContain('Not cooked after the alcohol is added, so all of it stays.');
  });

  it('gives a flavouring only a note and names a drink line it cannot count', () => {
    const el = renderAlcohol({ ...wine, drinks: [], asWrittenGrams: 0, retainedGrams: 0, flavourings: ['Vanilla Extract'], flavouringGrams: 0.2, uncounted: ['a glass of sherry'], basis: null, retainedPercent: null });

    expect(el.querySelector('[data-testid="nutrition-alcohol-note-drinks"]')).toBeNull();
    expect(el.querySelector('[data-testid="nutrition-alcohol-note-flavouring"]')?.textContent).toContain('Vanilla Extract adds a trace of alcohol (under 0.5 g per serving).');
    expect(el.querySelector('[data-testid="nutrition-alcohol-note-uncounted"]')?.textContent).toContain('“a glass of sherry”');
  });

  it('shows no alcohol note when no line carries alcohol', () => {
    const el = renderAlcohol(null);

    expect(el.querySelector('[data-testid="nutrition-alcohol-note"]')).toBeNull();
  });
});
