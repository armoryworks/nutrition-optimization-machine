import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Profile, ProfileFormData } from './profile.component';
import { ReferenceService } from '../core/services/reference.service';
import { ReferenceDiscriminator } from '../core/models/reference-discriminator.model';
import { ReferenceItem } from '../core/models/reference-item.model';

const attributeType = (referenceId: number, referenceName: string): ReferenceItem => ({
  referenceId,
  referenceName,
  referenceDescription: null,
  groupId: 0,
  groupName: '',
  groupDescription: null,
});

const UNIT_SYSTEM_ID = 30;

describe('Profile (wizard)', () => {
  function create(forSelf: boolean) {
    TestBed.configureTestingModule({
      imports: [Profile],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: ReferenceService,
          useValue: {
            getReferencesBulk: () => of({
              [ReferenceDiscriminator.PersonActivityLevelType]: [],
              [ReferenceDiscriminator.PersonHealthGoalType]: [],
              [ReferenceDiscriminator.PersonAttributeType]: [
                attributeType(10, 'Height'),
                attributeType(20, 'Weight'),
                attributeType(UNIT_SYSTEM_ID, 'Unit System'),
              ],
            }),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(Profile);
    fixture.componentRef.setInput('mode', 'wizard');
    fixture.componentRef.setInput('forSelf', forSelf);
    fixture.detectChanges();
    return fixture;
  }

  function completeAsImperial(fixture: ReturnType<typeof create>): ProfileFormData {
    let emitted: ProfileFormData | undefined;
    fixture.componentInstance.stepComplete.subscribe(d => (emitted = d));
    fixture.componentInstance.onUnitSystemChange('imperial');
    fixture.componentInstance.profileForm.patchValue({ name: 'Liz', heightFeet: 5, heightInches: 6, weightLbs: 142 });
    fixture.componentInstance.submit();
    return emitted!;
  }

  it('carries the chosen unit system when the wizard is for the signed-in person', () => {
    const data = completeAsImperial(create(true));
    expect(data.attributes).toContainEqual({ attributeTypeRefId: UNIT_SYSTEM_ID, value: 'imperial' });
    expect(data.unitSystem).toBe('imperial');
  });

  it('leaves the unit system alone when the wizard is for someone else', () => {
    const data = completeAsImperial(create(false));
    expect(data.attributes.some(a => a.attributeTypeRefId === UNIT_SYSTEM_ID)).toBe(false);
  });
});

describe('Profile (wizard) invalid submit feedback', () => {
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
      imports: [Profile],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: ReferenceService,
          useValue: {
            getReferencesBulk: () => of({
              [ReferenceDiscriminator.PersonActivityLevelType]: [],
              [ReferenceDiscriminator.PersonHealthGoalType]: [],
              [ReferenceDiscriminator.PersonAttributeType]: [
                attributeType(10, 'Height'),
                attributeType(20, 'Weight'),
                attributeType(25, 'Date of Birth'),
                attributeType(UNIT_SYSTEM_ID, 'Unit System'),
              ],
            }),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(Profile);
    fixture.componentRef.setInput('mode', 'wizard');
    fixture.componentRef.setInput('forSelf', true);
    fixture.detectChanges();
    return fixture;
  }

  function fieldErrorsFor(fixture: ReturnType<typeof create>, controlName: string): string[] {
    const input = fixture.nativeElement.querySelector(`[formControlName="${controlName}"]`) as HTMLElement;
    const field = input.closest('mat-form-field')!;
    return Array.from(field.querySelectorAll('mat-error')).map((e) => (e as HTMLElement).textContent?.trim() ?? '');
  }

  function clickContinue(fixture: ReturnType<typeof create>): { emitted?: ProfileFormData; invalid: number } {
    const result: { emitted?: ProfileFormData; invalid: number } = { invalid: 0 };
    fixture.componentInstance.stepComplete.subscribe((d) => (result.emitted = d));
    fixture.componentInstance.invalidSubmit.subscribe(() => result.invalid++);
    (fixture.nativeElement.querySelector('[data-testid="profile-submit-btn"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return result;
  }

  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('explains a date of birth the browser cannot parse and blocks Continue', () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel' });
    const dob = fixture.nativeElement.querySelector('[formControlName="dateOfBirth"]') as HTMLInputElement;
    dob.value = '22/10/1990';
    dob.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const result = clickContinue(fixture);

    expect(fixture.componentInstance.profileForm.get('dateOfBirth')?.hasError('matDatepickerParse')).toBe(true);
    expect(result.emitted).toBeUndefined();
    expect(result.invalid).toBe(1);
    expect(fieldErrorsFor(fixture, 'dateOfBirth')).toEqual(['Enter a date like MM/DD/YYYY']);
  });

  it('keeps a date of birth typed as YYYY-MM-DD on the same calendar day', () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel' });
    const dob = fixture.nativeElement.querySelector('[formControlName="dateOfBirth"]') as HTMLInputElement;
    dob.value = '1980-10-22';
    dob.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const result = clickContinue(fixture);

    expect(result.emitted?.attributes).toContainEqual({ attributeTypeRefId: 25, value: '1980-10-22' });
  });

  it('rejects a date of birth in the future with a message', () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel' });
    const dob = fixture.nativeElement.querySelector('[formControlName="dateOfBirth"]') as HTMLInputElement;
    dob.value = '10/22/2999';
    dob.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const result = clickContinue(fixture);

    expect(result.emitted).toBeUndefined();
    expect(fieldErrorsFor(fixture, 'dateOfBirth')).toEqual(['Date of birth must be in the past']);
  });

  it('shows the inch range when 12 inches is entered and blocks Continue', () => {
    const fixture = create();
    fixture.componentInstance.onUnitSystemChange('imperial');
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel', heightFeet: 5, heightInches: 12, weightLbs: 180 });
    fixture.detectChanges();

    const result = clickContinue(fixture);

    expect(fixture.componentInstance.profileForm.get('heightInches')?.hasError('max')).toBe(true);
    expect(result.emitted).toBeUndefined();
    expect(fieldErrorsFor(fixture, 'heightInches')).toEqual(['Between 0 and 11 in']);
  });

  it('converts 182 cm to 6 ft 0 in, never 5 ft 12 in, so Continue still works', () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel', heightCm: 182 });
    fixture.componentInstance.onUnitSystemChange('imperial');
    fixture.detectChanges();

    const result = clickContinue(fixture);

    const value = fixture.componentInstance.profileForm.getRawValue();
    expect(value.heightFeet).toBe(6);
    expect(value.heightInches).toBe(0);
    expect(result.emitted).toBeDefined();
  });

  it('clears the hidden unit system\'s fields when switching, so they cannot block Continue', () => {
    const fixture = create();
    fixture.componentInstance.onUnitSystemChange('imperial');
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel', heightFeet: 5, heightInches: 12 });
    fixture.componentInstance.onUnitSystemChange('metric');
    fixture.detectChanges();

    const result = clickContinue(fixture);

    const value = fixture.componentInstance.profileForm.getRawValue();
    expect(value.heightCm).toBe(183);
    expect(value.heightInches).toBeNull();
    expect(result.emitted).toBeDefined();
  });

  it('opens Less Common and shows the range when RMR is out of range', async () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: 'Daniel', rmr: 300 });
    expect(fixture.componentInstance.lessCommonExpanded()).toBe(false);

    const result = clickContinue(fixture);
    await settle();
    fixture.detectChanges();

    expect(result.emitted).toBeUndefined();
    expect(fixture.componentInstance.lessCommonExpanded()).toBe(true);
    expect(fieldErrorsFor(fixture, 'rmr')).toEqual(['Between 500 and 10,000 kcal/day']);
  });

  it('scrolls the first invalid field into view and focuses it', async () => {
    const fixture = create();
    fixture.componentInstance.profileForm.patchValue({ name: '' });

    clickContinue(fixture);
    await settle();

    expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    const name = fixture.nativeElement.querySelector('[data-testid="profile-name-input"]') as HTMLInputElement;
    expect(document.activeElement).toBe(name);
    expect(fieldErrorsFor(fixture, 'name')).toEqual(['Name is required']);
  });
});
