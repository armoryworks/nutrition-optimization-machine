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
