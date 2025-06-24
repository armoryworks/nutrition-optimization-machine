import {
  Component,
  OnInit,
  Output,
  EventEmitter,
  ViewEncapsulation,
  ViewChild,
  OnDestroy,
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
import { finalize, Subscription } from 'rxjs';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';

// Import all child components for ViewChild references
import { PersonEditComponent } from '../../../person/components/person-edit/person-edit.component';
import { PersonHealthEditComponent } from '../../../person/components/person-health-edit/person-health-edit.component';
import { PlanEditComponent } from '../../../plan/components/plan-edit/plan-edit.component';
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component';
import { OnboardingInvitationCodeComponent } from '../onboarding-invitation-code/onboarding-invitation-code.component';
import { OnboardingAdditionalParticipantsComponent } from '../onboarding-additional-participants/onboarding-additional-participants.component';
import { OnboardingRestrictionScopeComponent } from '../onboarding-restriction-scope/onboarding-restriction-scope.component';

// Import Models and Services
import { PersonModel } from '../../../person/models/person.model';
import { PersonAttributeModel } from '../../../person/models/person-attribute.model';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { OnboardingCompleteRequestModel } from '../../models/onboarding-complete-request.model';
import { PersonService } from '../../../person/services/person.service';
import { NotificationService } from '../../../utilities/services/notification.service';
import { RestrictionTypeEnum } from '../../../restriction/enums/restriction-type.enum';
import { OnboardingService } from '../../services/onboarding.service';

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
    OnboardingInvitationCodeComponent,
    OnboardingAdditionalParticipantsComponent,
    OnboardingRestrictionScopeComponent,
  ],
  templateUrl: './onboarding-workflow.component.html',
  styleUrls: ['./onboarding-workflow.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class OnboardingWorkflowComponent implements OnInit, OnDestroy {
  private _principalPersonId: number | null = null;

  @Output() onboardingComplete = new EventEmitter<boolean>();

  currentStepIndex: number = 0;
  isLoading: boolean = false;
  isSubmitting: boolean = false;
  error: string | null = null;
  submitMessage: string | null = null;

  onboardingData!: OnboardingCompleteRequestModel;
  workflowSteps: any[] = [];

  currentHealthAttributePersonIndex: number = 0;
  currentRestrictionPersonIndex: number = 0;

  allPersonsInPlan: PersonModel[] = [];

  private routeSubscription!: Subscription;
  private onboardingDataSubscription!: Subscription;

  // Define NO_FLOW_STEPS as an array of step IDs that manage their own navigation
  public NO_FLOW_STEPS: string[] = [
    'invitationCode',
    'additionalParticipants',
    'restrictionScope',
  ];

  @ViewChild(PersonEditComponent) personEditComponent?: PersonEditComponent;
  @ViewChild(PersonHealthEditComponent)
  personHealthEditComponent?: PersonHealthEditComponent;
  @ViewChild(RestrictionEditComponent)
  restrictionEditComponent?: RestrictionEditComponent;
  @ViewChild(OnboardingInvitationCodeComponent)
  onboardingInvitationCodeComponent?: OnboardingInvitationCodeComponent;
  @ViewChild(OnboardingAdditionalParticipantsComponent)
  onboardingAdditionalParticipantsComponent?: OnboardingAdditionalParticipantsComponent;
  @ViewChild(OnboardingRestrictionScopeComponent)
  onboardingRestrictionScopeComponent?: OnboardingRestrictionScopeComponent;

  constructor(
    private personService: PersonService,
    private notificationService: NotificationService,
    private onboardingService: OnboardingService,
    private router: Router,
    private activatedRoute: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.workflowSteps = this.onboardingService.workflowSteps;

    this.onboardingDataSubscription =
      this.onboardingService.onboardingData$.subscribe((data) => {
        this.onboardingData = data;
        this.deriveAllPersonsInPlan();
      });

    this.routeSubscription = this.activatedRoute.paramMap.subscribe(
      (params) => {
        const stepId = params.get('stepId');
        this.initializeWorkflowPosition(stepId);
        this.focusOnCurrentStep();
      }
    );
  }

  ngOnDestroy(): void {
    if (this.routeSubscription) {
      this.routeSubscription.unsubscribe();
    }
    if (this.onboardingDataSubscription) {
      this.onboardingDataSubscription.unsubscribe();
    }
  }

  private deriveAllPersonsInPlan(): void {
    const persons: PersonModel[] = [];
    if (
      this.onboardingData.personDetails &&
      this.onboardingData.personDetails.id
    ) {
      persons.push(new PersonModel(this.onboardingData.personDetails));
      this._principalPersonId = this.onboardingData.personDetails.id;
    }
    if (
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.additionalParticipantDetails
    ) {
      this.onboardingData.additionalParticipantDetails.forEach((p) => {
        if (!(p instanceof PersonModel)) {
          p = new PersonModel(p);
        }
        persons.push(p);
      });
    }
    this.allPersonsInPlan = persons;
  }

  private initializeWorkflowPosition(stepId: string | null): void {
    const foundIndex = this.workflowSteps.findIndex(
      (step) => step.id === stepId
    );
    if (foundIndex !== -1) {
      this.currentStepIndex = foundIndex;
    } else {
      this.router.navigate(['onboarding', this.workflowSteps[0].id], {
        replaceUrl: true,
      });
      this.currentStepIndex = 0;
    }

    const healthAttributesStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'healthAttributes'
    );
    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );

    if (this.currentStepIndex !== healthAttributesStepIndex) {
      this.currentHealthAttributePersonIndex = 0;
    }
    if (
      this.currentStepIndex < firstRestrictionStepIndex ||
      this.currentStepIndex >
        this.workflowSteps.findIndex((s) => s.id === 'personalPreferences')
    ) {
      this.currentRestrictionPersonIndex = 0;
    }
  }

  get currentStep(): any {
    const currentStepConfig = this.workflowSteps[this.currentStepIndex];

    if (
      currentStepConfig.id === 'healthAttributes' &&
      this.allPersonsInPlan.length > 0 &&
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

    const firstRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'societalRestrictions'
    );
    const lastRestrictionStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personalPreferences'
    );

    if (
      this.currentStepIndex >= firstRestrictionStepIndex &&
      this.currentStepIndex <= lastRestrictionStepIndex &&
      this.onboardingData.hasAdditionalParticipants &&
      this.onboardingData.applyIndividualPreferencesToEachPerson &&
      this.currentRestrictionPersonIndex < this.allPersonsInPlan.length
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
    // FIX: The logic here was inverted. It should be skippable (via main button)
    // ONLY IF it's NOT in the NO_FLOW_STEPS list.
    return (
      !this.currentStep?.required &&
      this.currentStep?.id !== 'summary' &&
      !this.NO_FLOW_STEPS.includes(this.currentStep?.id)
    );
  }

  get currentHealthAttributesTargetPersonId(): number {
    return (
      this.allPersonsInPlan[this.currentHealthAttributePersonIndex]?.id || 0
    );
  }

  get currentRestrictionTargetPersonId(): number {
    return this.allPersonsInPlan[this.currentRestrictionPersonIndex]?.id || 0;
  }

  get filteredRestrictionsForCurrentPerson(): RestrictionModel[] {
    const targetPersonId = this.currentRestrictionTargetPersonId;
    const isPlanWide = this.onboardingData.currentRestrictionScope === 'plan';

    if (isPlanWide) {
      return this.onboardingData.restrictions.filter((r) => r.planId !== null);
    }
    return this.onboardingData.restrictions.filter(
      (r) => r.personId === targetPersonId
    );
  }

  // --- Handlers for Child Component Emissions ---

  onInvitationCodeSubmitted(code: string): void {
    this.error = null;
    this.isLoading = true;
    setTimeout(() => {
      this.isLoading = false;
      const isValid = code === 'PLAN123';
      if (isValid) {
        this.onboardingService.updateOnboardingProperty(
          'planInvitationCode',
          code
        );
        if (!this.onboardingData.personDetails?.id) {
          this.onboardingService.updateOnboardingProperty(
            'personDetails',
            new PersonModel({ id: 100, name: 'Existing User' })
          );
          this.onboardingService.updateOnboardingProperty('personId', 100);
          this._principalPersonId = 100;
        }
        this.nextStepInternal();
      } else {
        this.error =
          'Invalid invitation code. Please try again or create a new plan.';
        this.notificationService.error(this.error);
        this.onboardingInvitationCodeComponent?.invitationCodeFormControl.setErrors(
          { invalidCode: true }
        );
      }
    }, 1000);
  }

  onNoInvitationCodeSelected(): void {
    this.error = null;
    this.onboardingService.updateOnboardingProperty('planInvitationCode', null);
    this.nextStepInternal();
  }

  onPersonDetailsSubmitted(person: PersonModel): void {
    this.isLoading = true;
    // Assuming personService.createPerson expects a PersonModel object now, not just a name string.
    // If it expects `person.name`, change `person` to `person.name` here.
    this.personService
      .createPerson(person)
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (response) => {
          if (response && response.id) {
            this._principalPersonId = response.id;
            const updatedPersonDetails = new PersonModel({
              id: response.id,
              name: person.name,
            });
            this.onboardingService.updateOnboardingProperty(
              'personDetails',
              updatedPersonDetails
            );
            this.onboardingService.updateOnboardingProperty(
              'personId',
              response.id
            );

            this.onboardingService.updateOnboardingProperty(
              'currentAffectedPersonIds',
              [this._principalPersonId]
            );

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

  onAdditionalParticipantsSubmitted(data: {
    hasAdditionalParticipants: boolean;
    numberOfAdditionalParticipants: number;
    additionalParticipantDetails: PersonModel[];
  }): void {
    this.error = null;
    this.onboardingService.updateOnboardingProperty(
      'hasAdditionalParticipants',
      data.hasAdditionalParticipants
    );
    this.onboardingService.updateOnboardingProperty(
      'numberOfAdditionalParticipants',
      data.numberOfAdditionalParticipants
    );
    this.onboardingService.updateOnboardingProperty(
      'additionalParticipantDetails',
      data.additionalParticipantDetails
    );

    this.nextStepInternal();
  }

  onHealthAttributesSubmitted(attributes: PersonAttributeModel[]): void {
    const currentPersonId = this.currentHealthAttributesTargetPersonId;
    const personToUpdate = this.allPersonsInPlan.find(
      (p) => p.id === currentPersonId
    );

    if (personToUpdate) {
      personToUpdate.attributes = attributes;
      if (
        personToUpdate.id === this._principalPersonId &&
        this.onboardingData.personDetails
      ) {
        this.onboardingData.personDetails.attributes =
          personToUpdate.attributes;
      } else {
        const additionalIndex =
          this.onboardingData.additionalParticipantDetails.findIndex(
            (p) => p.id === personToUpdate.id
          );
        if (additionalIndex !== -1) {
          this.onboardingData.additionalParticipantDetails[
            additionalIndex
          ].attributes = personToUpdate.attributes;
        }
      }
      this.onboardingService.saveOnboardingData();
    }
    this.nextStepInternal();
  }

  onRestrictionScopeDataSubmitted(data: {
    appliesToEntirePlan: boolean;
    affectedPersonIds: number[];
  }): void {
    this.error = null;
    this.onboardingService.updateOnboardingProperty(
      'currentRestrictionScope',
      data.appliesToEntirePlan ? 'plan' : 'specific'
    );
    this.onboardingService.updateOnboardingProperty(
      'currentAffectedPersonIds',
      data.affectedPersonIds
    );

    this.nextStepInternal();
  }

  onRestrictionsSubmitted(submittedRestriction: RestrictionModel): void {
    submittedRestriction.personId =
      this.onboardingData.currentRestrictionScope === 'specific'
        ? this.currentRestrictionTargetPersonId
        : null;
    submittedRestriction.planId =
      this.onboardingData.currentRestrictionScope === 'plan' ? 1 : null;
    submittedRestriction.appliesToEntirePlan =
      this.onboardingData.currentRestrictionScope === 'plan';
    submittedRestriction.affectedPersonIds =
      this.onboardingData.currentRestrictionScope === 'specific'
        ? this.onboardingData.currentAffectedPersonIds || []
        : [];

    const updatedRestrictions = [...this.onboardingData.restrictions];
    const existingIndex = updatedRestrictions.findIndex(
      (r) =>
        r.restrictionTypeId === submittedRestriction.restrictionTypeId &&
        r.personId === submittedRestriction.personId &&
        r.planId === submittedRestriction.planId
    );

    if (existingIndex !== -1) {
      updatedRestrictions[existingIndex] = submittedRestriction;
    } else {
      updatedRestrictions.push(submittedRestriction);
    }

    this.onboardingService.updateOnboardingProperty(
      'restrictions',
      updatedRestrictions
    );

    this.nextStepInternal();
  }

  onIndividualPreferencesAnswer(answer: boolean): void {
    this.error = null;
    this.onboardingService.updateOnboardingProperty(
      'applyIndividualPreferencesToEachPerson',
      answer
    );
    this.nextStepInternal();
  }

  // --- Workflow Navigation Logic ---

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
      case 'app-onboarding-additional-participants':
        if (
          this.onboardingAdditionalParticipantsComponent?.currentSubStep ===
          'names'
        ) {
          this.onboardingAdditionalParticipantsComponent.onNamesSubmit();
        } else if (
          this.onboardingAdditionalParticipantsComponent?.currentSubStep ===
          'howMany'
        ) {
          this.onboardingAdditionalParticipantsComponent.onHowManySubmit();
        } else {
          console.warn(
            'Attempted to trigger submission on OnboardingAdditionalParticipantsComponent at an unexpected sub-step.'
          );
          this.nextStepInternal();
        }
        break;
      case 'app-onboarding-restriction-scope':
        if (
          this.onboardingRestrictionScopeComponent?.currentSubStep ===
          'selectPeople'
        ) {
          this.onboardingRestrictionScopeComponent.onPeopleSelectionSubmit();
        } else if (
          this.onboardingRestrictionScopeComponent?.currentSubStep ===
          'selectScope'
        ) {
          this.onboardingRestrictionScopeComponent.onScopeSelection();
        } else {
          console.warn(
            'Attempted to trigger submission on OnboardingRestrictionScopeComponent at an unexpected sub-step.'
          );
          this.nextStepInternal();
        }
        break;
      case 'ui-only-yes-no':
        console.warn(
          'triggerChildComponentSubmission called for ui-only-yes-no step, which should not happen.'
        );
        break;
      case 'ui-only-summary':
        this.submitOnboardingData();
        break;
      default:
        console.log(
          `Advancing from default case for step: ${this.currentStep?.id}`
        );
        this.nextStepInternal();
        break;
    }
  }

  private navigateToStep(stepId: string): void {
    this.router.navigate(['onboarding', stepId]);
  }

  private nextStepInternal(): void {
    this.error = null;

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
    const summaryStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'summary'
    );
    const invitationCodeStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'invitationCode'
    );
    const personDetailsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personDetails'
    );
    const additionalParticipantsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'additionalParticipants'
    );
    const restrictionScopeStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'restrictionScope'
    );

    // Handle special jumps or loops FIRST
    // Jump from Invitation Code (Step 1) to Health Attributes (Step 6)
    if (
      this.currentStep.id === 'invitationCode' &&
      this.onboardingData.planInvitationCode
    ) {
      this.currentStepIndex = healthAttributesStepIndex;
      this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
      return;
    }

    // Handle "No Additional Participants" path from Step 3 (Additional Participants component)
    if (
      this.currentStep.id === 'additionalParticipants' &&
      this.onboardingData.hasAdditionalParticipants === false
    ) {
      this.currentStepIndex = summaryStepIndex;
      this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
      return;
    }

    // Handle looping for Health Attributes (Step 6)
    if (
      this.currentStep.id === 'healthAttributes' &&
      this.allPersonsInPlan.length > 0
    ) {
      if (
        this.currentHealthAttributePersonIndex <
        this.allPersonsInPlan.length - 1
      ) {
        this.currentHealthAttributePersonIndex++;
        this.navigateToStep(this.workflowSteps[healthAttributesStepIndex].id);
        return;
      } else {
        this.currentHealthAttributePersonIndex = 0;
      }
    }

    // Handle looping for Restriction types (Steps 9-11)
    if (
      this.currentStepIndex >= firstRestrictionStepIndex &&
      this.currentStepIndex <= lastRestrictionStepIndex
    ) {
      if (
        this.currentStepIndex === lastRestrictionStepIndex &&
        this.onboardingData.hasAdditionalParticipants &&
        this.onboardingData.applyIndividualPreferencesToEachPerson &&
        this.currentRestrictionPersonIndex < this.allPersonsInPlan.length - 1
      ) {
        this.currentRestrictionPersonIndex++;
        this.currentStepIndex = firstRestrictionStepIndex;
        this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
        return;
      } else if (this.currentStepIndex === lastRestrictionStepIndex) {
        if (
          this.onboardingData.hasAdditionalParticipants &&
          this.workflowSteps[applyIndividualPreferencesStepIndex].condition!(
            this.onboardingData,
            this
          )
        ) {
          nextStepIndexCandidate = applyIndividualPreferencesStepIndex;
        } else {
          nextStepIndexCandidate = summaryStepIndex;
        }
      }
    }

    // Default progression: Find the next un-skipped step in the linear sequence
    while (nextStepIndexCandidate < this.workflowSteps.length) {
      const nextStepConfig = this.workflowSteps[nextStepIndexCandidate];

      if (nextStepConfig.condition) {
        if (!nextStepConfig.condition!(this.onboardingData, this)) {
          nextStepIndexCandidate++;
        } else {
          break;
        }
      } else {
        break;
      }
    }

    this.currentStepIndex = nextStepIndexCandidate;
    this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
  }

  previousStep(): void {
    this.error = null;

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
    const additionalParticipantsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'additionalParticipants'
    );
    const personDetailsStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'personDetails'
    );
    const invitationCodeStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'invitationCode'
    );
    const summaryStepIndex = this.workflowSteps.findIndex(
      (s) => s.id === 'summary'
    );

    // Handle going back from Summary when 'No Additional Participants' was selected
    if (
      this.currentStep.id === 'summary' &&
      this.onboardingData.hasAdditionalParticipants === false
    ) {
      this.currentStepIndex = additionalParticipantsStepIndex;
      this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
      return;
    }

    // Handle going back from Apply Individual Preferences (Step 12)
    if (this.currentStep.id === 'applyIndividualPreferences') {
      this.currentStepIndex = lastRestrictionStepIndex;
      this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
      return;
    }

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
          this.currentRestrictionPersonIndex--;
          this.currentStepIndex = lastRestrictionStepIndex;
        } else {
          this.currentStepIndex = restrictionScopeStepIndex;
        }
        this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
        return;
      } else {
        this.currentStepIndex--;
        this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
        return;
      }
    }

    // Handle going back within Health Attributes loop (Step 6)
    if (this.currentStepIndex === healthAttributesStepIndex) {
      if (this.currentHealthAttributePersonIndex > 0) {
        this.currentHealthAttributePersonIndex--;
        this.navigateToStep(this.workflowSteps[healthAttributesStepIndex].id);
        return;
      } else {
        if (this.onboardingData.planInvitationCode) {
          this.currentStepIndex = invitationCodeStepIndex;
        } else {
          this.currentStepIndex = additionalParticipantsStepIndex;
        }
        this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
        return;
      }
    }

    // Default back navigation: Find the previous un-skipped step
    if (this.currentStepIndex > 0) {
      this.currentStepIndex--;

      // Skip back over conditional steps if their condition is no longer met
      while (
        this.currentStepIndex > 0 &&
        this.workflowSteps[this.currentStepIndex].condition &&
        !this.workflowSteps[this.currentStepIndex].condition!(
          this.onboardingData,
          this
        )
      ) {
        this.currentStepIndex--;
      }
    }
    this.navigateToStep(this.workflowSteps[this.currentStepIndex].id);
  }

  skipCurrentStep(): void {
    if (
      this.currentStep &&
      !this.currentStep.required &&
      !this.NO_FLOW_STEPS.includes(this.currentStep?.id) // Corrected logic here
    ) {
      const dataProp = this.currentStep.dataProperty;
      if (dataProp) {
        if (dataProp === 'attributes') {
          const currentPersonId = this.currentHealthAttributesTargetPersonId;
          const personToUpdate = this.allPersonsInPlan.find(
            (p) => p.id === currentPersonId
          );
          if (personToUpdate) {
            personToUpdate.attributes = [];
          }
        } else if (Array.isArray((this.onboardingData as any)[dataProp])) {
          this.onboardingService.updateOnboardingProperty(
            dataProp as keyof OnboardingCompleteRequestModel,
            [] as any
          );
        } else {
          this.onboardingService.updateOnboardingProperty(
            dataProp as keyof OnboardingCompleteRequestModel,
            null as any
          );
        }
      }
      this.nextStepInternal();
    } else if (this.currentStep?.id === 'invitationCode') {
      this.onNoInvitationCodeSelected();
    }
  }

  submitOnboardingData(): void {
    if (this.isSubmitting) return;

    this.isSubmitting = true;
    this.error = null;
    this.submitMessage = null;

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
            this.onboardingService.updateOnboardingProperty(
              'personId',
              response.data.newPersonId
            );
          }
          this.onboardingService.clearOnboardingData();
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
