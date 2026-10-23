import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RecipeForm } from './recipe-form.component';
import { MeasurementService } from '../core/services/measurement.service';
import { PolicyService } from '../core/services/policy.service';

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
