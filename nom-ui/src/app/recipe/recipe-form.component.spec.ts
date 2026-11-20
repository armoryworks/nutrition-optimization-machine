import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RecipeForm } from './recipe-form.component';
import { MeasurementService } from '../core/services/measurement.service';
import { PolicyService } from '../core/services/policy.service';
import { IngredientSearchResult } from '../core/models/ingredient-search-result.model';

function create() {
  TestBed.configureTestingModule({
    imports: [RecipeForm],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideNoopAnimations(),
      provideRouter([]),
      {
        provide: MeasurementService,
        useValue: { loadMeasurements: () => of([{ id: 1, name: 'Cup', symbol: 'cup', categoryId: 2, categoryName: 'Volume' }]) },
      },
      { provide: PolicyService, useValue: { loadOwnPolicyForPrimaryHousehold: () => undefined, isGatedPrimary: () => false } },
    ],
  });
  const fixture = TestBed.createComponent(RecipeForm);
  fixture.detectChanges();
  return fixture;
}

describe('RecipeForm invalid submit feedback', () => {
  const originalScrollIntoView = Element.prototype.scrollIntoView;
  let scrollIntoView: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView as unknown as typeof Element.prototype.scrollIntoView;
  });

  afterEach(() => {
    Element.prototype.scrollIntoView = originalScrollIntoView;
  });

  it('names the blank ingredient and scrolls to it when the row has a quantity and unit but no ingredient', async () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.recipeForm.patchValue({ name: 'Soup' });
    component.stepsArray.at(0).patchValue({ description: 'Simmer.' });
    component.ingredientsArray.at(0).patchValue({ quantity: 2, measurementId: 1 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="recipe-form-fields"]') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(component.ingredientsArray.length).toBe(1);
    expect(component.ingredientsArray.at(0).get('searchText')?.hasError('required')).toBe(true);
    expect(component.errorMessage()).toBe('Please complete the highlighted fields before saving.');
    const rowError = fixture.nativeElement.querySelector('[data-testid="recipe-ingredient-required-error"]') as HTMLElement;
    expect(rowError?.textContent).toContain('Ingredient is required');
    expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    const ingredientInput = fixture.nativeElement.querySelector('[formControlName="searchText"]') as HTMLInputElement;
    expect(document.activeElement).toBe(ingredientInput);
  });
});

describe('RecipeForm nutrition hints', () => {
  function create() {
    TestBed.configureTestingModule({
      imports: [RecipeForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: MeasurementService,
          useValue: {
            loadMeasurements: () => of([
              { id: 3, name: 'Piece', symbol: 'pc', categoryId: 3, categoryName: 'Count' },
              { id: 20, name: 'Can', symbol: 'can', categoryId: 3, categoryName: 'Count', isContainer: true },
            ]),
          },
        },
        { provide: PolicyService, useValue: { loadOwnPolicyForPrimaryHousehold: () => undefined, isGatedPrimary: () => false } },
      ],
    });
    const fixture = TestBed.createComponent(RecipeForm);
    fixture.detectChanges();
    return fixture;
  }

  it('says a can will not count even when the food has an item weight, and says nothing for a piece', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onIngredientSelected(0, { id: 7, name: 'Tomatoes', hasNutrition: true, canWeighVolume: true, canWeighCount: true });

    component.ingredientsArray.at(0).patchValue({ quantity: 1, measurementId: 3 });
    fixture.detectChanges();

    expect(component.nutritionGap(0)).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="recipe-ingredient-unit-gap"]')).toBeNull();

    component.ingredientsArray.at(0).patchValue({ measurementId: 20 });
    fixture.detectChanges();

    expect(component.nutritionGap(0)).toBe('no-conversion');
    const hint = fixture.nativeElement.querySelector('[data-testid="recipe-ingredient-unit-gap"]') as HTMLElement;
    expect(hint?.textContent).toContain('Use g or oz so it counts');
  });
});

describe('RecipeForm typed ingredient that equals the alias of a suggestion', () => {
  const originalScrollIntoView = Element.prototype.scrollIntoView;

  beforeEach(() => {
    Element.prototype.scrollIntoView = vi.fn() as unknown as typeof Element.prototype.scrollIntoView;
  });

  afterEach(() => {
    Element.prototype.scrollIntoView = originalScrollIntoView;
  });

  function submitTyped(typed: string, suggestion: IngredientSearchResult) {
    const fixture = create();
    const component = fixture.componentInstance;
    const row = component.ingredientsArray.at(0);
    row.patchValue({ searchText: typed, quantity: 1, measurementId: 1 });
    component.ingredientOptions.set(new Map([[row.get('rowId')?.value as number, [suggestion]]]));

    component.onSubmit();

    return row;
  }

  it('leaves the row unmatched when nobody has confirmed the alias', () => {
    const row = submitTyped('Kosher Salt', { id: 280659, name: 'Salt', matchedAlias: 'kosher salt', matchedAliasTrusted: false });

    expect(row.get('ingredientId')?.value).toBe(0);
    expect(row.get('searchText')?.hasError('unmatched')).toBe(true);
  });

  it('leaves the row unmatched when the server does not say whether the alias is trusted', () => {
    const row = submitTyped('kosher salt', { id: 280659, name: 'Salt', matchedAlias: 'kosher salt' });

    expect(row.get('ingredientId')?.value).toBe(0);
    expect(row.get('searchText')?.hasError('unmatched')).toBe(true);
  });

  it('selects the suggestion when a rule or a person put the alias on the food', () => {
    const row = submitTyped('Large Eggs', { id: 278540, name: 'Eggs', matchedAlias: 'large eggs', matchedAliasTrusted: true });

    expect(row.get('ingredientId')?.value).toBe(278540);
    expect(row.get('searchText')?.value).toBe('Eggs');
    expect(row.get('searchText')?.hasError('unmatched')).toBe(false);
  });

  it('selects the suggestion whose own name was typed, whatever its alias', () => {
    const row = submitTyped('salt', { id: 280659, name: 'Salt', matchedAlias: 'kosher salt', matchedAliasTrusted: false });

    expect(row.get('ingredientId')?.value).toBe(280659);
  });
});
