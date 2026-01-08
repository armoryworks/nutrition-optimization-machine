import {
  Component,
  OnInit,
  input,
  output,
  effect,
} from '@angular/core';

import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { PersonModel } from '../../../person/models/person.model';

@Component({
  selector: 'nom-onboarding-restriction-scope',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatRadioModule,
    MatSelectModule
],
  templateUrl: './onboarding-restriction-scope.component.html',
  styleUrls: ['./onboarding-restriction-scope.component.scss'],
})
export class OnboardingRestrictionScopeComponent implements OnInit {
  restrictionScopeInput = input<'plan' | 'specific' | null>(null);
  affectedPersonIdsInput = input<number[] | null>(null);
  allPersonsInPlan = input<PersonModel[]>([]);
  isLoading = input(false); // To disable buttons when parent is loading

  scopeSubmitted = output<{
    appliesToEntirePlan: boolean;
    affectedPersonIds: number[];
  }>();

  // Internal sub-step management
  currentSubStep: 'selectScope' | 'selectPeople' = 'selectScope';

  // Internal FormControls
  internalRestrictionScopeControl = new FormControl<'plan' | 'specific' | null>(
    null,
    Validators.required
  );
  internalAffectedPersonIdsControl = new FormControl<number[] | null>(
    null,
    Validators.required
  );

  constructor() {
    // Effect to watch for input changes and re-initialize
    effect(() => {
      // Access inputs to track them
      const scope = this.restrictionScopeInput();
      const personIds = this.affectedPersonIdsInput();
      const persons = this.allPersonsInPlan();

      // Re-initialize when inputs change
      this.initializeFromInputs();
    });
  }

  ngOnInit(): void {
    this.initializeFromInputs();
  }

  private initializeFromInputs(): void {
    // Set initial values for internal controls from inputs
    if (this.restrictionScopeInput() !== null) {
      this.internalRestrictionScopeControl.setValue(this.restrictionScopeInput());
    }
    if (this.affectedPersonIdsInput() !== null) {
      this.internalAffectedPersonIdsControl.setValue(
        this.affectedPersonIdsInput()
      );
    }

    // Determine starting sub-step based on loaded data or default
    this.updateSubStep();
  }

  private updateSubStep(): void {
    if (
      this.internalRestrictionScopeControl.value === 'specific' &&
      (this.allPersonsInPlan()?.length || 0) > 1
    ) {
      this.currentSubStep = 'selectPeople';
    } else {
      this.currentSubStep = 'selectScope';
    }
  }

  onScopeSelection(): void {
    if (this.internalRestrictionScopeControl.valid) {
      if (this.internalRestrictionScopeControl.value === 'plan') {
        this.emitDataAndProceed(true, []); // Plan-wide, no specific people
      } else {
        // 'specific' selected
        // If only one person available, auto-select them and proceed
        if ((this.allPersonsInPlan()?.length || 0) <= 1) {
          const personId =
            this.allPersonsInPlan().length > 0 ? this.allPersonsInPlan()[0].id! : 0;
          this.emitDataAndProceed(false, [personId]);
        } else {
          // Otherwise, move to the "select people" sub-step
          this.currentSubStep = 'selectPeople';
        }
      }
    } else {
      this.internalRestrictionScopeControl.markAsTouched();
    }
  }

  onPeopleSelectionSubmit(): void {
    if (
      this.internalAffectedPersonIdsControl.valid &&
      (this.internalAffectedPersonIdsControl.value?.length || 0) > 0
    ) {
      this.emitDataAndProceed(
        false,
        this.internalAffectedPersonIdsControl.value!
      );
    } else {
      this.internalAffectedPersonIdsControl.markAsTouched();
    }
  }

  private emitDataAndProceed(
    appliesToEntirePlan: boolean,
    affectedPersonIds: number[]
  ): void {
    this.scopeSubmitted.emit({
      appliesToEntirePlan: appliesToEntirePlan,
      affectedPersonIds: affectedPersonIds,
    });
  }

  goToPreviousSubStep(): void {
    if (this.currentSubStep === 'selectPeople') {
      this.currentSubStep = 'selectScope';
    }
    // No 'previous' from selectScope, parent handles overall navigation
  }

  isPreviousSubStepAvailable(): boolean {
    return this.currentSubStep === 'selectPeople';
  }
}
