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

  it('explains an empty label by the ingredients that could not be counted', () => {
    const el = render([], [{ name: '2 Cup Black Beans', reason: 'no-data' }]);

    expect(el.textContent).toContain('not yet available');
    expect(el.querySelector('[data-testid="nutrition-label-uncounted-item"]')?.textContent).toContain('Black Beans');
  });
});
