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
import {
  FormControl,
  Validators,
  ReactiveFormsModule,
  FormArray,
} from '@angular/forms';
import { finalize, of } from 'rxjs';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

// Import all child components for ViewChild references
import { PersonEditComponent } from '../../../person/components/person-edit/person-edit.component';
import { PersonHealthEditComponent } from '../../../person/components/person-health-edit/person-health-edit.component';
import { PlanEditComponent } from '../../../plan/components/plan-edit/plan-edit.component'; // Keep even if not directly used yet
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component';

// Import Models and Services
import { PersonModel } from '../../../person/models/person.model';
import { PersonAttributeModel } from '../../../person/models/person-attribute.model';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { OnboardingCompleteRequestModel } from '../../models/onboarding-complete-request.model';
import { PersonService } from '../../../person/services/person.service';
import { NotificationService } from '../../../utilities/services/notification.service';
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
    MatRadioModule,
    MatSelectModule,
    PersonEditComponent,
    PersonHealthEditComponent,
    RestrictionEditComponent,
  ],
  templateUrl: './onboarding-workflow.component.html',
  styleUrls: ['./onboarding-workflow.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class OnboardingWorkflowComponent implements OnInit {
  // _principalPersonId will be updated by the API call in Step 2 if user doesn't use invitation code
  private _principalPersonId?: number;

  public readonly NO_FLOW_STEPS: string[] = [
    'invitationCode',
    'hasAdditionalParticipants',
  ];

  @Output() onboardingComplete = new EventEmitter<boolean>();

  currentStepIndex: number = 0;
  isLoading: boolean = false;
  isSubmitting: boolean = false;
  error: string | null = null;
  submitMessage: string | null = null;

  onboardingData: OnboardingCompleteRequestModel =
    new OnboardingCompleteRequestModel();

  // New index for looping through persons for health attributes
  currentHealthAttributePersonIndex: number = 0;
  currentRestrictionPersonIndex: number = 0; // Renamed from currentAdditionalParticipantIndex for clarity in restriction loop

  // allPersonsInPlan will hold PersonModel instances, now including their attributes
  allPersonsInPlan: PersonModel[] = [];

  // Controls for restriction scope and affected persons
  restrictionScopeControl = new FormControl<'plan' | 'specific' | null>(
    null,
    Validators.required
  );
  affectedPersonIdsControl = new FormControl<number[] | null>(null);

  // Form Controls for UI-only steps
  invitationCodeFormControl = new FormControl<string | null>(null);
  numberOfAdditionalParticipantsControl = new FormControl<number | null>(null, [
    Validators.min(0),
    Validators.required,
  ]);

  @ViewChild(PersonEditComponent) personEditComponent?: PersonEditComponent;
  @ViewChild(PersonHealthEditComponent)
  personHealthEditComponent?: PersonHealthEditComponent;
  @ViewChild(RestrictionEditComponent)
  restrictionEditComponent?: RestrictionEditComponent;

  // Define the new workflow steps
  workflowSteps = [
    {
      id: 'invitationCode',
      title: 'Invitation Code',
      component: 'ui-only-invitation-code',
      required: false,
      dataProperty: 'planInvitationCode',
    },
    {
      id: 'personDetails',
      title: 'Your Details',
      component: 'app-person-edit',
      required: true,
      dataProperty: 'personDetails',
    },
    {
      id: 'hasAdditionalParticipants',
      title: 'Additional Participants',
      component: 'ui-only-yes-no',
      required: true,
      dataProperty: 'hasAdditionalParticipants',
    },
    {
      id: 'numberOfAdditionalParticipants',
      title: 'How Many People?',
      component: 'ui-only-number-input',
      required: true,
      dataProperty: 'numberOfAdditionalParticipants',
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true,
    },
    {
      id: 'additionalParticipantNames',
      title: 'Participant Names',
      component: 'ui-only-name-slots',
      required: true,
      dataProperty: 'additionalParticipantDetails',
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true &&
        data.numberOfAdditionalParticipants > 0,
    },
    {
      id: 'healthAttributes',
      title: 'Health Attributes',
      component: 'app-person-health-edit',
      required: false,
      dataProperty: 'attributes',
    }, // This step will loop
    {
      id: 'restrictionScope',
      title: 'Restriction Scope',
      component: 'ui-only-restriction-scope',
      required: true,
      dataProperty: null,
    },
    {
      id: 'selectAffectedPeople',
      title: 'Select Affected People',
      component: 'ui-only-select-people',
      required: true,
      dataProperty: null,
      condition: (
        data: OnboardingCompleteRequestModel,
        comp: OnboardingWorkflowComponent
      ) =>
        comp.restrictionScopeControl.value === 'specific' &&
        (comp.allPersonsInPlan?.length || 0) > 1,
    },
    {
      id: 'societalRestrictions',
      title: 'Dietary Practices',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.SocietalReligiousEthical,
    },
    {
      id: 'medicalRestrictions',
      title: 'Medical Restrictions',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.AllergyMedical,
    },
    {
      id: 'personalPreferences',
      title: 'Personal Preferences',
      component: 'app-restriction-edit',
      required: false,
      restrictionType: RestrictionTypeEnum.PersonalPreference,
    },
    {
      id: 'applyIndividualPreferences',
      title: 'Individual Preferences',
      component: 'ui-only-yes-no',
      required: true,
      dataProperty: 'applyIndividualPreferencesToEachPerson',
      condition: (data: OnboardingCompleteRequestModel) =>
        data.hasAdditionalParticipants === true &&
        data.numberOfAdditionalParticipants > 0,
    },
    {
      id: 'summary',
      title: 'Review & Submit',
      component: 'ui-only-summary',
      required: true,
      dataProperty: null,
    },
  ];

  constructor(
    private personService: PersonService,
    private notificationService: NotificationService
  ) {}

  ngOnInit(): void {
    // Initial setup: No principal person ID yet, it's determined in Step 2.
    // allPersonsInPlan starts empty until primary person is created/identified.
    this.allPersonsInPlan = [];

    // Default restriction scope (can be pre-selected if desired)
    this.restrictionScopeControl.setValue('specific');
    // Default affected person to primary (will be updated once primaryPersonId is known)
    this.affectedPersonIdsControl.setValue([]);
  }

  get currentStep(): any {
    const currentStepConfig = this.workflowSteps[this.currentStepIndex];

    // Logic for looping through Health Attributes for each person
    if (
      currentStepConfig.id === 'healthAttributes' &&
      this.currentHealthAttributePersonIndex < this.allPersonsInPlan.length
    ) {
      const currentPersonForHealth =
        this.allPersonsInPlan[this.currentHealthAttributePersonIndex];
      return {
        ...currentStepConfig,
        title: `Health Attributes for ${
          currentPersonForHealth?.name ||
          `Person ${this.currentHealthAttributePersonIndex + 1}`
        }`,
      };
    }

    // Logic for looping through Restrictions for each person (if applyIndividualPreferences is true)
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );
    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );

    if (
      this.currentRestrictionPersonIndex > 0 && // Only loop for additional people (primary is handled by main flow)
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson &&
      this.currentRestrictionPersonIndex < this.allPersonsInPlan.length &&
      this.currentStepIndex >= firstRestrictionStepIndex &&
      this.currentStepIndex <= lastRestrictionStepIndex
    ) {
      const currentPersonForRestriction =
        this.allPersonsInPlan[this.currentRestrictionPersonIndex];
      return {
        ...currentStepConfig,
        title: `${currentStepConfig.title} for ${
          currentPersonForRestriction?.name ||
          `Person ${this.currentRestrictionPersonIndex + 1}`
        }`,
      };
    }

    return currentStepConfig;
  }

  get currentStepTitle(): string {
    return this.currentStep?.title || 'Loading...';
  }

  isSubmitStep(): boolean {
    return this.currentStep?.id === 'summary';
  }

  isSkippable(): boolean {
    // A step is skippable if it's not required and not the summary
    // And for special UI-only steps that have explicit skip buttons or "No code" options.
    return !this.currentStep?.required && this.currentStep?.id !== 'summary';
  }

  // Gets the current PersonId for the *Health Attributes* step
  get currentHealthAttributesTargetPersonId(): number {
    return (
      this.allPersonsInPlan[this.currentHealthAttributePersonIndex]?.id || 0
    );
  }

  // Gets the current PersonId for the *Restriction* steps (which loop)
  get currentRestrictionTargetPersonId(): number {
    return this.allPersonsInPlan[this.currentRestrictionPersonIndex]?.id || 0;
  }

  // Helper to filter restrictions for the current person in the restriction loop
  get filteredRestrictionsForCurrentPerson(): RestrictionModel[] {
    const targetPersonId = this.currentRestrictionTargetPersonId;
    // If 'plan' scope, show all plan-wide restrictions
    if (this.restrictionScopeControl.value === 'plan') {
      return this.onboardingData.restrictions.filter((r) => r.planId !== null);
    }
    // If 'specific' scope, show restrictions for the current person in the loop
    return this.onboardingData.restrictions.filter(
      (r) => r.personId === targetPersonId
    );
  }

  // --- Handlers for Child Component Emissions ---

  // UPDATED: Now makes API call to create PersonEntity and sets _principalPersonId
  onPersonDetailsSubmitted(person: PersonModel): void {
    this.isLoading = true;
    this.personService
      .createPerson(person) // API call to create PersonEntity
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (response) => {
          if (response && response.id) {
            this._principalPersonId = response.id; // Store the real PersonId
            this.onboardingData.personId = response.id; // Update onboarding data
            this.onboardingData.personDetails = new PersonModel({
              id: response.id,
              name: person.name,
            });

            // Update the primary person in allPersonsInPlan with the real ID
            const primaryPersonInList = this.allPersonsInPlan.find(
              (p) => p.id === -1
            );
            if (primaryPersonInList) {
              primaryPersonInList.id = response.id;
              primaryPersonInList.name = person.name; // Also update name
            } else {
              // If not found (e.g., initial state), add it
              this.allPersonsInPlan.unshift(
                new PersonModel({ id: response.id, name: person.name })
              );
            }

            // After primary person is created/identified, set default for affectedPersonIdsControl
            this.affectedPersonIdsControl.setValue([this._principalPersonId]);

            this.nextStepInternal();
          } else {
            this.error =
              'Failed to create primary person record. Please try again.';
            this.notificationService.error(this.error);
          }
        },
        error: (err) => {
          console.error('Error creating person:', err);
          this.error =
            'Failed to create your profile: ' +
            (err.error?.message || err.message);
          this.notificationService.error(this.error);
        },
      });
  }

  // UPDATED: Handles health attributes for current person in loop
  onHealthAttributesSubmitted(attributes: PersonAttributeModel[]): void {
    // Assign attributes to the current person in the allPersonsInPlan array
    if (this.allPersonsInPlan[this.currentHealthAttributePersonIndex]) {
      this.allPersonsInPlan[this.currentHealthAttributePersonIndex].attributes =
        attributes;
    }
    this.nextStepInternal();
  }

  // UPDATED: Now receives a single RestrictionModel from RestrictionEditComponent
  onRestrictionsSubmitted(submittedRestriction: RestrictionModel): void {
    // Ensure the restriction has the correct PersonId or PlanId based on workflow state
    submittedRestriction.personId =
      this.restrictionScopeControl.value === 'specific'
        ? this.currentRestrictionTargetPersonId
        : null;
    submittedRestriction.planId =
      this.restrictionScopeControl.value === 'plan' ? 1 : null; // Mock planId 1, will be real ID from backend
    submittedRestriction.appliesToEntirePlan =
      this.restrictionScopeControl.value === 'plan';
    submittedRestriction.affectedPersonIds =
      this.restrictionScopeControl.value === 'specific'
        ? this.affectedPersonIdsControl.value || []
        : [];

    // Find and replace existing restriction of the same type and scope, or add new
    const existingIndex = this.onboardingData.restrictions.findIndex(
      (r) =>
        r.restrictionTypeId === submittedRestriction.restrictionTypeId &&
        r.personId === submittedRestriction.personId &&
        r.planId === submittedRestriction.planId
    );

    if (existingIndex !== -1) {
      this.onboardingData.restrictions[existingIndex] = submittedRestriction;
    } else {
      this.onboardingData.restrictions.push(submittedRestriction);
    }

    this.nextStepInternal();
  }

  // --- Handlers for UI-only Steps (No dedicated component emitted events) ---

  // Handler for "Invitation Code" submission
  onInvitationCodeSubmit(): void {
    this.error = null;
    const invitationCode = this.invitationCodeFormControl.value;

    if (invitationCode) {
      this.isLoading = true;
      // TODO: Replace with actual API call to validate invitation code
      // For now, mock a successful validation
      of(true)
        .pipe(
          // Mock API call
          finalize(() => (this.isLoading = false))
        )
        .subscribe((isValid) => {
          if (isValid) {
            this.onboardingData.planInvitationCode = invitationCode;
            // If valid code, jump to Health Attributes (Step 6)
            this.currentStepIndex = this.workflowSteps.findIndex(
              (s) => s.id === 'healthAttributes'
            );
            this.focusOnCurrentStep();
          } else {
            this.error =
              'Invalid invitation code. Please try again or create a new plan.';
            this.notificationService.error(this.error);
          }
        });
    } else {
      this.error = 'Please enter an invitation code.';
    }
  }

  // Handler for "I have no Invitation Code" button
  onNoInvitationCode(): void {
    this.error = null;
    // Jump to Your Details (Step 2)
    this.currentStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personDetails'
    );
    this.focusOnCurrentStep();
  }

  onYesNoAnswer(propertyName: string, answer: boolean): void {
    (this.onboardingData as any)[propertyName] = answer;
    this.error = null;

    if (propertyName === 'hasAdditionalParticipants') {
      if (answer === false) {
        this.onboardingData.numberOfAdditionalParticipants = 0;
        this.onboardingData.additionalParticipantDetails = [];
        // Keep only primary person if they exist, otherwise keep allPersonsInPlan empty (should have primary by now)
        this.allPersonsInPlan = this._principalPersonId
          ? [
              new PersonModel({
                id: this._principalPersonId,
                name: this.onboardingData.personDetails.name,
              }),
            ]
          : [];

        // Also clear any person-specific restrictions for removed participants
        this.onboardingData.restrictions =
          this.onboardingData.restrictions.filter(
            (r) =>
              r.planId !== null ||
              (r.personId && r.personId === this._principalPersonId)
          );

        this.nextStepInternal(); // This will lead to Summary
      } else {
        // If "Yes", ensure primary person is in allPersonsInPlan (should be from Step 2)
        if (
          !this.allPersonsInPlan.some((p) => p.id === this._principalPersonId)
        ) {
          this.allPersonsInPlan.unshift(
            new PersonModel({
              id: this._principalPersonId || 0,
              name: this.onboardingData.personDetails.name,
            })
          );
        }
        this.nextStepInternal();
      }
    } else if (propertyName === 'applyIndividualPreferencesToEachPerson') {
      // This flag directly influences looping behavior for restrictions (Steps 9-11)
      this.nextStepInternal();
    }
  }

  onNumberInput(propertyName: string, value: number): void {
    this.error = null;
    (this.onboardingData as any)[propertyName] = value;
    const newParticipantsCount = value;
    const existingParticipantsCount =
      this.onboardingData.additionalParticipantDetails.length;

    if (newParticipantsCount > existingParticipantsCount) {
      for (let i = existingParticipantsCount; i < newParticipantsCount; i++) {
        this.onboardingData.additionalParticipantDetails.push(
          new PersonModel({
            id: -(i + 2), // Assign unique negative IDs for new temp participants (e.g., -2, -3)
            name: `Person ${i + 2}`, // Default name
            attributes: [], // Initialize attributes for new person
          })
        );
      }
    } else if (newParticipantsCount < existingParticipantsCount) {
      // Remove participants and their associated restrictions & attributes
      const removedParticipants =
        this.onboardingData.additionalParticipantDetails.slice(
          newParticipantsCount
        );
      const removedParticipantIds = removedParticipants.map((p) => p.id);

      this.onboardingData.additionalParticipantDetails =
        this.onboardingData.additionalParticipantDetails.slice(
          0,
          newParticipantsCount
        );

      // Filter out restrictions and health attributes associated with removed participants
      this.onboardingData.restrictions =
        this.onboardingData.restrictions.filter(
          (r) =>
            r.planId !== null ||
            (r.personId && !removedParticipantIds.includes(r.personId))
        );
      // For health attributes, if they are structured per person, remove them from the PersonModel instances
      // (This is implicitly handled if you update allPersonsInPlan and attributes are part of PersonModel)
    }

    this.allPersonsInPlan = [
      new PersonModel({
        id: this._principalPersonId,
        name: this.onboardingData.personDetails.name,
      }), // Primary person
      ...this.onboardingData.additionalParticipantDetails,
    ];

    // Reset affectedPersonIds if the pool of participants changes significantly
    if (
      this.affectedPersonIdsControl.value &&
      !this.affectedPersonIdsControl.value.every((id) =>
        this.allPersonsInPlan.some((p) => p.id === id)
      )
    ) {
      this.affectedPersonIdsControl.setValue([this._principalPersonId!]);
    }

    this.nextStepInternal();
  }

  onNamesEntered(persons: PersonModel[]): void {
    this.error = null;
    this.onboardingData.additionalParticipantDetails = persons;
    // Update names in allPersonsInPlan to reflect changes
    persons.forEach((person, index) => {
      if (this.allPersonsInPlan[index + 1]) {
        // +1 to skip primary person
        this.allPersonsInPlan[index + 1].name = person.name;
      }
    });
    this.nextStepInternal();
  }

  // --- Restriction Scope Handlers ---
  onRestrictionScopeChange(scope: 'plan' | 'specific'): void {
    this.error = null;
    this.restrictionScopeControl.setValue(scope);
    // If scope changes to 'plan', clear affectedPersonIds as they are not relevant
    if (scope === 'plan') {
      this.affectedPersonIdsControl.setValue([]);
    } else {
      // If switching to 'specific', ensure primary person is selected by default
      if (
        !this.affectedPersonIdsControl.value ||
        this.affectedPersonIdsControl.value.length === 0
      ) {
        this.affectedPersonIdsControl.setValue([this._principalPersonId!]);
      }
    }
    this.nextStepInternal();
  }

  onAffectedPersonIdsChange(selectedIds: number[]): void {
    this.error = null;
    this.affectedPersonIdsControl.setValue(selectedIds);
    this.nextStepInternal();
  }
  // --- End Restriction Scope Handlers ---

  // --- Workflow Navigation Logic ---

  triggerChildComponentSubmission(): void {
    this.error = null; // Clear error before attempting submission

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
        // Handled by specific buttons in the HTML
        break;
      case 'ui-only-yes-no':
        // Handled by specific buttons in the HTML
        break;
      case 'ui-only-number-input':
        if (this.numberOfAdditionalParticipantsControl.valid) {
          this.onNumberInput(
            'numberOfAdditionalParticipants',
            this.numberOfAdditionalParticipantsControl.value || 0
          );
        } else {
          this.numberOfAdditionalParticipantsControl.markAsTouched();
          this.error = 'Please enter a valid number of participants.';
        }
        break;
      case 'ui-only-name-slots':
        this.onNamesEntered(this.onboardingData.additionalParticipantDetails);
        break;
      case 'ui-only-restriction-scope':
        if (this.restrictionScopeControl.valid) {
          this.onRestrictionScopeChange(this.restrictionScopeControl.value!); // Trigger internal next step
        } else {
          this.restrictionScopeControl.markAsTouched();
          this.error = 'Please select an option for restriction scope.';
        }
        break;
      case 'ui-only-select-people':
        if (
          this.affectedPersonIdsControl.valid &&
          (this.affectedPersonIdsControl.value?.length || 0) > 0
        ) {
          this.onAffectedPersonIdsChange(this.affectedPersonIdsControl.value!); // Trigger internal next step
        } else {
          this.affectedPersonIdsControl.markAsTouched();
          this.error = 'Please select at least one person.';
        }
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

  private nextStepInternal(): void {
    this.error = null; // Clear error on step change

    let nextStepIndexCandidate = this.currentStepIndex + 1;

    const healthAttributesStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'healthAttributes'
    );
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );
    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );
    const applyIndividualPreferencesStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'applyIndividualPreferences'
    );

    // Handle looping for Health Attributes (Step 6)
    if (this.currentStep.id === 'healthAttributes') {
      if (
        this.currentHealthAttributePersonIndex <
        this.allPersonsInPlan.length - 1
      ) {
        this.currentHealthAttributePersonIndex++;
        nextStepIndexCandidate = healthAttributesStepIndex; // Loop back to health attributes
        this.focusOnCurrentStep();
        return; // Exit as step is handled
      } else {
        // All health attributes collected, reset index and move to next logical step
        this.currentHealthAttributePersonIndex = 0;
      }
    }

    // Handle looping for Restriction types (Steps 9-11)
    if (
      this.currentStepIndex >= firstRestrictionStepIndex &&
      this.currentStepIndex <= lastRestrictionStepIndex
    ) {
      // If we are at the last restriction step AND individual preferences are enabled AND there are more participants
      if (
        this.currentStepIndex === lastRestrictionStepIndex &&
        this.onboardingData.hasAdditionalParticipants &&
        this.onboardingData.applyIndividualPreferencesToEachPerson &&
        this.currentRestrictionPersonIndex < this.allPersonsInPlan.length - 1
      ) {
        this.currentRestrictionPersonIndex++; // Move to the next additional participant
        this.currentStepIndex = firstRestrictionStepIndex; // Loop back to first restriction step
        this.focusOnCurrentStep();
        return; // Exit as step is handled
      }
    }

    // Default progression: Find the next un-skipped step
    while (nextStepIndexCandidate < this.workflowSteps.length) {
      const nextStepConfig = this.workflowSteps[nextStepIndexCandidate];

      // Special handling for restriction scope branches
      if (
        nextStepConfig.id === 'selectAffectedPeople' &&
        (this.restrictionScopeControl.value !== 'specific' ||
          (this.allPersonsInPlan?.length || 0) <= 1)
      ) {
        nextStepIndexCandidate++; // Skip 'selectAffectedPeople' if not applicable
        continue;
      }

      // Handle the jump from Invitation Code (Step 1) to Health Attributes (Step 6)
      // This is necessary if the user entered a valid invitation code and submitted it.
      // The currentStepIndex would be pointing to the invitationCode step, and the nextStepInternal
      // is being called from onInvitationCodeSubmit after setting currentStepIndex to healthAttributes.
      // So, if we are currently at a step whose ID is 'healthAttributes' and the previous step was 'invitationCode'
      // and we indeed processed an invitation code, we should not proceed through personDetails etc.
      if (
        this.currentStepIndex === healthAttributesStepIndex &&
        this.currentStepIndex > 0 &&
        this.workflowSteps[this.currentStepIndex - 1].id === 'invitationCode' &&
        this.onboardingData.planInvitationCode
      ) {
        // Ensure that if we just jumped to healthAttributes from invitationCode,
        // we don't accidentally proceed through other steps before it in the workflowSteps array.
        // This break ensures we stay on healthAttributes.
        break;
      }

      if (nextStepConfig.condition) {
        // Use non-null assertion (!) because we are confident it exists due to the 'if' check
        if (!nextStepConfig.condition!(this.onboardingData, this)) {
          nextStepIndexCandidate++; // Skip this step
        } else {
          break; // This step is valid, stop skipping
        }
      } else {
        break; // No condition, valid step
      }
    }

    this.currentStepIndex = nextStepIndexCandidate;
    this.focusOnCurrentStep();
  }

  previousStep(): void {
    this.error = null; // Clear error on step change

    const healthAttributesStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'healthAttributes'
    );
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );
    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );
    const applyIndividualPreferencesStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'applyIndividualPreferences'
    );
    const restrictionScopeStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'restrictionScope'
    );
    const selectAffectedPeopleStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'selectAffectedPeople'
    );
    const hasAdditionalParticipantsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'hasAdditionalParticipants'
    );
    const personDetailsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personDetails'
    );
    const invitationCodeStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'invitationCode'
    );

    // Handle going back within Restriction loop (Steps 9-11)
    if (
      this.currentStepIndex >= firstRestrictionStepIndex &&
      this.currentStepIndex <= lastRestrictionStepIndex
    ) {
      if (this.currentStepIndex === firstRestrictionStepIndex) {
        // If at the first restriction step for a person in loop
        if (
          this.onboardingData.hasAdditionalParticipants &&
          this.onboardingData.applyIndividualPreferencesToEachPerson &&
          this.currentRestrictionPersonIndex > 0
        ) {
          this.currentRestrictionPersonIndex--; // Go back to previous person
          this.currentStepIndex = lastRestrictionStepIndex; // Loop back to last restriction type for previous person
        } else {
          // If at the first restriction step for the primary person (or first person in loop after initial decision)
          // Go back to Select Affected People (if applicable) or Restriction Scope
          if (
            this.workflowSteps[selectAffectedPeopleStepIndex].condition &&
            this.workflowSteps[selectAffectedPeopleStepIndex].condition!(
              this.onboardingData,
              this
            )
          ) {
            // Non-null assertion
            this.currentStepIndex = selectAffectedPeopleStepIndex;
          } else {
            this.currentStepIndex = restrictionScopeStepIndex;
          }
        }
        this.focusOnCurrentStep();
        return;
      } else {
        this.currentStepIndex--; // Go to previous restriction type for current person
        this.focusOnCurrentStep();
        return;
      }
    }

    // Handle going back within Health Attributes loop (Step 6)
    if (this.currentStepIndex === healthAttributesStepIndex) {
      if (this.currentHealthAttributePersonIndex > 0) {
        this.currentHealthAttributePersonIndex--;
        this.currentStepIndex = healthAttributesStepIndex; // Stay on health attributes for previous person
        this.focusOnCurrentStep();
        return;
      } else {
        // If at the first person's health attributes, go back to Participant Names (Step 5)
        // Or if no additional participants, go back to 'hasAdditionalParticipants' (Step 3) if they said no
        if (this.onboardingData.hasAdditionalParticipants) {
          this.currentStepIndex = this.workflowSteps.findIndex(
            (s) => s.id === 'additionalParticipantNames'
          );
        } else {
          // If no additional participants, and coming from Health Attributes,
          // then the path was: InvitationCode (No Code) -> PersonDetails -> HealthAttributes
          // So, go back to PersonDetails.
          this.currentStepIndex = personDetailsStepIndex;
        }
        this.focusOnCurrentStep();
        return;
      }
    }

    // Normal back navigation, accounting for conditional steps and direct jumps
    if (this.currentStepIndex > 0) {
      this.currentStepIndex--;

      // Special case: If coming from InvitationCode (Step 1) and user accepted it,
      // and we are currently at a step before 'Health Attributes', we need to jump back to 'InvitationCode'.
      // This handles scenarios where user might click 'Previous' from a step that was jumped over.
      if (
        this.onboardingData.planInvitationCode &&
        this.currentStepIndex < healthAttributesStepIndex
      ) {
        this.currentStepIndex = invitationCodeStepIndex;
        this.focusOnCurrentStep();
        return;
      }

      // If going back from summary and no additional participants
      if (
        this.currentStep.id === 'summary' &&
        !this.onboardingData.hasAdditionalParticipants
      ) {
        this.currentStepIndex = hasAdditionalParticipantsStepIndex;
        this.focusOnCurrentStep();
        return;
      }

      // Skip back over conditional steps if their condition is no longer met
      while (
        this.currentStepIndex > 0 &&
        this.workflowSteps[this.currentStepIndex].condition &&
        !this.workflowSteps[this.currentStepIndex].condition!(
          this.onboardingData,
          this
        ) // Non-null assertion
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
      this.nextStepInternal();
    } else if (this.currentStep?.id === 'invitationCode') {
      // Special case for 'Skip' on Invitation Code step
      this.onNoInvitationCode();
    }
  }

  submitOnboardingData(): void {
    if (this.isSubmitting) return;

    this.isSubmitting = true;
    this.error = null;
    this.submitMessage = null;

    // Ensure the primary person's attributes are captured in the main onboardingData
    if (this._principalPersonId) {
      const primaryPerson = this.allPersonsInPlan.find(
        (p) => p.id === this._principalPersonId
      );
      if (primaryPerson) {
        this.onboardingData.personDetails.attributes = primaryPerson.attributes;
      }
    }

    // Populate additionalParticipantDetails with their collected attributes
    this.onboardingData.additionalParticipantDetails.forEach(
      (additionalPerson) => {
        const fullPersonData = this.allPersonsInPlan.find(
          (p) => p.id === additionalPerson.id
        );
        if (fullPersonData) {
          additionalPerson.attributes = fullPersonData.attributes;
        }
      }
    );

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
            // The _principalPersonId should already be set from Step 2, this is a fallback/confirmation
            if (!this._principalPersonId || this._principalPersonId < 0) {
              // Only update if it was a temp ID
              this._principalPersonId = newRealPrincipalId;
            }
            this.onboardingData.personId = newRealPrincipalId;
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
      // Also update the allPersonsInPlan array's corresponding entry
      if (this.allPersonsInPlan[index + 1]) {
        // +1 to skip primary person
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
