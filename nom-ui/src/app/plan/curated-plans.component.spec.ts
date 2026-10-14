import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { CuratedPlans } from './curated-plans.component';

describe('CuratedPlans', () => {
  let http: HttpTestingController;

  function create(plans: unknown[]) {
    TestBed.configureTestingModule({
      imports: [CuratedPlans],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(CuratedPlans);
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/Plan/curated')).flush(plans);
    fixture.detectChanges();
    return fixture;
  }

  const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  afterEach(() => http.verify());

  it('gives the empty state a way back to the meal plan', () => {
    const el = create([]).nativeElement as HTMLElement;
    expect(q(el, 'curated-plans-empty')).toBeTruthy();
    expect(q(el, 'curated-plans-go-to-plan-btn')!.getAttribute('href')).toBe('/plan');
    expect(q(el, 'curated-plans-list')).toBeNull();
  });

  it('lists curated plans with a clone action', () => {
    const el = create([
      { id: 1, name: 'Gluten-free week', description: null, authorName: 'NOM', meals: [], restrictions: [] },
    ]).nativeElement as HTMLElement;
    expect(q(el, 'curated-plans-empty')).toBeNull();
    expect(el.querySelectorAll('[data-testid="curated-plan-card"]').length).toBe(1);
    expect(q(el, 'curated-plan-clone-btn')).toBeTruthy();
  });
});
