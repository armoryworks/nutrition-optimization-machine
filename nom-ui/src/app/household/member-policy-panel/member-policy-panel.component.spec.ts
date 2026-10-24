import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { MemberPolicyPanel } from './member-policy-panel.component';
import { PolicyService } from '../../core/services/policy.service';
import { PersonService } from '../../core/services/person.service';

describe('MemberPolicyPanel restrictions', () => {
  function create(onboarding: Observable<unknown>) {
    TestBed.configureTestingModule({
      imports: [MemberPolicyPanel],
      providers: [
        provideNoopAnimations(),
        {
          provide: PolicyService,
          useValue: {
            getMemberPolicy: vi.fn(() => of({ householdId: 3, personId: 12, featureGates: {}, frequencyCaps: [], curatedOnly: false, updatedBy: null })),
          },
        },
        { provide: PersonService, useValue: { getOnboardingState: vi.fn(() => onboarding) } },
      ],
    });
    const fixture = TestBed.createComponent(MemberPolicyPanel);
    fixture.componentRef.setInput('householdId', 3);
    fixture.componentRef.setInput('members', [{ id: 1, personId: 12, personName: 'Tobi' }]);
    fixture.detectChanges();
    return fixture;
  }

  it('lists the member\'s real restrictions so a steward can lock them', () => {
    const fixture = create(of({ restrictions: [{ id: 5, name: 'Peanut Allergy', restrictionTypeId: 1, locked: false }] }));
    fixture.componentInstance.onMemberChange(12);
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelectorAll('[data-testid="member-policy-restriction-item"]').length).toBe(1);
    expect(host.querySelector('[data-testid="member-policy-lock-btn"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="member-policy-restrictions-empty"]')).toBeNull();
  });

  it('says the restrictions could not be loaded instead of claiming there are none', () => {
    const fixture = create(throwError(() => new HttpErrorResponse({ status: 403 })));
    fixture.componentInstance.onMemberChange(12);
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('[data-testid="member-policy-restrictions-error"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="member-policy-restrictions-empty"]')).toBeNull();
  });
});
