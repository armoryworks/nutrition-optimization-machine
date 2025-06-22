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
import { MatRadioGroup } from '@angular/material/radio';
import { Observable, of, startWith, map } from 'rxjs';

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
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
    MatCheckboxModule,
    MatChipsModule,
    MatIconModule,
    MatAutocompleteModule,
    MatRadioModule,
    JsonParseCommonPipe,
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

  @Output() formSubmitted = new EventEmitter<RestrictionModel[]>();
  @Output() skipStep = new EventEmitter<void>();

  restrictionForm!: FormGroup;

  ingredientSearchControl = new FormControl<string>('');
  micronutrientSearchControl = new FormControl<string>('');

  filteredCuratedIngredients!: Observable<string[]>;
  filteredMicronutrients!: Observable<string[]>;

  // Expose the enum to the template
  public RestrictionTypeEnum = RestrictionTypeEnum; // <--- ADDED THIS

  societalReligiousEthicalOptions = [
    { id: 100, name: 'Vegan' },
    { id: 101, name: 'Vegetarian' },
    { id: 102, name: 'Kosher' },
    { id: 103, name: 'Halal' },
    { id: 104, name: 'Pescatarian' },
  ];

  allergyOptions = [
    { id: 200, name: 'Peanuts' },
    { id: 201, name: 'Tree Nuts' },
    { id: 202, name: 'Dairy' },
    { id: 203, name: 'Eggs' },
    { id: 204, name: 'Wheat' },
    { id: 205, name: 'Soy' },
    { id: 206, name: 'Fish' },
    { id: 207, name: 'Shellfish' },
  ];

  allergyMedicalConditions = [
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

  gastrointestinalConditionsOptions = [
    { id: 300, name: "Crohn's Disease" },
    { id: 301, name: 'IBS' },
    { id: 302, name: 'Ulcerative Colitis' },
    { id: 303, name: 'GERD' },
  ];

  kidneyDiseaseRestrictionsOptions = [
    'Sodium',
    'Potassium',
    'Phosphorus',
    'Protein',
    'Fluids',
  ];

  spiceLevelOptions = ['Mild', 'Medium', 'Spicy', 'Very Spicy'];
  texturesDislikedOptions = [
    'Creamy',
    'Crunchy',
    'Smooth',
    'Chewy',
    'Crispy',
    'Soggy',
    'Slimy',
    'Gritty',
  ];
  preferredCookingMethodsOptions = [
    'Baked',
    'Grilled',
    'Fried',
    'Steamed',
    'Roasted',
    'Raw',
    'Sautéed',
    'Boiled',
  ];

  allCuratedIngredients: string[] = [
    'Apple',
    'Banana',
    'Carrot',
    'Dill',
    'Eggplant',
    'Fennel',
    'Garlic',
    'Ginger',
    'Honey',
    'Ice Cream',
    'Jalapeno',
    'Kale',
    'Lemon',
    'Mango',
    'Nutmeg',
    'Orange',
    'Pomegranate',
    'Quinoa',
    'Radish',
    'Spinach',
    'Tomato',
    'Ugli Fruit',
    'Vanilla',
    'Watermelon',
    'Xylitol',
    'Yam',
    'Zucchini',
  ];
  allMicronutrients: string[] = [
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
    this.restrictionForm = this.fb.group({
      appliesToEntirePlan: new FormControl(false),
      affectedPersonIds: this.fb.array([] as FormControl<number>[]),
      name: new FormControl(''),
      societalReligiousEthicalTypeIds: this.fb.array(
        [] as FormControl<number>[]
      ),
      mandatoryInclusions: this.fb.array([] as FormControl<string>[]),
      fastingSchedules: new FormControl(''),

      allergyMedicalIngredientIds: this.fb.array([] as FormControl<string>[]),
      allergyMedicalConditionIds: this.fb.array([] as FormControl<number>[]),
      gastrointestinalConditions: this.fb.array([] as FormControl<number>[]),
      kidneyDiseaseNutrientRestrictions: this.fb.array(
        [] as FormControl<string>[]
      ),
      vitaminMineralDeficiencies: this.fb.array([] as FormControl<string>[]),
      prescriptionInteractions: new FormControl(''),

      personalPreferenceSpiceLevel: new FormControl(''),
      dislikedIngredients: this.fb.array([] as FormControl<string>[]),
      dislikedTextures: this.fb.array([] as FormControl<string>[]),
      preferredCookingMethods: this.fb.array([] as FormControl<string>[]),
    });

    this.patchFormWithExistingRestrictions(this.restrictions);

    this.filteredCuratedIngredients =
      this.ingredientSearchControl.valueChanges.pipe(
        startWith(''),
        map((value) =>
          value
            ? this._filter(value, this.allCuratedIngredients)
            : this.allCuratedIngredients
        )
      );

    this.filteredMicronutrients =
      this.micronutrientSearchControl.valueChanges.pipe(
        startWith(''),
        map((value) =>
          value
            ? this._filter(value, this.allMicronutrients)
            : this.allMicronutrients
        )
      );

    if (
      this.allPersonsInPlan.length === 1 &&
      this.allPersonsInPlan[0].id === this.currentPersonId
    ) {
      const affectedPersonIdsArray = this.restrictionForm.get(
        'affectedPersonIds'
      ) as FormArray;
      if (affectedPersonIdsArray.length === 0 && this.currentPersonId !== 0) {
        affectedPersonIdsArray.push(this.fb.control(this.currentPersonId));
      }
    }

    this.restrictionService.getCuratedIngredients().subscribe((data) => {
      if (data && data.length > 0) this.allCuratedIngredients = data;
    });
    this.restrictionService.getMicronutrients().subscribe((data) => {
      if (data && data.length > 0) this.allMicronutrients = data;
    });
  }

  private patchFormWithExistingRestrictions(
    existingRestrictions: RestrictionModel[]
  ): void {
    Object.keys(this.restrictionForm.controls).forEach((key) => {
      const control = this.restrictionForm.get(key);
      if (control instanceof FormArray) {
        control.clear();
      }
    });

    existingRestrictions.forEach((res) => {
      if (res.restrictionTypeId === this.restrictionType) {
        this.restrictionForm.patchValue({
          appliesToEntirePlan: res.appliesToEntirePlan,
          name: res.name || '',
        });

        if (res.affectedPersonIds && res.affectedPersonIds.length > 0) {
          const affectedPersonIdsArray = this.restrictionForm.get(
            'affectedPersonIds'
          ) as FormArray;
          res.affectedPersonIds.forEach((id: number) =>
            affectedPersonIdsArray.push(this.fb.control(id))
          );
        } else if (
          res.personId !== null &&
          res.personId === this.currentPersonId &&
          !res.appliesToEntirePlan
        ) {
          const affectedPersonIdsArray = this.restrictionForm.get(
            'affectedPersonIds'
          ) as FormArray;
          if (!affectedPersonIdsArray.value.includes(this.currentPersonId)) {
            affectedPersonIdsArray.push(this.fb.control(this.currentPersonId));
          }
        }

        switch (this.restrictionType) {
          case RestrictionTypeEnum.SocietalReligiousEthical:
            res.societalReligiousEthicalTypeIds?.forEach((id: number) =>
              (
                this.restrictionForm.get(
                  'societalReligiousEthicalTypeIds'
                ) as FormArray
              ).push(this.fb.control(id))
            );
            res.mandatoryInclusions?.forEach((inc: string) =>
              (
                this.restrictionForm.get('mandatoryInclusions') as FormArray
              ).push(this.fb.control(inc))
            );
            this.restrictionForm.patchValue({
              fastingSchedules: res.fastingSchedules,
            });
            break;
          case RestrictionTypeEnum.AllergyMedical:
            res.allergyMedicalIngredientIds?.forEach((id: string) =>
              (
                this.restrictionForm.get(
                  'allergyMedicalIngredientIds'
                ) as FormArray
              ).push(this.fb.control(id))
            );
            res.allergyMedicalConditionIds?.forEach((id: number) =>
              (
                this.restrictionForm.get(
                  'allergyMedicalConditionIds'
                ) as FormArray
              ).push(this.fb.control(id))
            );
            res.gastrointestinalConditions?.forEach((id: number) =>
              (
                this.restrictionForm.get(
                  'gastrointestinalConditions'
                ) as FormArray
              ).push(this.fb.control(id))
            );
            res.kidneyDiseaseNutrientRestrictions?.forEach((nut: string) =>
              (
                this.restrictionForm.get(
                  'kidneyDiseaseNutrientRestrictions'
                ) as FormArray
              ).push(this.fb.control(nut))
            );
            res.vitaminMineralDeficiencies?.forEach((vit: string) =>
              (
                this.restrictionForm.get(
                  'vitaminMineralDeficiencies'
                ) as FormArray
              ).push(this.fb.control(vit))
            );
            this.restrictionForm.patchValue({
              prescriptionInteractions: res.prescriptionInteractions,
            });
            break;
          case RestrictionTypeEnum.PersonalPreference:
            this.restrictionForm.patchValue({
              personalPreferenceSpiceLevel: res.personalPreferenceSpiceLevel,
            });
            res.dislikedIngredients?.forEach((ing: string) =>
              (
                this.restrictionForm.get('dislikedIngredients') as FormArray
              ).push(this.fb.control(ing))
            );
            res.dislikedTextures?.forEach((tex: string) =>
              (this.restrictionForm.get('dislikedTextures') as FormArray).push(
                this.fb.control(tex)
              )
            );
            res.preferredCookingMethods?.forEach((meth: string) =>
              (
                this.restrictionForm.get('preferredCookingMethods') as FormArray
              ).push(this.fb.control(meth))
            );
            break;
        }
      }
    });
  }

  private _filter(value: string, options: string[]): string[] {
    const filterValue = value ? value.toLowerCase() : '';
    return options.filter((option) =>
      option.toLowerCase().includes(filterValue)
    );
  }

  addChip(event: any, formArrayName: string): void {
    const input = event.input;
    const value = (event.value || '').trim();
    if (value) {
      const formArray = this.restrictionForm.get(formArrayName) as FormArray;
      if (!formArray.value.includes(value)) {
        formArray.push(this.fb.control(value));
      }
    }
    if (input) {
      input.value = '';
    }
  }

  removeChip(chip: string, formArrayName: string): void {
    const formArray = this.restrictionForm.get(formArrayName) as FormArray;
    const index = formArray.value.indexOf(chip);
    if (index >= 0) {
      formArray.removeAt(index);
    }
  }

  // CORRECTED: onMultiSelectChange now takes 2 arguments, as per standard MatSelect
  public onMultiSelectChange(formControlName: string, event: any): void {
    // <--- Made public
    const selectedValues = event.value;
    const formArray = this.restrictionForm.get(formControlName) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: any) => {
      formArray.push(this.fb.control(value));
    });
  }

  // Helper method for checkbox changes (e.g., specific conditions)
  public onCheckboxChange(
    formControlName: string,
    value: any,
    isChecked: boolean
  ): void {
    // <--- Made public
    const formArray = this.restrictionForm.get(formControlName) as FormArray;
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

  // Helper for checking if a multi-select option is selected (used in HTML)
  public isMultiSelectOptionSelected(
    formControlName: string,
    optionValue: any
  ): boolean {
    // <--- Made public
    const formArray = this.restrictionForm.get(formControlName) as FormArray;
    return formArray.value.includes(optionValue);
  }

  // Helper getters for form arrays
  get societalReligiousEthicalTypeIdsArray(): FormArray {
    return this.restrictionForm.get(
      'societalReligiousEthicalTypeIds'
    ) as FormArray;
  }
  get mandatoryInclusionsArray(): FormArray {
    return this.restrictionForm.get('mandatoryInclusions') as FormArray;
  }
  get allergyMedicalIngredientIdsArray(): FormArray {
    return this.restrictionForm.get('allergyMedicalIngredientIds') as FormArray;
  }
  get allergyMedicalConditionIdsArray(): FormArray {
    return this.restrictionForm.get('allergyMedicalConditionIds') as FormArray;
  }
  get gastrointestinalConditionsArray(): FormArray {
    return this.restrictionForm.get('gastrointestinalConditions') as FormArray;
  }
  get kidneyDiseaseNutrientRestrictionsArray(): FormArray {
    return this.restrictionForm.get(
      'kidneyDiseaseNutrientRestrictions'
    ) as FormArray;
  }
  get vitaminMineralDeficienciesArray(): FormArray {
    return this.restrictionForm.get('vitaminMineralDeficiencies') as FormArray;
  }
  get dislikedIngredientsArray(): FormArray {
    return this.restrictionForm.get('dislikedIngredients') as FormArray;
  }
  get dislikedTexturesArray(): FormArray {
    return this.restrictionForm.get('dislikedTextures') as FormArray;
  }
  get preferredCookingMethodsArray(): FormArray {
    return this.restrictionForm.get('preferredCookingMethods') as FormArray;
  }
  get affectedPersonIdsArray(): FormArray {
    return this.restrictionForm.get('affectedPersonIds') as FormArray;
  }

  public submitForm(): void {
    // <--- Made public
    this.restrictionForm.markAllAsTouched();

    if (this.restrictionForm.valid) {
      const formValue = this.restrictionForm.value;
      const submittedRestrictions: RestrictionModel[] = [];

      const newRestriction = new RestrictionModel({
        personId: formValue.appliesToEntirePlan ? null : this.currentPersonId,
        planId: formValue.appliesToEntirePlan ? 1 : null,
        restrictionTypeId: this.restrictionType!,
        name:
          formValue.name ||
          this.getRestrictionTypeName(this.restrictionType!) + ' Custom',
        appliesToEntirePlan: formValue.appliesToEntirePlan,
        affectedPersonIds: formValue.appliesToEntirePlan
          ? []
          : formValue.affectedPersonIds || [],
        ...(!formValue.appliesToEntirePlan &&
          (!formValue.affectedPersonIds ||
            formValue.affectedPersonIds.length === 0) &&
          this.currentPersonId !== 0 && {
            affectedPersonIds: [this.currentPersonId],
          }),
      });

      switch (this.restrictionType) {
        case RestrictionTypeEnum.SocietalReligiousEthical:
          newRestriction.societalReligiousEthicalTypeIds =
            formValue.societalReligiousEthicalTypeIds;
          newRestriction.mandatoryInclusions = formValue.mandatoryInclusions;
          newRestriction.fastingSchedules = formValue.fastingSchedules;
          break;
        case RestrictionTypeEnum.AllergyMedical:
          newRestriction.allergyMedicalIngredientIds =
            formValue.allergyMedicalIngredientIds;
          newRestriction.allergyMedicalConditionIds =
            formValue.allergyMedicalConditionIds;
          newRestriction.gastrointestinalConditions =
            formValue.gastrointestinalConditions;
          newRestriction.kidneyDiseaseNutrientRestrictions =
            formValue.kidneyDiseaseNutrientRestrictions;
          newRestriction.vitaminMineralDeficiencies =
            formValue.vitaminMineralDeficiencies;
          newRestriction.prescriptionInteractions =
            formValue.prescriptionInteractions;
          break;
        case RestrictionTypeEnum.PersonalPreference:
          newRestriction.personalPreferenceSpiceLevel =
            formValue.personalPreferenceSpiceLevel;
          newRestriction.dislikedIngredients = formValue.dislikedIngredients;
          newRestriction.dislikedTextures = formValue.dislikedTextures;
          newRestriction.preferredCookingMethods =
            formValue.preferredCookingMethods;
          break;
      }

      submittedRestrictions.push(newRestriction);
      this.formSubmitted.emit(submittedRestrictions);
    } else {
      this.formSubmitted.error(
        'Restriction form is invalid. Please correct the errors.'
      );
      console.error('Restriction form is invalid. Please correct the errors.');
    }
  }

  public getRestrictionTypeName(type: RestrictionTypeEnum): string {
    // <--- Made public
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

  onSkip(): void {
    this.skipStep.emit();
  }
}
