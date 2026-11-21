import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RecipeDetail } from './recipe-detail.component';
import { RecipeService } from '../core/services/recipe.service';
import { RecipeModel } from '../core/models/recipe.model';

const base: RecipeModel = {
  id: 7,
  name: 'Sheet pan cake',
  description: '',
  authorName: 'NOM',
  authorId: 1,
  rating: 0,
  commentCount: 0,
  ratingCount: 0,
  createdDate: '2026-11-20T00:00:00Z',
  curationStatus: 'Approved',
  sourceUrl: 'https://www.example.com/cake',
  ingredients: [],
  steps: [],
};

async function render(recipe: RecipeModel): Promise<HTMLElement> {
  const recipeService = new Proxy({}, {
    get: (_, key) => (key === 'getRecipe' ? () => of(recipe) : () => of([])),
  });
  TestBed.configureTestingModule({
    imports: [RecipeDetail],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideNoopAnimations(),
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { params: of({ id: String(recipe.id) }) } },
      { provide: RecipeService, useValue: recipeService },
    ],
  });
  const fixture = TestBed.createComponent(RecipeDetail);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const byId = (el: HTMLElement, id: string) => el.querySelector(`[data-testid="${id}"]`);

describe('RecipeDetail withheld method', () => {
  it('shows the key facts, the source link and the note in place of the method', async () => {
    const el = await render({ ...base, proseWithheld: true, keyFacts: ['oven 350°F', 'bake 30 to 40 min'] });

    expect(byId(el, 'recipe-method-withheld')).not.toBeNull();
    const facts = Array.from(el.querySelectorAll('[data-testid="recipe-key-fact"]')).map((li) => li.textContent?.trim());
    expect(facts).toEqual(['oven 350°F', 'bake 30 to 40 min']);
    const link = byId(el, 'recipe-method-source-link') as HTMLAnchorElement;
    expect(link.textContent?.trim()).toBe('Full method at example.com');
    expect(link.getAttribute('href')).toBe('https://www.example.com/cake');
    expect(byId(el, 'recipe-method-withheld-note')?.textContent).toContain('in our own words');
    expect(el.querySelector('.nom-recipe__steps li[data-testid="step-altered"]')).toBeNull();
    expect(el.textContent).not.toContain('No instructions listed.');
    expect(Array.from(el.querySelectorAll('[data-testid="recipe-print-key-fact"]')).length).toBe(2);
    expect(byId(el, 'recipe-print-step')).toBeNull();
  });

  it('shows the steps and no withheld view for a recipe whose method is shown', async () => {
    const el = await render({ ...base, steps: [{ order: 1, description: 'Bake the cake.' }] });

    expect(byId(el, 'recipe-method-withheld')).toBeNull();
    expect(byId(el, 'recipe-key-facts')).toBeNull();
    expect(el.textContent).toContain('Bake the cake.');
  });
});

describe('RecipeDetail assumed container sizes', () => {
  it('marks a line counted at an assumed size and hands it to the label', async () => {
    const el = await render({
      ...base,
      steps: [{ order: 1, description: 'Simmer.' }],
      ingredients: [
        {
          ingredientId: 3, name: 'Black Beans', quantity: 2, measurementId: 20, measurement: 'Can', grams: 850,
          nutritionStatus: 'counted-assumed',
          assumedSize: '1 can ≈ 14.99 oz (425 g): the most common size in USDA branded data (2025-12-18, 223 of 304 products); estimated',
        },
        { ingredientId: 4, name: 'Onion', quantity: 100, measurementId: 1, measurement: 'Gram', grams: 100, nutritionStatus: 'counted' },
      ],
    });

    const marks = Array.from(el.querySelectorAll('[data-testid="recipe-ingredient-assumed-size"]'));
    expect(marks.length).toBe(1);
    expect(marks[0].getAttribute('title')).toContain('425 g');
    expect(Array.from(el.querySelectorAll('[data-testid="ingredient-mass"]')).some((m) => m.textContent!.includes('≈'))).toBe(true);
  });
});
