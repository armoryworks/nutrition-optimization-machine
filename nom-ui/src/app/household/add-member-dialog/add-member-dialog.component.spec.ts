import { TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { AddMemberDialog, AddMemberDialogData } from './add-member-dialog.component';
import { PersonService } from '../../core/services/person.service';
import { LoadingService } from '../../core/services/loading.service';

describe('AddMemberDialog editing an existing member', () => {
  function create(data: AddMemberDialogData, onboarding: Observable<unknown>) {
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: PersonService, useValue: { getOnboardingState: vi.fn(() => onboarding) } },
        { provide: LoadingService, useValue: { loading: () => <T>(source: Observable<T>) => source } },
      ],
    });
    TestBed.overrideComponent(AddMemberDialog, { set: { template: '' } });
    const component = TestBed.createComponent(AddMemberDialog).componentInstance;
    component.ngOnInit();
    return component;
  }

  const state = {
    personDetails: { id: 12, name: 'Tobi' },
    attributes: [],
    restrictions: [{ id: 5, name: 'Peanut Allergy', restrictionTypeId: 1 }],
  };

  it('opens the dietary step with the member\'s restrictions when Dietary was clicked', () => {
    const component = create({ householdId: 3, personId: 12, personName: 'Tobi', initialStep: 1 }, of(state));

    expect(component.dataReady()).toBe(true);
    expect(component.currentStep()).toBe(1);
    expect(component.restrictionsInitialData().map(r => r.name)).toEqual(['Peanut Allergy']);
    expect(component.errorMessage()).toBe('');
  });

  it('opens the profile step filled in when Profile was clicked', () => {
    const component = create({ householdId: 3, personId: 12, personName: 'Tobi', initialStep: 0 }, of(state));

    expect(component.currentStep()).toBe(0);
    expect(component.profileInitialData()?.personDetails.name).toBe('Tobi');
  });

  it('does not offer blank, saveable forms when the member could not be loaded', () => {
    const component = create(
      { householdId: 3, personId: 12, personName: 'Tobi', initialStep: 1 },
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );

    expect(component.dataReady()).toBe(false);
    expect(component.errorMessage()).toBe('Unable to load member data.');
  });

  it('shows the empty profile step straight away when adding a new member', () => {
    const component = create({ householdId: 3 }, of(state));

    expect(component.dataReady()).toBe(true);
    expect(component.currentStep()).toBe(0);
  });
});
