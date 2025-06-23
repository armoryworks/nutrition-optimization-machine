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
  FormArray,
} from '@angular/forms';

import { PersonModel } from '../../../person/models/person.model';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component';
import { RestrictionTypeEnum } from '../../../restriction/enums/restriction-type.enum'; // Import RestrictionTypeEnum
import { PlanModel } from '../../models/plan.model';

@Component({
  selector: 'app-plan-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    RestrictionEditComponent, // Ensure RestrictionEditComponent is imported
  ],
  templateUrl: './plan-edit.component.html',
  styleUrls: ['./plan-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PlanEditComponent implements OnInit {
  @Input() plan: PlanModel | null = null;
  @Input() allPersonsInPlan: PersonModel[] = []; // Assuming PersonModel is available
  @Input() currentPersonId: number = 0; // Assuming currentPersonId is passed for individual restrictions

  @Output() formSubmitted = new EventEmitter<PlanModel>();
  @Output() back = new EventEmitter<void>();

  planForm!: FormGroup;

  // Track the type of restriction being edited to pass to RestrictionEditComponent
  currentRestrictionType: RestrictionTypeEnum | null = null;

  // Temporary list to hold restrictions before final submission
  tempRestrictions: RestrictionModel[] = [];

  // Expose enum to template
  public RestrictionTypeEnum = RestrictionTypeEnum;

  constructor(private fb: NonNullableFormBuilder) {}

  ngOnInit(): void {
    this.planForm = this.fb.group({
      name: new FormControl(this.plan?.name || '', Validators.required),
      description: new FormControl(this.plan?.description || ''),
      invitationCode: new FormControl(this.plan?.invitationCode || ''),
      // No longer managing restrictions or participants directly here,
      // but will collect them from child components if needed
    });

    // Initialize tempRestrictions with any existing restrictions from the input plan
    if (this.plan?.restrictions) {
      this.tempRestrictions = [...this.plan.restrictions];
    }
  }

  public getPersonName(personId: number | null, defaultRetVal: string): string {
    return (
      this.allPersonsInPlan.find((p) => p.id === personId)?.name ||
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
    // <--- CHANGED FROM RestrictionModel[] to RestrictionModel
    // This logic needs to manage adding/updating the single restriction in the temporary list
    const index = this.tempRestrictions.findIndex(
      (r) =>
        r.restrictionTypeId === restriction.restrictionTypeId &&
        r.personId === restriction.personId &&
        r.planId === restriction.planId
    );

    if (index !== -1) {
      this.tempRestrictions[index] = restriction; // Update existing
    } else {
      this.tempRestrictions.push(restriction); // Add new
    }

    this.currentRestrictionType = null; // Exit restriction editing mode
  }

  public submitForm(): void {
    this.planForm.markAllAsTouched();
    if (this.planForm.valid) {
      const updatedPlan: PlanModel = {
        ...this.plan!, // Start with existing plan data
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
    if (this.currentRestrictionType === null) {
      return [];
    }
    // Filter for current type and for the current person/plan scope
    return this.tempRestrictions.filter(
      (r) =>
        r.restrictionTypeId === this.currentRestrictionType &&
        (r.personId === this.currentPersonId || r.planId !== null) // Assuming planId != null means it's a plan-wide restriction
    );
  }
}
