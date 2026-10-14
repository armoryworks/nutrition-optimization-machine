import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Plan } from './plan.component';
import { PlanService } from '../core/services/plan.service';
import { HouseholdStore } from '../core/services/household-store';

describe('Plan curated link', () => {
  function create(curated: ReturnType<PlanService['getCuratedPlans']>) {
    TestBed.configureTestingModule({
      imports: [Plan],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        { provide: PlanService, useValue: { getCuratedPlans: () => curated } },
        { provide: HouseholdStore, useValue: { getHouseholds: () => of([]), households$reactive: of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(Plan);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const link = (el: HTMLElement) => el.querySelector('[data-testid="plan-curated-link"]');

  it('is hidden when there are no curated plans to browse', () => {
    expect(link(create(of([])))).toBeNull();
  });

  it('is hidden when curated plans cannot be loaded', () => {
    expect(link(create(throwError(() => new Error('offline'))))).toBeNull();
  });

  it('shows when curated plans exist', () => {
    const el = create(of([{ id: 1, name: 'Gluten-free week' } as never]));
    expect(link(el)!.getAttribute('href')).toBe('/plan/curated');
  });
});
