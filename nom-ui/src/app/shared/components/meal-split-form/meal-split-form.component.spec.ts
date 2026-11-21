import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MealSplitForm } from './meal-split-form.component';

describe('MealSplitForm prepared meals', () => {
  let http: HttpTestingController;

  function create(options: { includeDessert: boolean; preparedMealTypeIds?: number[] }) {
    TestBed.configureTestingModule({
      imports: [MealSplitForm],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MealSplitForm);
    fixture.componentRef.setInput('householdId', 7);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url.endsWith('/Household/7/meal-split'))
      .flush({ breakfastPct: 25, lunchPct: 30, dinnerPct: 35, snacksPct: 10, dessertPct: 0 });
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/Household/7/meal-plan-options')).flush(options);
    fixture.detectChanges();
    return fixture;
  }

  const box = (el: HTMLElement, testid: string) =>
    el.querySelector<HTMLInputElement>(`[data-testid="${testid}"] input`);

  afterEach(() => http.verify());

  it('shows the stored choice per meal, and Dessert only when desserts are planned', () => {
    const fixture = create({ includeDessert: false, preparedMealTypeIds: [1101] });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="meal-split-prepared"]')).not.toBeNull();
    expect(box(el, 'meal-split-prepared-lunch')!.checked).toBe(true);
    expect(box(el, 'meal-split-prepared-dinner')!.checked).toBe(false);
    expect(box(el, 'meal-split-prepared-dessert')).toBeNull();
  });

  it('saves the ticked meals with the plan options', () => {
    const fixture = create({ includeDessert: false });
    const el = fixture.nativeElement as HTMLElement;

    box(el, 'meal-split-prepared-dinner')!.click();
    box(el, 'meal-split-prepared-lunch')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="meal-split-save-btn"]')!.click();

    const save = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Household/7/meal-plan-options'));
    expect(save.request.body).toEqual({ includeDessert: false, preparedMealTypeIds: [1101, 1102] });
    save.flush({ includeDessert: false, preparedMealTypeIds: [1101, 1102] });
    http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/Household/7/meal-split')).flush({});
  });
});
