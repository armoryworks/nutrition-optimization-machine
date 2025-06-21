import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { FormControl, Validators, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs'; // Keep finalize import

// Import all child components
import { PersonEditComponent } from '../../../person/components/person-edit/person-edit.component';
import { PersonHealthEditComponent } from '../../../person/components/person-health-edit/person-health-edit.component';
import { PlanEditComponent } from '../../../plan/components/plan-edit/plan-edit.component';
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component'; // Corrected path previously

// Import Models and Services
import { PersonModel } from '../../../person/models/person.model';
import { PersonAttributeModel } from '../../../person/models/person-attribute.model';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { OnboardingCompleteRequestModel } from '../../models/onboarding-complete-request.model';
import { PersonService } from '../../../person/services/person.service';
import { NotificationService } from '../../../utilities/services/notification.service'; // For snackbar
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
  @Input() principalPersonId: number | null = null;
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

  // Define steps and their properties (rest of the array remains the same)
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

  // FormControl for UI-only steps that use it (e.g., Invitation Code, Number of Participants)
  // These were missing in your previous .ts, adding them here to avoid potential errors in template
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
    // if (this.principalPersonId) {
    //   this.onboardingData.personId = this.principalPersonId;
    //   this.allPersonsInPlan.push(
    //     new PersonModel({ id: this.principalPersonId, name: 'You (Primary)' })
    //   );
    // } else {
    //   this.error = 'User not identified for onboarding. Please log in again.';
    //   console.error(this.error);
    // }
  }

  // --- NEW GETTER FOR FILTERING RESTRICTIONS ---
  get filteredRestrictionsForCurrentPerson(): RestrictionModel[] {
    // Return an empty array if restrictions are null/undefined or if currentPersonId is not set
    if (
      !this.onboardingData.restrictions ||
      this.currentRestrictionPersonId === null ||
      this.currentRestrictionPersonId === undefined
    ) {
      return [];
    }
    // Filter the restrictions based on the current person's ID
    return this.onboardingData.restrictions.filter(
      (r) => r.personId === this.currentRestrictionPersonId
    );
  }
  // --- END NEW GETTER ---

  get currentStep(): any {
    if (
      this.currentStepIndex ===
        this.workflowSteps.findIndex((s) => s.id === 'summary') &&
      this.currentAdditionalParticipantIndex < this.allPersonsInPlan.length
    ) {
      return {
        id: 'restrictionCollectionPerPerson',
        title:
          'Restrictions for ' +
          (this.allPersonsInPlan[this.currentAdditionalParticipantIndex]
            ?.name || `Person ${this.currentAdditionalParticipantIndex + 1}`),
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
    const principalPerson = this.allPersonsInPlan.find(
      (p) => p.id === this.principalPersonId
    );
    if (principalPerson) {
      principalPerson.name = person.name;
    }
    this.nextStep();
  }

  onHealthAttributesSubmitted(attributes: PersonAttributeModel[]): void {
    this.onboardingData.attributes = attributes;
    this.nextStep();
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

    this.nextStep();
  }

  onInvitationCodeEntered(code: string): void {
    this.onboardingData.planInvitationCode = code;
    this.nextStep();
  }

  onYesNoAnswer(propertyName: string, answer: boolean): void {
    (this.onboardingData as any)[propertyName] = answer;
    if (propertyName === 'hasAdditionalParticipants' && answer === false) {
      this.currentStepIndex = this.workflowSteps.findIndex(
        (s) => s.id === 'summary'
      );
      this.onboardingData.numberOfAdditionalParticipants = 0;
      this.onboardingData.additionalParticipantDetails = [];
      this.allPersonsInPlan = [this.allPersonsInPlan[0]];
    } else {
      if (propertyName === 'hasAdditionalParticipants' && answer === true) {
        if (
          !this.allPersonsInPlan.some((p) => p.id === this.principalPersonId)
        ) {
          this.allPersonsInPlan.unshift(
            new PersonModel({
              id: this.principalPersonId || 0,
              name: 'You (Primary)',
            })
          );
        }
      }
    }
    this.nextStep();
  }

  onNumberInput(propertyName: string, value: number): void {
    (this.onboardingData as any)[propertyName] = value;
    const newParticipantsCount = value;
    const existingParticipantsCount =
      this.onboardingData.additionalParticipantDetails.length;

    if (newParticipantsCount > existingParticipantsCount) {
      for (let i = existingParticipantsCount; i < newParticipantsCount; i++) {
        this.onboardingData.additionalParticipantDetails.push(
          new PersonModel({
            id: 0,
            name: `Person ${this.allPersonsInPlan.length + 1}`,
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

    this.nextStep();
  }

  onNamesEntered(persons: PersonModel[]): void {
    this.onboardingData.additionalParticipantDetails = persons;
    this.allPersonsInPlan = [this.allPersonsInPlan[0], ...persons];
    this.nextStep();
  }

  nextStep(): void {
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
    if (
      this.currentStepIndex === summaryStepIndex &&
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson
    ) {
      if (
        this.currentAdditionalParticipantIndex < this.allPersonsInPlan.length
      ) {
        this.currentAdditionalParticipantIndex++;
        this.currentStepIndex = this.workflowSteps.findIndex(
          (s) => s.id === 'societalRestrictions'
        );
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
    if (
      this.currentStepIndex >=
        this.workflowSteps.findIndex((s) => s.id === 'societalRestrictions') &&
      this.currentStepIndex < summaryStepIndex &&
      this.onboardingData.hasAdditionalParticipants &&
      this.currentAdditionalParticipantIndex > 0
    ) {
      this.currentAdditionalParticipantIndex--;
      this.currentStepIndex = this.workflowSteps.findIndex(
        (s) => s.id === 'personalPreferences'
      );
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
      this.nextStep();
    }
  }

  submitOnboardingData(): void {
    if (this.isSubmitting) return;

    this.isSubmitting = true;
    this.error = null;
    this.submitMessage = null;

    this.onboardingData.personId = this.principalPersonId || 0;

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

  updateAdditionalParticipantName(index: number, control: any): void {
    console.log(control);

    // if (this.onboardingData.additionalParticipantDetails[index]) {
    //   this.onboardingData.additionalParticipantDetails[index].name = 'bob';
    //   this.allPersonsInPlan[index + 1].name = 'bob';
    // }
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
