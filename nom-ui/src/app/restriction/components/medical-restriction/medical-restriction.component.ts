import { Component, OnInit, Input } from '@angular/core';
import {
  FormGroup,
  FormControl,
  FormArray,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { Observable, startWith, map } from 'rxjs';
import { RestrictionService } from '../../services/restriction.service';


@Component({
  selector: 'app-medical-restriction',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatChipsModule,
    MatIconModule,
    MatAutocompleteModule,
  ],
  templateUrl: './medical-restriction.component.html',
  styleUrls: ['./medical-restriction.component.scss'],
})
export class MedicalRestrictionComponent implements OnInit {
  @Input() medicalRestrictionForm!: FormGroup; // Input FormGroup for this section

  // FormControls for autocomplete inputs
  public micronutrientSearchControl = new FormControl<string>('');
  public filteredMicronutrients!: Observable<string[]>;

  // Options for select dropdowns and checkboxes
  public allergyOptions = [
    { id: 200, name: 'Peanuts' },
    { id: 201, name: 'Tree Nuts' },
    { id: 202, name: 'Dairy' },
    { id: 203, name: 'Eggs' },
    { id: 204, name: 'Wheat' },
    { id: 205, name: 'Soy' },
    { id: 206, name: 'Fish' },
    { id: 207, name: 'Shellfish' },
  ];

  public allergyMedicalConditions = [
    { id: 200, name: 'Celiac Disease' },
    { id: 201, name: 'Lactose Intolerance' },
    { id: 202, name: 'Diabetes Type 1' },
    { id: 203, name: 'Diabetes Type 2' },
    { id: 204, name: 'High Blood Pressure' },
    { id: 205, name: 'High Cholesterol' },
    { id: 206, name: 'Gout' },
    { id: 207, name: 'Anemia' },
    { id: 208, name: 'Pregnancy' },
  ];

  public gastrointestinalConditionsOptions = [
    { id: 300, name: "Crohn's Disease" },
    { id: 301, name: 'IBS' },
    { id: 302, name: 'Ulcerative Colitis' },
    { id: 303, name: 'GERD' },
  ];

  public kidneyDiseaseRestrictionsOptions = [
    'Sodium',
    'Potassium',
    'Phosphorus',
    'Protein',
    'Fluids',
  ];

  private allMicronutrients: string[] = [
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

  constructor(
    private fb: NonNullableFormBuilder,
    private restrictionService: RestrictionService
  ) {}

  ngOnInit(): void {
    if (!this.medicalRestrictionForm) {
      console.warn(
        'MedicalRestrictionComponent: medicalRestrictionForm input is missing. Initializing a default one.'
      );
      this.medicalRestrictionForm = this.fb.group({
        allergyMedicalIngredientIds: this.fb.array([] as FormControl<string>[]),
        allergyMedicalConditionIds: this.fb.array([] as FormControl<number>[]),
        gastrointestinalConditions: this.fb.array([] as FormControl<number>[]),
        kidneyDiseaseNutrientRestrictions: this.fb.array(
          [] as FormControl<string>[]
        ),
        vitaminMineralDeficiencies: this.fb.array([] as FormControl<string>[]),
        prescriptionInteractions: new FormControl(''),
      });
    }

    this.filteredMicronutrients =
      this.micronutrientSearchControl.valueChanges.pipe(
        startWith(''),
        map((value) =>
          value
            ? this._filter(value, this.allMicronutrients)
            : this.allMicronutrients
        )
      );

    // Fetch real data (mocked for now)
    this.restrictionService.getMicronutrients().subscribe((data) => {
      if (data && data.length > 0) this.allMicronutrients = data;
    });
  }

  private _filter(value: string, options: string[]): string[] {
    const filterValue = value ? value.toLowerCase() : '';
    return options.filter((option) =>
      option.toLowerCase().includes(filterValue)
    );
  }

  public addChip(event: any, formArrayName: string): void {
    const input = event.input;
    const value = (event.value || '').trim();
    if (value) {
      const formArray = this.medicalRestrictionForm.get(
        formArrayName
      ) as FormArray;
      if (!formArray.value.includes(value)) {
        formArray.push(this.fb.control(value));
      }
    }
    if (input) {
      input.value = '';
    }
  }

  public removeChip(chip: string, formArrayName: string): void {
    const formArray = this.medicalRestrictionForm.get(
      formArrayName
    ) as FormArray;
    const index = formArray.value.indexOf(chip);
    if (index >= 0) {
      formArray.removeAt(index);
    }
  }

  public onMultiSelectChange(formControlName: string, event: any): void {
    const selectedValues = event.value;
    const formArray = this.medicalRestrictionForm.get(
      formControlName
    ) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: any) => {
      formArray.push(this.fb.control(value));
    });
  }

  public onCheckboxChange(
    formControlName: string,
    value: any,
    isChecked: boolean
  ): void {
    const formArray = this.medicalRestrictionForm.get(
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
    optionValue: any
  ): boolean {
    const formArray = this.medicalRestrictionForm.get(
      formControlName
    ) as FormArray;
    return formArray.value.includes(optionValue);
  }

  // Getters for FormArrays
  get allergyMedicalIngredientIdsArray(): FormArray {
    return this.medicalRestrictionForm.get(
      'allergyMedicalIngredientIds'
    ) as FormArray;
  }
  get allergyMedicalConditionIdsArray(): FormArray {
    return this.medicalRestrictionForm.get(
      'allergyMedicalConditionIds'
    ) as FormArray;
  }
  get gastrointestinalConditionsArray(): FormArray {
    return this.medicalRestrictionForm.get(
      'gastrointestinalConditions'
    ) as FormArray;
  }
  get kidneyDiseaseNutrientRestrictionsArray(): FormArray {
    return this.medicalRestrictionForm.get(
      'kidneyDiseaseNutrientRestrictions'
    ) as FormArray;
  }
  get vitaminMineralDeficienciesArray(): FormArray {
    return this.medicalRestrictionForm.get(
      'vitaminMineralDeficiencies'
    ) as FormArray;
  }
}
