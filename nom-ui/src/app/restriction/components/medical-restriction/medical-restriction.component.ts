import { Component, OnInit, input, inject, OnDestroy } from '@angular/core';
import {
  FormGroup,
  FormControl,
  FormArray,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { AmwSelectComponent, AmwCheckboxComponent, AmwTextareaComponent, AmwChipInputComponent } from 'angular-material-wrap';
import { Subject, takeUntil } from 'rxjs';
import { RestrictionService } from '../../services/restriction.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';


@Component({
  selector: 'nom-medical-restriction',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwSelectComponent,
    AmwCheckboxComponent,
    AmwTextareaComponent,
    AmwChipInputComponent,
  ],
  templateUrl: './medical-restriction.component.html',
  styleUrls: ['./medical-restriction.component.scss'],
})
export class MedicalRestrictionComponent implements OnInit, OnDestroy {
  private fb = inject(NonNullableFormBuilder);
  private restrictionService = inject(RestrictionService);
  private referenceDataService = inject(ReferenceDataService);

  medicalRestrictionForm = input.required<FormGroup>(); // Input FormGroup for this section

  // FormControl for chip input - syncs with FormArray
  public vitaminMineralDeficienciesControl = new FormControl<{ value: string; label: string }[]>([]);

  private destroy$ = new Subject<void>();

  // Options for select dropdowns and checkboxes - loaded from backend
  public allergyOptions: ReferenceItemModel[] = [];
  public allergyMedicalConditions: ReferenceItemModel[] = [];
  public gastrointestinalConditionsOptions: ReferenceItemModel[] = [];

  public kidneyDiseaseRestrictionsOptions = [
    'Sodium',
    'Potassium',
    'Phosphorus',
    'Protein',
    'Fluids',
  ];

  private _allMicronutrients: string[] = [
    'Vitamin A',
    'Vitamin B1 (Thiamine)',
    'Vitamin B2 (Riboflavin)',
    'Vitamin B3 (Niacin)',
    'Vitamin B5 (Pantothenic Acid)',
    'Vitamin B6 (Pyridoxine)',
    'Vitamin B7 (Biotin)',
    'Vitamin B9 (Folate)',
    'Vitamin B12 (Cobalamin)',
    'Vitamin C',
    'Vitamin D',
    'Vitamin E',
    'Vitamin K',
    'Calcium',
    'Chloride',
    'Chromium',
    'Copper',
    'Fluoride',
    'Iodine',
    'Iron',
    'Magnesium',
    'Manganese',
    'Molybdenum',
    'Phosphorus',
    'Potassium',
    'Selenium',
    'Sodium',
    'Zinc',
  ];

  // Getter that converts strings to ChipInputOption format
  get allMicronutrientOptions(): { value: string; label: string }[] {
    return this._allMicronutrients.map(item => ({ value: item, label: item }));
  }


  ngOnInit(): void {
    // Fetch real data (mocked for now)
    this.restrictionService.getMicronutrients()
      .pipe(takeUntil(this.destroy$))
      .subscribe((data) => {
        if (data && data.length > 0) this._allMicronutrients = data;
      });

    this.loadMedicalRestrictionOptions();
    this.setupChipSync();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private setupChipSync(): void {
    // Sync chip input control with FormArray
    this.vitaminMineralDeficienciesControl.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((values) => {
        const formArray = this.vitaminMineralDeficienciesArray;
        formArray.clear();
        (values || []).forEach((chip: { value: string; label: string }) => {
          formArray.push(this.fb.control(chip.value));
        });
      });

    // Initialize control with existing FormArray values (convert strings to ChipInputOption)
    const initialValues = this.vitaminMineralDeficienciesArray.value.map((v: string) => ({ value: v, label: v }));
    this.vitaminMineralDeficienciesControl.setValue(initialValues);
  }

  private loadMedicalRestrictionOptions(): void {
    // Load medical restriction options from backend
    // Using different reference groups for different types of medical restrictions
    this.referenceDataService.getReferencesByGroup(2000).subscribe({
      next: (options) => {
        // For now, we'll use the same group for all medical restrictions
        // In a real implementation, you might have separate groups for allergies, conditions, etc.
        this.allergyOptions = options;
        this.allergyMedicalConditions = options;
        this.gastrointestinalConditionsOptions = options;
      },
      error: (error) => {
        console.error('Error loading medical restriction options:', error);
        // Fallback to empty arrays if API fails
        this.allergyOptions = [];
        this.allergyMedicalConditions = [];
        this.gastrointestinalConditionsOptions = [];
      }
    });
  }

  public onMultiSelectChange(formControlName: string, event: { value: string[] }): void {
    const selectedValues = event.value;
    const formArray = this.medicalRestrictionForm().get(
      formControlName
    ) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: string) => {
      formArray.push(this.fb.control(value));
    });
  }

  public onCheckboxChange(
    formControlName: string,
    value: string,
    isChecked: boolean
  ): void {
    const formArray = this.medicalRestrictionForm().get(
      formControlName
    ) as FormArray;
    if (isChecked) {
      if (!formArray.value.includes(value)) {
        formArray.push(this.fb.control(value));
      }
    } else {
      const index = formArray.value.indexOf(value);
      if (index >= 0) {
        formArray.removeAt(index);
      }
    }
  }

  public isMultiSelectOptionSelected(
    formControlName: string,
    optionValue: string
  ): boolean {
    const formArray = this.medicalRestrictionForm().get(
      formControlName
    ) as FormArray;
    return formArray.value.includes(optionValue);
  }

  // Getters for FormArrays
  get allergyMedicalIngredientIdsArray(): FormArray {
    return this.medicalRestrictionForm().get(
      'allergyMedicalIngredientIds'
    ) as FormArray;
  }
  get allergyMedicalConditionIdsArray(): FormArray {
    return this.medicalRestrictionForm().get(
      'allergyMedicalConditionIds'
    ) as FormArray;
  }
  get gastrointestinalConditionsArray(): FormArray {
    return this.medicalRestrictionForm().get(
      'gastrointestinalConditions'
    ) as FormArray;
  }
  get kidneyDiseaseNutrientRestrictionsArray(): FormArray {
    return this.medicalRestrictionForm().get(
      'kidneyDiseaseNutrientRestrictions'
    ) as FormArray;
  }
  get vitaminMineralDeficienciesArray(): FormArray {
    return this.medicalRestrictionForm().get(
      'vitaminMineralDeficiencies'
    ) as FormArray;
  }

  // Helper methods for AMW select options
  getAllergyOptions(): { value: string; label: string }[] {
    return this.allergyOptions.map(option => ({
      value: option.referenceName ?? '',
      label: option.referenceName ?? ''
    }));
  }

  getGIConditionOptions(): { value: number; label: string }[] {
    return this.gastrointestinalConditionsOptions.map(option => ({
      value: option.referenceId ?? 0,
      label: option.referenceName ?? ''
    }));
  }

  getKidneyRestrictionOptions(): { value: string; label: string }[] {
    return this.kidneyDiseaseRestrictionsOptions.map(option => ({
      value: option,
      label: option
    }));
  }
}
