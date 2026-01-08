import {
  Component,
  OnInit,
  input,
  output,
  signal,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PersonModel } from '../../../person/models/person.model';

@Component({
  selector: 'nom-onboarding-additional-participants',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './onboarding-additional-participants.component.html',
  styleUrls: ['./onboarding-additional-participants.component.scss'],
})
export class OnboardingAdditionalParticipantsComponent
  implements OnInit {
  hasAdditionalParticipantsInput = input<boolean | null>(null);
  numberOfAdditionalParticipantsInput = input<number | null>(null);
  additionalParticipantDetailsInput = input<PersonModel[]>([]);
  principalPersonId = input<number | null>(null); // Used for generating unique temp IDs
  isLoading = input(false);
  allCurrentPersonsInPlan = input<PersonModel[]>([]); // Full list of persons for temp ID generation

  participantsDataSubmitted = output<{
    hasAdditionalParticipants: boolean;
    numberOfAdditionalParticipants: number;
    additionalParticipantDetails: PersonModel[];
  }>();
  skipStep = output<void>(); // For skipping the whole section if a skip button is desired

  // Internal state to manage the sub-workflow
  currentSubStep: 'hasParticipants' | 'howMany' | 'names' = 'hasParticipants';

  // Local form controls and data
  internalHasAdditionalParticipants: boolean | null = null;
  numberOfAdditionalParticipantsControl = new FormControl<number | null>(null, [
    Validators.min(0),
    Validators.required,
  ]);
  internalAdditionalParticipantDetails: PersonModel[] = [];

  constructor() {
    // Effect to watch for input changes and re-initialize
    effect(() => {
      // Access all inputs to track them
      const hasParticipants = this.hasAdditionalParticipantsInput();
      const numberOfParticipants = this.numberOfAdditionalParticipantsInput();
      const participantDetails = this.additionalParticipantDetailsInput();

      // Re-initialize if inputs have changed
      if (
        hasParticipants !== this.internalHasAdditionalParticipants ||
        numberOfParticipants !== this.numberOfAdditionalParticipantsControl.value ||
        participantDetails !== this.internalAdditionalParticipantDetails
      ) {
        this.initializeFromInputs();
      }
      this.updateSubStep();
    });
  }

  ngOnInit(): void {
    this.initializeFromInputs();
  }

  private initializeFromInputs(): void {
    this.internalHasAdditionalParticipants =
      this.hasAdditionalParticipantsInput();
    this.numberOfAdditionalParticipantsControl.setValue(
      this.numberOfAdditionalParticipantsInput()
    );
    // Create a deep copy to avoid direct mutation of parent's array
    this.internalAdditionalParticipantDetails = this
      .additionalParticipantDetailsInput()
      ? this.additionalParticipantDetailsInput().map((p) => new PersonModel(p))
      : [];

    // Ensure names are empty strings if they were default placeholders on load
    this.internalAdditionalParticipantDetails.forEach((p) => {
      const defaultNamePattern = /^Person \d+$/; // Regex to match "Person X"
      if (p.name && defaultNamePattern.test(p.name)) {
        p.name = ''; // Clear default names loaded from input
      }
    });

    this.updateSubStep();
  }

  private updateSubStep(): void {
    if (this.internalHasAdditionalParticipants === null) {
      this.currentSubStep = 'hasParticipants';
    } else if (this.internalHasAdditionalParticipants === true) {
      if (
        this.numberOfAdditionalParticipantsControl.value === null ||
        this.numberOfAdditionalParticipantsControl.invalid
      ) {
        this.currentSubStep = 'howMany';
      } else if (this.numberOfAdditionalParticipantsControl.value! > 0) {
        this.currentSubStep = 'names';
        this.ensureCorrectNumberOfNameFields();
      } else {
        this.currentSubStep = 'howMany';
      }
    } else {
      this.currentSubStep = 'hasParticipants';
    }
  }

  onHasParticipantsAnswer(answer: boolean): void {
    this.internalHasAdditionalParticipants = answer;
    if (!answer) {
      this.numberOfAdditionalParticipantsControl.setValue(0);
      this.internalAdditionalParticipantDetails = [];
      this.emitDataAndProceed();
    } else {
      this.updateSubStep();
    }
  }

  onHowManySubmit(): void {
    if (
      this.numberOfAdditionalParticipantsControl.valid &&
      this.numberOfAdditionalParticipantsControl.value !== null
    ) {
      this.ensureCorrectNumberOfNameFields();
      if (this.numberOfAdditionalParticipantsControl.value === 0) {
        this.emitDataAndProceed();
      } else {
        this.updateSubStep();
      }
    } else {
      this.numberOfAdditionalParticipantsControl.markAsTouched();
    }
  }

  get areAllParticipantNamesValid(): boolean {
    return this.internalAdditionalParticipantDetails.every(
      (p) => p.name && p.name.trim().length > 0
    );
  }

  onNamesSubmit(): void {
    if (this.areAllParticipantNamesValid) {
      this.emitDataAndProceed();
    } else {
      console.error('All participant names are required and cannot be empty.');
      // Optionally, add logic here to mark individual name fields as invalid/touched
    }
  }

  updateParticipantName(index: number, event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    const name = inputElement.value;
    if (this.internalAdditionalParticipantDetails[index]) {
      this.internalAdditionalParticipantDetails[index].name = name;
    }
  }

  // Removed clearDefaultNameOnFocus as placeholder handles it now.

  private ensureCorrectNumberOfNameFields(): void {
    const desiredCount = this.numberOfAdditionalParticipantsControl.value || 0;
    const currentCount = this.internalAdditionalParticipantDetails.length;

    if (desiredCount > currentCount) {
      for (let i = currentCount; i < desiredCount; i++) {
        const newTempId = -(this.allCurrentPersonsInPlan().length + 1 + i);
        this.internalAdditionalParticipantDetails.push(
          new PersonModel({
            id: newTempId,
            name: '', // Initialize with an EMPTY string
            attributes: [],
          })
        );
      }
    } else if (desiredCount < currentCount) {
      this.internalAdditionalParticipantDetails =
        this.internalAdditionalParticipantDetails.slice(0, desiredCount);
    }
  }

  emitDataAndProceed(): void {
    this.participantsDataSubmitted.emit({
      hasAdditionalParticipants: this.internalHasAdditionalParticipants!,
      numberOfAdditionalParticipants:
        this.numberOfAdditionalParticipantsControl.value || 0,
      additionalParticipantDetails: this.internalAdditionalParticipantDetails,
    });
  }

  // --- Navigation for internal sub-steps ---
  goToPreviousSubStep(): void {
    if (this.currentSubStep === 'names') {
      this.currentSubStep = 'howMany';
    } else if (this.currentSubStep === 'howMany') {
      this.currentSubStep = 'hasParticipants';
    } else if (this.currentSubStep === 'hasParticipants') {
      this.skipStep.emit();
    }
  }

  isPreviousSubStepAvailable(): boolean {
    return this.currentSubStep !== 'hasParticipants';
  }
}
