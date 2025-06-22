import {
  Component,
  OnInit,
  Output,
  EventEmitter,
  ViewEncapsulation,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { FormControl, Validators, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';

// Import all child components for ViewChild references
import { PersonEditComponent } from '../../../person/components/person-edit/person-edit.component';
import { PersonHealthEditComponent } from '../../../person/components/person-health-edit/person-health-edit.component';
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component';

// Import Models and Services
import { PersonModel } from '../../../person/models/person.model';
import { PersonAttributeModel } from '../../../person/models/person-attribute.model';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { OnboardingCompleteRequestModel } from '../../models/onboarding-complete-request.model';
import { PersonService } from '../../../person/services/person.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RestrictionTypeEnum } from '../../../restriction/enums/restriction-type.enum';

@Component({
  selector: 'app-onboarding-workflow',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    PersonEditComponent,
    PersonHealthEditComponent,
    RestrictionEditComponent,
  ],
  templateUrl: './onboarding-workflow.component.html',
  styleUrls: ['./onboarding-workflow.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class OnboardingWorkflowComponent implements OnInit {
  private _principalPersonId: number | null = null;

  @Output() onboardingComplete = new EventEmitter<boolean>();

  currentStepIndex: number = 0;
  isLoading: boolean = false;
  isSubmitting: boolean = false;
  error: string | null = null;
  submitMessage: string | null = null;

  onboardingData: OnboardingCompleteRequestModel =
    new OnboardingCompleteRequestModel();

  currentAdditionalParticipantIndex: number = 0;
  allPersonsInPlan: PersonModel[] = [];

  @ViewChild(PersonEditComponent) personEditComponent?: PersonEditComponent;
  @ViewChild(PersonHealthEditComponent)
  personHealthEditComponent?: PersonHealthEditComponent;
  @ViewChild(RestrictionEditComponent)
  restrictionEditComponent?: RestrictionEditComponent;

  workflowSteps = [
    {
      id: 'personDetails',
      title: 'Your Details',
      component: 'app-person-edit',
      dataProperty: 'personDetails',
      required: true,
    },
    {
      id: 'healthAttributes',
      title: 'Health Attributes',
      component: 'app-person-health-edit',
      dataProperty: 'attributes',
      required: false,
    },
    {
      id: 'societalRestrictions',
      title: 'Dietary Practices',
      component: 'app-restriction-edit',
      dataProperty: 'restrictions',
      required: false,
      restrictionType: RestrictionTypeEnum.SocietalReligiousEthical,
    },
    {
      id: 'medicalRestrictions',
      title: 'Medical Restrictions',
      component: 'app-restriction-edit',
      dataProperty: 'restrictions',
      required: false,
      restrictionType: RestrictionTypeEnum.AllergyMedical,
    },
    {
      id: 'personalPreferences',
      title: 'Personal Preferences',
      component: 'app-restriction-edit',
      dataProperty: 'restrictions',
      required: false,
      restrictionType: RestrictionTypeEnum.PersonalPreference,
    },
    {
      id: 'invitationCode',
      title: 'Invitation Code',
      component: 'ui-only-invitation-code',
      dataProperty: 'planInvitationCode',
      required: false,
    },
    {
      id: 'hasAdditionalParticipants',
      title: 'Additional Participants',
      component: 'ui-only-yes-no',
      dataProperty: 'hasAdditionalParticipants',
      required: true,
    },
    {
      id: 'numberOfAdditionalParticipants',
      title: 'How Many People?',
      component: 'ui-only-number-input',
      dataProperty: 'numberOfAdditionalParticipants',
      required: true,
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true,
    },
    {
      id: 'additionalParticipantNames',
      title: 'Participant Names',
      component: 'ui-only-name-slots',
      dataProperty: 'additionalParticipantDetails',
      required: true,
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true &&
        data.numberOfAdditionalParticipants > 0,
    },
    {
      id: 'applyIndividualPreferences',
      title: 'Individual Preferences',
      component: 'ui-only-yes-no',
      dataProperty: 'applyIndividualPreferencesToEachPerson',
      required: true,
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true &&
        data.numberOfAdditionalParticipants > 0,
    },
    {
      id: 'summary',
      title: 'Review & Submit',
      component: 'ui-only-summary',
      dataProperty: null,
      required: true,
    },
  ];

  invitationCodeFormControl = new FormControl<string | null>(
    null,
    Validators.minLength(1)
  );
  numberOfAdditionalParticipantsControl = new FormControl<number | null>(
    null,
    Validators.min(0)
  );

  constructor(
    private personService: PersonService,
    private notificationService: NotificationService
  ) {}

  ngOnInit(): void {
    this._principalPersonId = -1;
    this.onboardingData.personId = this._principalPersonId;

    this.allPersonsInPlan.push(
      new PersonModel({ id: this._principalPersonId, name: 'You (Primary)' })
    );
  }

  get filteredRestrictionsForCurrentPerson(): RestrictionModel[] {
    if (
      !this.onboardingData.restrictions ||
      this.currentRestrictionPersonId === null ||
      this.currentRestrictionPersonId === undefined
    ) {
      return [];
    }
    return this.onboardingData.restrictions.filter(
      (r) => r.personId === this.currentRestrictionPersonId
    );
  }

  get currentStep(): any {
    if (
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson &&
      this.currentAdditionalParticipantIndex < this.allPersonsInPlan.length &&
      this.currentStepIndex >=
        this.workflowSteps.findIndex((s) => s.id === 'societalRestrictions') &&
      this.currentStepIndex <
        this.workflowSteps.findIndex((s) => s.id === 'summary')
    ) {
      return {
        id: 'restrictionCollectionPerPerson',
        title: `Restrictions for ${
          this.allPersonsInPlan[this.currentAdditionalParticipantIndex]?.name ||
          `Person ${this.currentAdditionalParticipantIndex + 1}`
        }`,
        component: 'app-restriction-edit',
        dataProperty: 'restrictions',
        required: false,
      };
    }
    return this.workflowSteps[this.currentStepIndex];
  }

  get currentStepTitle(): string {
    return this.currentStep?.title || 'Loading...';
  }

  isSubmitStep(): boolean {
    return (
      this.currentStep?.id === 'summary' &&
      this.currentAdditionalParticipantIndex >= this.allPersonsInPlan.length
    );
  }

  isSkippable(): boolean {
    return (
      !this.currentStep?.required &&
      this.currentStep?.id !== 'summary' &&
      this.currentStep?.id !== 'hasAdditionalParticipants'
    );
  }

  get currentRestrictionPersonId(): number {
    return (
      this.allPersonsInPlan[this.currentAdditionalParticipantIndex]?.id || 0
    );
  }

  onPersonDetailsSubmitted(person: PersonModel): void {
    this.onboardingData.personDetails = person;
    const principalPersonInList = this.allPersonsInPlan.find(
      (p) => p.id === this._principalPersonId
    );
    if (principalPersonInList) {
      principalPersonInList.name = person.name;
    }
    this.nextStepInternal(); // Call internal nextStep
  }

  onHealthAttributesSubmitted(attributes: PersonAttributeModel[]): void {
    this.onboardingData.attributes = attributes;
    this.nextStepInternal(); // Call internal nextStep
  }

  onRestrictionsSubmitted(restrictions: RestrictionModel[]): void {
    const currentPersonIdForRestrictions =
      this.allPersonsInPlan[this.currentAdditionalParticipantIndex].id;

    this.onboardingData.restrictions = this.onboardingData.restrictions.filter(
      (r) =>
        r.personId !== currentPersonIdForRestrictions &&
        r.planId !== currentPersonIdForRestrictions
    );

    this.onboardingData.restrictions.push(...restrictions);

    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );

    if (
      this.currentStepIndex === lastRestrictionStepIndex ||
      (this.currentStep.id === 'restrictionCollectionPerPerson' &&
        this.currentStepIndex === lastRestrictionStepIndex)
    ) {
      if (
        this.onboardingData.hasAdditionalParticipants &&
        this.onboardingData.applyIndividualPreferencesToEachPerson &&
        this.currentAdditionalParticipantIndex <
          this.allPersonsInPlan.length - 1
      ) {
        this.currentAdditionalParticipantIndex++;
        this.currentStepIndex = this.workflowSteps.findIndex(
          (s) => s.id === 'societalRestrictions'
        );
        this.focusOnCurrentStep();
        return;
      }
    }
    this.nextStepInternal(); // Call internal nextStep
  }

  // RE-ADDED: onInvitationCodeEntered
  onInvitationCodeEntered(code: string): void {
    this.onboardingData.planInvitationCode = code;
    this.nextStepInternal();
  }

  // RE-ADDED: onYesNoAnswer
  onYesNoAnswer(propertyName: string, answer: boolean): void {
    (this.onboardingData as any)[propertyName] = answer;

    if (propertyName === 'hasAdditionalParticipants') {
      if (answer === false) {
        this.currentStepIndex = this.workflowSteps.findIndex(
          (s) => s.id === 'summary'
        );
        this.onboardingData.numberOfAdditionalParticipants = 0;
        this.onboardingData.additionalParticipantDetails = [];
        this.allPersonsInPlan = [this.allPersonsInPlan[0]];
        this.currentAdditionalParticipantIndex = 0;
        this.nextStepInternal();
      } else {
        if (
          !this.allPersonsInPlan.some((p) => p.id === this._principalPersonId)
        ) {
          this.allPersonsInPlan.unshift(
            new PersonModel({
              id: this._principalPersonId || 0,
              name: 'You (Primary)',
            })
          );
        }
        this.nextStepInternal();
      }
    } else if (propertyName === 'applyIndividualPreferencesToEachPerson') {
      this.nextStepInternal();
    }
  }

  // RE-ADDED: onNumberInput
  onNumberInput(propertyName: string, value: number): void {
    (this.onboardingData as any)[propertyName] = value;
    const newParticipantsCount = value;
    const existingParticipantsCount =
      this.onboardingData.additionalParticipantDetails.length;

    if (newParticipantsCount > existingParticipantsCount) {
      for (let i = existingParticipantsCount; i < newParticipantsCount; i++) {
        this.onboardingData.additionalParticipantDetails.push(
          new PersonModel({
            id: -(i + 2),
            name: `Person ${i + 2}`,
          })
        );
      }
    } else if (newParticipantsCount < existingParticipantsCount) {
      this.onboardingData.additionalParticipantDetails =
        this.onboardingData.additionalParticipantDetails.slice(
          0,
          newParticipantsCount
        );
    }

    this.allPersonsInPlan = [
      this.allPersonsInPlan[0],
      ...this.onboardingData.additionalParticipantDetails,
    ];

    this.nextStepInternal();
  }

  // RE-ADDED: onNamesEntered
  onNamesEntered(persons: PersonModel[]): void {
    this.onboardingData.additionalParticipantDetails = persons;
    persons.forEach((person, index) => {
      if (this.allPersonsInPlan[index + 1]) {
        this.allPersonsInPlan[index + 1].name = person.name;
      }
    });
    this.nextStepInternal();
  }

  // --- NEW PUBLIC METHOD TO BE CALLED BY "NEXT" BUTTON ---
  triggerChildComponentSubmission(): void {
    this.error = null;

    if (this.isSubmitStep()) {
      this.submitOnboardingData();
      return;
    }

    switch (this.currentStep?.component) {
      case 'app-person-edit':
        this.personEditComponent?.submitForm();
        break;
      case 'app-person-health-edit':
        this.personHealthEditComponent?.submitForm();
        break;
      case 'app-restriction-edit':
        this.restrictionEditComponent?.submitForm();
        break;
      case 'ui-only-invitation-code':
        this.onInvitationCodeEntered(
          this.invitationCodeFormControl.value || ''
        );
        break;
      case 'ui-only-yes-no':
        // These steps are typically handled by direct button clicks that call onYesNoAnswer.
        // If 'Next' button is used, we need to ensure the choice is captured or a default is made.
        // For now, assuming onYesNoAnswer is called directly via specific buttons.
        // If a 'Next' button is the *only* way to advance, you'd need logic here
        // to infer what 'Yes'/'No' means for the current step, or ensure a radio button group
        // is part of the UI and its value can be accessed via a FormControl.
        // For simplicity, we assume the specific 'Yes'/'No' buttons call the handler directly.
        break;
      case 'ui-only-number-input':
        this.onNumberInput(
          'numberOfAdditionalParticipants',
          this.numberOfAdditionalParticipantsControl.value || 0
        );
        break;
      case 'ui-only-name-slots':
        this.onNamesEntered(this.onboardingData.additionalParticipantDetails);
        break;
      case 'ui-only-summary':
        this.submitOnboardingData();
        break;
      default:
        // For any other UI-only steps that don't have explicit submission methods, just advance.
        this.nextStepInternal();
        break;
    }
  }

  // --- RENAMED: internal method for step progression, called by event handlers ---
  private nextStepInternal(): void {
    // Renamed from nextStep() to avoid confusion
    this.error = null;

    if (this.currentStepIndex < this.workflowSteps.length - 1) {
      this.currentStepIndex++;
      while (
        this.currentStep?.condition &&
        !this.currentStep.condition(this.onboardingData) &&
        this.currentStepIndex < this.workflowSteps.length - 1
      ) {
        this.currentStepIndex++;
      }
    }

    const summaryStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'summary'
    );
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );

    if (
      this.currentStepIndex >=
        this.workflowSteps.findIndex(
          (s) => s.id === 'applyIndividualPreferences'
        ) &&
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson &&
      this.currentAdditionalParticipantIndex < this.allPersonsInPlan.length
    ) {
      if (this.currentStep.component !== 'app-restriction-edit') {
        this.currentStepIndex = firstRestrictionStepIndex;
      }
    }

    if (
      this.currentStepIndex === summaryStepIndex &&
      (!this.onboardingData.hasAdditionalParticipants ||
        this.currentAdditionalParticipantIndex >= this.allPersonsInPlan.length)
    ) {
      this.submitOnboardingData();
    }

    this.focusOnCurrentStep();
  }

  previousStep(): void {
    this.error = null;

    const summaryStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'summary'
    );
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );
    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );

    if (
      this.currentStep?.component === 'app-restriction-edit' &&
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson &&
      this.currentAdditionalParticipantIndex >= 0
    ) {
      if (
        this.currentStepIndex === firstRestrictionStepIndex &&
        this.currentAdditionalParticipantIndex > 0
      ) {
        this.currentAdditionalParticipantIndex--;
        this.currentStepIndex = lastRestrictionStepIndex;
      } else if (this.currentStepIndex > firstRestrictionStepIndex) {
        this.currentStepIndex--;
      } else if (
        this.currentStepIndex === firstRestrictionStepIndex &&
        this.currentAdditionalParticipantIndex === 0
      ) {
        let targetIndex = this.workflowSteps.findIndex(
          (s) => s.id === 'applyIndividualPreferences'
        );
        if (targetIndex === -1) {
          targetIndex = this.workflowSteps.findIndex(
            (s) => s.id === 'additionalParticipantNames'
          );
        }
        this.currentStepIndex = targetIndex;
      } else {
        if (this.currentStepIndex > 0) {
          this.currentStepIndex--;
        }
      }
    } else if (this.currentStepIndex > 0) {
      this.currentStepIndex--;
      while (
        this.currentStep?.condition &&
        !this.currentStep.condition(this.onboardingData) &&
        this.currentStepIndex > 0
      ) {
        this.currentStepIndex--;
      }
    }
    this.focusOnCurrentStep();
  }

  skipCurrentStep(): void {
    if (this.currentStep && !this.currentStep.required) {
      const dataProp = this.currentStep.dataProperty;
      if (dataProp) {
        if (typeof (this.onboardingData as any)[dataProp] === 'boolean') {
          (this.onboardingData as any)[dataProp] = false;
        } else if (Array.isArray((this.onboardingData as any)[dataProp])) {
          (this.onboardingData as any)[dataProp] = [];
        } else if (dataProp === 'planInvitationCode') {
          (this.onboardingData as any)[dataProp] = null;
        }
      }
      this.nextStepInternal(); // Call internal nextStep
    }
  }

  submitOnboardingData(): void {
    if (this.isSubmitting) return;

    this.isSubmitting = true;
    this.error = null;
    this.submitMessage = null;

    this.onboardingData.personId = this._principalPersonId || 0;

    this.personService
      .submitOnboardingComplete(this.onboardingData)
      .pipe(
        finalize(() => {
          this.isSubmitting = false;
        })
      )
      .subscribe({
        next: (response) => {
          this.submitMessage =
            response.message || 'Onboarding completed successfully!';
          this.notificationService.success(this.submitMessage);

          if (
            response &&
            response.data &&
            typeof response.data.newPersonId === 'number'
          ) {
            const newRealPrincipalId = response.data.newPersonId;
            this._principalPersonId = newRealPrincipalId;
            this.onboardingData.personId = newRealPrincipalId;

            const primaryPersonInList = this.allPersonsInPlan.find(
              (p) => p.id === -1
            );
            if (primaryPersonInList) {
              primaryPersonInList.id = newRealPrincipalId;
            }
          }
          this.onboardingComplete.emit(true);
        },
        error: (err) => {
          console.error('Onboarding submission failed:', err);
          const errorMsg = err.error?.message || err.message || 'Unknown error';
          this.error = `Submission failed: ${errorMsg}`;
          this.submitMessage = `Submission failed: ${errorMsg}`;
          this.notificationService.error(this.submitMessage);
        },
      });
  }

  onCompletionRedirect(): void {
    this.onboardingComplete.emit(true);
  }

  updateAdditionalParticipantName(index: number, event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    const name = inputElement.value;

    if (this.onboardingData.additionalParticipantDetails[index]) {
      this.onboardingData.additionalParticipantDetails[index].name = name;
      if (this.allPersonsInPlan[index + 1]) {
        this.allPersonsInPlan[index + 1].name = name;
      }
    }
  }

  getPersonName(personId: number | null, defaultRetVal: string): string {
    return (
      this.allPersonsInPlan.find((p) => p.id === personId)?.name ||
      defaultRetVal
    );
  }

  private focusOnCurrentStep(): void {
    window.scrollTo(0, 0);
  }
}
