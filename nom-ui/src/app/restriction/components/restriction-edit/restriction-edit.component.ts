import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
} from '@angular/core';
import {
  FormGroup,
  FormControl,
  FormArray,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatRadioModule } from '@angular/material/radio';
import { Observable, of, startWith, map } from 'rxjs';

// Import new child restriction components
import { SocietalRestrictionComponent } from '../societal-restriction/societal-restriction.component';
import { MedicalRestrictionComponent } from '../medical-restriction/medical-restriction.component';
import { PersonalPreferenceComponent } from '../personal-preference/personal-preference.component'; // Renamed component

import { RestrictionModel } from '../../models/restriction.model';
import { RestrictionService } from '../../services/restriction.service';
import { PersonModel } from '../../../person/models/person.model';
import { RestrictionTypeEnum } from '../../enums/restriction-type.enum';
import { JsonParseCommonPipe } from '../../../common/pipes/json-parse-common.pipe';

@Component({
  selector: 'app-restriction-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatSelectModule, // Still needed for appliesToEntirePlan
    MatIconModule, // Still needed for submit icon
    MatFormFieldModule, // Still needed for appliesToEntirePlan
    // Import the new child components
    SocietalRestrictionComponent,
    MedicalRestrictionComponent,
    PersonalPreferenceComponent,
  ],
  templateUrl: './restriction-edit.component.html',
  styleUrls: ['./restriction-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class RestrictionEditComponent implements OnInit {
  @Input() restrictions: RestrictionModel[] = [];
  @Input() currentPersonId: number = 0;
  @Input() allPersonsInPlan: PersonModel[] = [];
  @Input() restrictionType: RestrictionTypeEnum | null = null;
  // NEW: Input for whether the restriction applies to the entire plan,
  // this is now managed by OnboardingWorkflowComponent
  @Input() appliesToEntirePlan: boolean = false;
  // NEW: Input for explicitly affected person IDs (if appliesToEntirePlan is false)
  @Input() affectedPersonIds: number[] = [];

  @Output() formSubmitted = new EventEmitter<RestrictionModel>(); // Changed to single RestrictionModel
  @Output() skipStep = new EventEmitter<void>();

  restrictionForm!: FormGroup;

  // Expose the enum to the template
  public RestrictionTypeEnum = RestrictionTypeEnum;

  constructor(
    private fb: NonNullableFormBuilder,
    private restrictionService: RestrictionService
  ) {}

  ngOnInit(): void {
    this.restrictionForm = this.fb.group({
      name: new FormControl(''), // Generic name for the restriction

      // Nested FormGroups for each restriction type
      societalRestrictionForm: this.fb.group({
        societalReligiousEthicalTypeIds: this.fb.array(
          [] as FormControl<number>[]
        ),
        mandatoryInclusions: this.fb.array([] as FormControl<string>[]),
        fastingSchedules: new FormControl(''),
      }),
      medicalRestrictionForm: this.fb.group({
        allergyMedicalIngredientIds: this.fb.array([] as FormControl<string>[]),
        allergyMedicalConditionIds: this.fb.array([] as FormControl<number>[]),
        gastrointestinalConditions: this.fb.array([] as FormControl<number>[]),
        kidneyDiseaseNutrientRestrictions: this.fb.array(
          [] as FormControl<string>[]
        ),
        vitaminMineralDeficiencies: this.fb.array([] as FormControl<string>[]),
        prescriptionInteractions: new FormControl(''),
      }),
      personalPreferenceForm: this.fb.group({
        personalPreferenceSpiceLevel: new FormControl(''),
        dislikedIngredients: this.fb.array([] as FormControl<string>[]),
        dislikedTextures: this.fb.array([] as FormControl<string>[]),
        preferredCookingMethods: this.fb.array([] as FormControl<string>[]),
      }),
    });

    this.patchFormWithExistingRestrictions(this.restrictions);
  }

  private patchFormWithExistingRestrictions(
    existingRestrictions: RestrictionModel[]
  ): void {
    // Clear all FormArrays within the nested form groups initially
    Object.keys(this.restrictionForm.controls).forEach((key) => {
      const control = this.restrictionForm.get(key);
      if (control instanceof FormGroup) {
        Object.keys(control.controls).forEach((subKey) => {
          const subControl = control.get(subKey);
          if (subControl instanceof FormArray) {
            subControl.clear();
          }
        });
      }
    });

    existingRestrictions.forEach((res) => {
      if (res.restrictionTypeId === this.restrictionType) {
        // Patch the generic name
        this.restrictionForm.patchValue({
          name: res.name || '',
        });

        // Patch the relevant nested FormGroup
        switch (this.restrictionType) {
          case RestrictionTypeEnum.SocietalReligiousEthical:
            const societalGroup = this.restrictionForm.get(
              'societalRestrictionForm'
            ) as FormGroup;
            res.societalReligiousEthicalTypeIds?.forEach((id: number) =>
              (
                societalGroup.get(
                  'societalReligiousEthicalTypeIds'
                ) as FormArray
              ).push(this.fb.control(id))
            );
            res.mandatoryInclusions?.forEach((inc: string) =>
              (societalGroup.get('mandatoryInclusions') as FormArray).push(
                this.fb.control(inc)
              )
            );
            societalGroup.patchValue({
              fastingSchedules: res.fastingSchedules,
            });
            break;
          case RestrictionTypeEnum.AllergyMedical:
            const medicalGroup = this.restrictionForm.get(
              'medicalRestrictionForm'
            ) as FormGroup;
            res.allergyMedicalIngredientIds?.forEach((id: string) =>
              (
                medicalGroup.get('allergyMedicalIngredientIds') as FormArray
              ).push(this.fb.control(id))
            );
            res.allergyMedicalConditionIds?.forEach((id: number) =>
              (
                medicalGroup.get('allergyMedicalConditionIds') as FormArray
              ).push(this.fb.control(id))
            );
            res.gastrointestinalConditions?.forEach((id: number) =>
              (
                medicalGroup.get('gastrointestinalConditions') as FormArray
              ).push(this.fb.control(id))
            );
            res.kidneyDiseaseNutrientRestrictions?.forEach((nut: string) =>
              (
                medicalGroup.get(
                  'kidneyDiseaseNutrientRestrictions'
                ) as FormArray
              ).push(this.fb.control(nut))
            );
            res.vitaminMineralDeficiencies?.forEach((vit: string) =>
              (
                medicalGroup.get('vitaminMineralDeficiencies') as FormArray
              ).push(this.fb.control(vit))
            );
            medicalGroup.patchValue({
              prescriptionInteractions: res.prescriptionInteractions,
            });
            break;
          case RestrictionTypeEnum.PersonalPreference:
            const personalGroup = this.restrictionForm.get(
              'personalPreferenceForm'
            ) as FormGroup;
            personalGroup.patchValue({
              personalPreferenceSpiceLevel: res.personalPreferenceSpiceLevel,
            });
            res.dislikedIngredients?.forEach((ing: string) =>
              (personalGroup.get('dislikedIngredients') as FormArray).push(
                this.fb.control(ing)
              )
            );
            res.dislikedTextures?.forEach((tex: string) =>
              (personalGroup.get('dislikedTextures') as FormArray).push(
                this.fb.control(tex)
              )
            );
            res.preferredCookingMethods?.forEach((meth: string) =>
              (personalGroup.get('preferredCookingMethods') as FormArray).push(
                this.fb.control(meth)
              )
            );
            break;
        }
      }
    });
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

  // --- NEW GETTERS FOR NESTED FORM GROUPS ---
  get societalRestrictionFormGroup(): FormGroup {
    return this.restrictionForm.get('societalRestrictionForm') as FormGroup;
  }

  get medicalRestrictionFormGroup(): FormGroup {
    return this.restrictionForm.get('medicalRestrictionForm') as FormGroup;
  }

  get personalPreferenceFormGroup(): FormGroup {
    return this.restrictionForm.get('personalPreferenceForm') as FormGroup;
  }
  // --- END NEW GETTERS ---

  public submitForm(): void {
    // Mark the relevant nested form group as touched to trigger validation messages
    let formGroupToValidate: FormGroup | null = null;
    let submittedFormValue: any = {};

    switch (this.restrictionType) {
      case RestrictionTypeEnum.SocietalReligiousEthical:
        formGroupToValidate = this.societalRestrictionFormGroup; // Use getter
        submittedFormValue = formGroupToValidate.value;
        break;
      case RestrictionTypeEnum.AllergyMedical:
        formGroupToValidate = this.medicalRestrictionFormGroup; // Use getter
        submittedFormValue = formGroupToValidate.value;
        break;
      case RestrictionTypeEnum.PersonalPreference:
        formGroupToValidate = this.personalPreferenceFormGroup; // Use getter
        submittedFormValue = formGroupToValidate.value;
        break;
      default:
        console.error('Unknown restriction type for submission.');
        this.formSubmitted.error('Unknown restriction type.');
        return;
    }

    if (formGroupToValidate) {
      formGroupToValidate.markAllAsTouched();
    }

    if (formGroupToValidate && formGroupToValidate.valid) {
      const newRestriction = new RestrictionModel({
        // These are now provided as Inputs from OnboardingWorkflowComponent
        personId: this.appliesToEntirePlan ? null : this.currentPersonId,
        planId: this.appliesToEntirePlan ? 1 : null, // Mock planId 1 if applies to plan, otherwise null
        restrictionTypeId: this.restrictionType!,
        name: this.getRestrictionTypeName(this.restrictionType!) + ' Custom', // Name can be refined later if needed

        // Populate type-specific properties from the relevant nested form group
        ...submittedFormValue,

        // Ensure affectedPersonIds is explicitly set based on input from parent
        affectedPersonIds: this.appliesToEntirePlan
          ? []
          : this.affectedPersonIds,
        appliesToEntirePlan: this.appliesToEntirePlan, // Pass this original flag back for consistency with DTO
      });

      this.formSubmitted.emit(newRestriction);
    } else {
      this.formSubmitted.error(
        'Restriction form is invalid. Please correct the errors.'
      );
      console.error('Restriction form is invalid. Please correct the errors.');
    }
  }

  onSkip(): void {
    this.skipStep.emit();
  }
}
