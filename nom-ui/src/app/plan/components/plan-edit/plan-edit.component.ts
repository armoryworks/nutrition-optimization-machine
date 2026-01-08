import { Component, OnInit, input, output, ViewEncapsulation, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import {
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';

import { PersonModel } from '../../../person/models/person.model';
import { PlanModel, RestrictionModel } from '../../models/plan.model';
import { RestrictionTypeEnum } from '../../../restriction/enums/restriction-type.enum'; // Import RestrictionTypeEnum

@Component({
  selector: 'nom-plan-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule
  ],
  templateUrl: './plan-edit.component.html',
  styleUrls: ['./plan-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PlanEditComponent implements OnInit {
  private fb = inject(NonNullableFormBuilder);

  plan = input<PlanModel | undefined>(undefined);
  allPersonsInPlan = input<PersonModel[]>([]); // Assuming PersonModel is available
  currentPersonId = input(0); // Assuming currentPersonId is passed for individual restrictions

  formSubmitted = output<PlanModel>();
  back = output<void>();

  planForm: FormGroup = this.fb.group({
    name: new FormControl('', Validators.required),
    description: new FormControl(''),
    invitationCode: new FormControl(''),
    // No longer managing restrictions or participants directly here,
    // but will collect them from child components if needed
  });

  // Track the type of restriction being edited to pass to RestrictionEditComponent
  currentRestrictionType: RestrictionTypeEnum | undefined = undefined;

  // Temporary list to hold restrictions before final submission
  tempRestrictions: RestrictionModel[] = [];

  // Expose enum to template
  public RestrictionTypeEnum = RestrictionTypeEnum;

  constructor() {
    // Form is now initialized at declaration
  }

  ngOnInit(): void {
    // Update form values based on input plan
    if (this.plan()) {
      this.planForm.patchValue({
        name: this.plan()!.name || '',
        description: this.plan()!.description || '',
        invitationCode: this.plan()!.invitationCode || '',
      });
    }

    // Initialize tempRestrictions with any existing restrictions from the input plan
    if (this.plan()?.restrictions) {
      this.tempRestrictions = [...this.plan()!.restrictions];
    }
  }

  public getPersonName(personId: number | null, defaultRetVal: string): string {
    return (
      this.allPersonsInPlan().find((p) => p.id === personId)?.name ||
      defaultRetVal
    );
  }

  public selectRestrictionType(type: RestrictionTypeEnum): void {
    this.currentRestrictionType = type;
  }

  public getRestrictionTypeName(type: RestrictionTypeEnum): string {
    switch (type) {
      case RestrictionTypeEnum.SocietalReligiousEthical:
        return 'Dietary Practice';
      case RestrictionTypeEnum.AllergyMedical:
        return 'Medical Restriction';
      case RestrictionTypeEnum.PersonalPreference:
        return 'Personal Preference';
      default:
        return 'Restriction';
    }
  }

  public onRestrictionsSubmitted(restriction: RestrictionModel): void {
    // This logic needs to manage adding/updating the single restriction in the temporary list
    const index = this.tempRestrictions.findIndex(
      (r) =>
        r.restrictionType === restriction.restrictionType &&
        r.name === restriction.name
    );

    if (index !== -1) {
      this.tempRestrictions[index] = restriction; // Update existing
    } else {
      this.tempRestrictions.push(restriction); // Add new
    }

    this.currentRestrictionType = undefined; // Exit restriction editing mode
  }

  public submitForm(): void {
    this.planForm.markAllAsTouched();
    if (this.planForm.valid) {
      const updatedPlan: PlanModel = {
        ...this.plan()!, // Start with existing plan data
        name: this.planForm.get('name')?.value,
        description: this.planForm.get('description')?.value,
        invitationCode: this.planForm.get('invitationCode')?.value,
        restrictions: this.tempRestrictions, // Attach the collected restrictions
        // participants: [], // Participants would be managed by other means or collected similarly
      };
      this.formSubmitted.emit(updatedPlan);
    }
  }

  public goBack(): void {
    this.back.emit();
  }

  // A getter to provide restrictions for the RestrictionEditComponent
  // It should filter based on what the RestrictionEditComponent is currently editing
  get restrictionsForEditComponent(): RestrictionModel[] {
    if (this.currentRestrictionType === undefined) {
      return [];
    }
    // Filter for current type
    return this.tempRestrictions.filter(
      (r) => r.restrictionType === this.getRestrictionTypeName(this.currentRestrictionType!)
    );
  }
}
