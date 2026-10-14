import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Onboarding } from './onboarding.component';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';
import { HouseholdStore } from '../core/services/household-store';
import { UnitPreferenceService } from '../core/services/unit-preference.service';

describe('Onboarding', () => {
  const calls: string[] = [];
  let personService: {
    getOnboardingState: ReturnType<typeof vi.fn>;
    saveProfile: ReturnType<typeof vi.fn>;
    completeOnboarding: ReturnType<typeof vi.fn>;
  };
  let householdStore: { invalidate: ReturnType<typeof vi.fn> };
  let unitPreference: { set: ReturnType<typeof vi.fn> };

  function create() {
    calls.length = 0;
    personService = {
      getOnboardingState: vi.fn(() => of({ isComplete: false, currentStep: 0, attributes: [], restrictions: [] })),
      saveProfile: vi.fn(() => of({})),
      completeOnboarding: vi.fn(() => of({})),
    };
    householdStore = { invalidate: vi.fn(() => calls.push('invalidate')) };
    unitPreference = { set: vi.fn() };

    TestBed.configureTestingModule({
      imports: [Onboarding],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            personId: signal(7),
            isLoggedIn: signal(true),
            ensurePersonId: () => of(7),
            refreshClaims: () => of(null),
          },
        },
        { provide: PersonService, useValue: personService },
        { provide: HouseholdStore, useValue: householdStore },
        { provide: UnitPreferenceService, useValue: { ...unitPreference, system: signal('metric') } },
      ],
    });
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockImplementation(() => {
      calls.push('navigate');
      return Promise.resolve(true);
    });
    const fixture = TestBed.createComponent(Onboarding);
    fixture.detectChanges();
    return fixture;
  }

  it('drops the cached household list before landing on the dashboard after "Just me"', () => {
    const fixture = create();
    fixture.componentInstance.householdMode.set('solo');
    fixture.componentInstance.completeOnboarding();

    expect(personService.completeOnboarding).toHaveBeenCalledWith(7, expect.objectContaining({ createSoloHousehold: true }));
    expect(calls).toEqual(['invalidate', 'navigate']);
  });

  it('applies the unit system chosen in the profile step once it is saved', () => {
    const fixture = create();
    fixture.componentInstance.onProfileComplete({
      personDetails: { id: 0, name: 'Liz', attributes: [] },
      attributes: [{ attributeTypeRefId: 30, value: 'imperial' }],
      unitSystem: 'imperial',
    });

    expect(personService.saveProfile).toHaveBeenCalledWith(7, expect.objectContaining({
      attributes: [{ attributeTypeRefId: 30, value: 'imperial' }],
    }));
    expect(unitPreference.set).toHaveBeenCalledWith('imperial');
  });
});
