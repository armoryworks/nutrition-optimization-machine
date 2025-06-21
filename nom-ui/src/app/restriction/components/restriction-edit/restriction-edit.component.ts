import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
} from '@angular/core';
import {
  FormArray,
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select'; // For multi-selects
import { RestrictionModel } from '../../models/restriction.model';
import { PersonModel } from '../../../person/models/person.model'; // To display affected persons
import { RestrictionService } from '../../services/restriction.service';
import { JsonParseCommonPipe } from '../../../common/pipes/json-parse-common.pipe'; // For displaying selected items
import { Observable, combineLatest, of } from 'rxjs';
import { map, startWith } from 'rxjs/operators';
import { RestrictionTypeEnum } from '../../enums/restriction-type.enum';

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
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatSelectModule,
    JsonParseCommonPipe,
  ],
  templateUrl: './restriction-edit.component.html',
  styleUrls: ['./restriction-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class RestrictionEditComponent implements OnInit {
  // Input: The current set of restrictions being edited for this person/plan
  @Input() restrictions: RestrictionModel[] = [];
  // Input: The ID of the person currently being onboarded (0 if for a plan or primary person)
  @Input() currentPersonId: number = 0;
  // Input: All available persons in the plan, for multi-select affected individuals
  @Input() allPersonsInPlan: PersonModel[] = [];

  // Output: Emits the updated list of restrictions when the user clicks "Next" or "Submit"
  @Output() formSubmitted = new EventEmitter<RestrictionModel[]>();
  // Output: Emits when the user explicitly clicks "Skip" for this restriction type section
  @Output() skipStep = new EventEmitter<void>();

  restrictionForm!: FormGroup;

  // Predefined lists for various restriction sub-types
  societalReligiousEthicalOptions: string[] = [
    'Kosher',
    'Halal',
    'Vegetarian',
    'Vegan',
    'Pescatarian',
    'Pollotarian',
    'Flexitarian',
    'Paleo',
    'Keto',
    'Mediterranean',
    'Dash Diet',
  ];
  allergyOptions: string[] = [
    'Peanuts',
    'Tree Nuts',
    'Dairy',
    'Eggs',
    'Soy',
    'Wheat',
    'Fish',
    'Shellfish',
    'Sesame',
    'Corn',
    'Sulfites',
  ];
  gastrointestinalConditionsOptions: string[] = [
    "Crohn's",
    'IBS',
    'Leaky Gut',
    'GERD',
  ];
  kidneyDiseaseRestrictionsOptions: string[] = [
    'sodium',
    'potassium',
    'phosphorus',
    'protein',
    'fluids',
  ];
  spiceLevelOptions: string[] = [
    'None',
    'Mild',
    'Medium',
    'Spicy',
    'Very Spicy',
  ];
  texturesDislikedOptions: string[] = [
    'Mushy',
    'Slimy',
    'Gritty',
    'Chewy',
    'Soggy',
    'Crunchy',
  ];
  preferredCookingMethodsOptions: string[] = [
    'Grilled',
    'Baked',
    'Roasted',
    'Stir-fried',
    'Slow-cooked',
    'Pressure cooked',
    'Raw',
  ];

  // Observables for dynamic searchable lists
  filteredIngredients$: Observable<string[]> = of([]);
  filteredMicronutrients$: Observable<string[]> = of([]);
  ingredientSearchControl = new FormControl<string>('');
  micronutrientSearchControl = new FormControl<string>('');

  // Local state to track which restriction questions received affirmative answers
  // This will dynamically control the "Applies to everyone / specific individuals" follow-up questions
  // Stored as Map<RestrictionName, { appliesToEntirePlan: boolean, affectedPersonIds: number[] }>
  private affirmativeRestrictionAnswers: Map<
    string,
    { appliesToEntirePlan: boolean; affectedPersonIds: number[] }
  > = new Map();

  // Internal list of specific restrictions that have received affirmative answers
  // Used to iterate and display the conditional allocation questions
  affirmativeRestrictionsList: { name: string; typeId: number }[] = [];

  constructor(
    private fb: NonNullableFormBuilder,
    private restrictionService: RestrictionService
  ) {}

  ngOnInit(): void {
    // Initialize the form with FormControls for each restriction sub-type.
    // Use an empty string for text, false for boolean, and JSON.stringify([]) for multi/single selects.
    this.restrictionForm = this.fb.group({
      societalReligiousEthical: [
        JSON.stringify(
          this.getInitialValue(
            'societalReligiousEthical',
            RestrictionTypeEnum.SocietalReligiousEthical,
            this.societalReligiousEthicalOptions
          )
        ),
        Validators.required,
      ],
      fastingSchedule: [
        this.getInitialValue(
          'fastingSchedule',
          RestrictionTypeEnum.SocietalReligiousEthical
        ),
      ],
      mandatoryInclusions: [
        JSON.stringify(
          this.getInitialValue(
            'mandatoryInclusions',
            RestrictionTypeEnum.SocietalReligiousEthical,
            []
          )
        ),
        Validators.required,
      ],
      allergies: [
        JSON.stringify(
          this.getInitialValue(
            'allergies',
            RestrictionTypeEnum.AllergyMedical,
            this.allergyOptions
          )
        ),
        Validators.required,
      ],
      celiacOrGlutenSensitivity: [
        this.getInitialValue(
          'celiacOrGlutenSensitivity',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      lactoseIntolerance: [
        this.getInitialValue(
          'lactoseIntolerance',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      type1Diabetes: [
        this.getInitialValue(
          'type1Diabetes',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      type2Diabetes: [
        this.getInitialValue(
          'type2Diabetes',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      highBloodPressure: [
        this.getInitialValue(
          'highBloodPressure',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      highCholesterol: [
        this.getInitialValue(
          'highCholesterol',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      gastrointestinalConditions: [
        JSON.stringify(
          this.getInitialValue(
            'gastrointestinalConditions',
            RestrictionTypeEnum.AllergyMedical,
            this.gastrointestinalConditionsOptions
          )
        ),
        Validators.required,
      ],
      kidneyDiseaseRestrictions: [
        JSON.stringify(
          this.getInitialValue(
            'kidneyDiseaseRestrictions',
            RestrictionTypeEnum.AllergyMedical,
            this.kidneyDiseaseRestrictionsOptions
          )
        ),
        Validators.required,
      ],
      gout: [this.getInitialValue('gout', RestrictionTypeEnum.AllergyMedical)],
      anemia: [
        this.getInitialValue('anemia', RestrictionTypeEnum.AllergyMedical),
      ],
      pregnancy: [
        this.getInitialValue('pregnancy', RestrictionTypeEnum.AllergyMedical),
      ],
      vitaminMineralDeficiencies: [
        JSON.stringify(
          this.getInitialValue(
            'vitaminMineralDeficiencies',
            RestrictionTypeEnum.AllergyMedical,
            []
          )
        ),
        Validators.required,
      ],
      prescriptionInteractions: [
        this.getInitialValue(
          'prescriptionInteractions',
          RestrictionTypeEnum.AllergyMedical
        ),
      ],
      spiceLevelPreferred: [
        this.getInitialValue(
          'spiceLevelPreferred',
          RestrictionTypeEnum.PersonalPreference
        ),
      ],
      ingredientsDisliked: [
        JSON.stringify(
          this.getInitialValue(
            'ingredientsDisliked',
            RestrictionTypeEnum.PersonalPreference,
            []
          )
        ),
        Validators.required,
      ],
      texturesDisliked: [
        JSON.stringify(
          this.getInitialValue(
            'texturesDisliked',
            RestrictionTypeEnum.PersonalPreference,
            this.texturesDislikedOptions
          )
        ),
        Validators.required,
      ],
      preferredCookingMethods: [
        JSON.stringify(
          this.getInitialValue(
            'preferredCookingMethods',
            RestrictionTypeEnum.PersonalPreference,
            this.preferredCookingMethodsOptions
          )
        ),
        Validators.required,
      ],
      // Dynamic FormArray for conditional restriction allocation
      conditionalAllocations: this.fb.array([]),
    });

    // Populate initial conditional allocations if restrictions are pre-existing
    this.restrictions.forEach((res) => {
      if (
        res.appliesToEntirePlan ||
        (res.affectedPersonIds && res.affectedPersonIds.length > 0)
      ) {
        this.addConditionalAllocation(
          res.name,
          res.appliesToEntirePlan,
          res.affectedPersonIds
        );
        this.affirmativeRestrictionAnswers.set(res.name, {
          appliesToEntirePlan: res.appliesToEntirePlan,
          affectedPersonIds: res.affectedPersonIds,
        });
      }
    });
    this.updateAffirmativeRestrictionsList();

    // Setup search functionality for ingredients and micronutrients
    this.filteredIngredients$ = combineLatest([
      this.ingredientSearchControl.valueChanges.pipe(startWith('')),
      this.restrictionService.getCuratedIngredients(), // Fetch all ingredients
    ]).pipe(
      map(([searchTerm, allIngredients]) => {
        const term = searchTerm?.toLowerCase() || '';
        return allIngredients.filter((ingredient) =>
          ingredient.toLowerCase().includes(term)
        );
      })
    );

    this.filteredMicronutrients$ = combineLatest([
      this.micronutrientSearchControl.valueChanges.pipe(startWith('')),
      this.restrictionService.getMicronutrients(), // Fetch all micronutrients
    ]).pipe(
      map(([searchTerm, allMicronutrients]) => {
        const term = searchTerm?.toLowerCase() || '';
        return allMicronutrients.filter((nutrient) =>
          nutrient.toLowerCase().includes(term)
        );
      })
    );

    // Subscribe to form value changes to update affirmativeRestrictionsList dynamically
    this.restrictionForm.valueChanges.subscribe(() =>
      this.updateAffirmativeRestrictionsList()
    );
  }

  /**
   * Helper to get initial value for a form control, checking existing restrictions first.
   * Handles stringified arrays for multi-selects.
   */
  getInitialValue(
    controlName: string,
    restrictionTypeId: RestrictionTypeEnum,
    options?: string[]
  ): string {
    const existingRes = this.restrictions.find(
      (r) => r.name === controlName && r.restrictionTypeId === restrictionTypeId
    );
    if (existingRes) {
      if (options) {
        // If it's a multi-select, assume stored as JSON string
        return existingRes.name; // Return the name itself, which the control will use to populate
      }
      return existingRes.name; // For Yes/No or Text input
    }
    return options ? JSON.stringify([]) : 'false'; // Default for selects and Yes/No
  }

  // Helper getter for the FormArray
  get conditionalAllocations(): FormArray {
    return this.restrictionForm.get('conditionalAllocations') as FormArray;
  }

  /**
   * Dynamically adds a FormGroup for conditional allocation to the FormArray.
   */
  addConditionalAllocation(
    restrictionName: string,
    appliesToEntirePlan: boolean,
    affectedPersonIds: number[]
  ): void {
    const allocationFormGroup = this.fb.group({
      restrictionName: [restrictionName],
      appliesToEntirePlan: [appliesToEntirePlan, Validators.required],
      affectedPersonIds: [affectedPersonIds, this.affectedPersonsValidator()],
    });

    // Add a valueChanges listener to each new allocation group
    allocationFormGroup
      .get('appliesToEntirePlan')
      ?.valueChanges.subscribe((value) => {
        if (value) {
          // If applies to entire plan, clear affectedPersonIds
          allocationFormGroup.get('affectedPersonIds')?.setValue([]);
        }
        allocationFormGroup.get('affectedPersonIds')?.updateValueAndValidity();
      });

    this.conditionalAllocations.push(allocationFormGroup);
  }

  /**
   * Custom validator for affectedPersonIds array based on appliesToEntirePlan.
   * Ensures that if appliesToEntirePlan is false, then affectedPersonIds is not empty.
   */
  affectedPersonsValidator() {
    return (control: FormControl): { [key: string]: any } | null => {
      const parentFormGroup = control.parent;
      if (!parentFormGroup) {
        return null;
      }
      const appliesToEntirePlanControl = parentFormGroup.get(
        'appliesToEntirePlan'
      );
      const affectedPersonIds = control.value;

      if (
        appliesToEntirePlanControl &&
        appliesToEntirePlanControl.value === false &&
        (!affectedPersonIds || affectedPersonIds.length === 0)
      ) {
        return { noAffectedPersons: true };
      }
      return null;
    };
  }

  /**
   * Updates the `affirmativeRestrictionsList` based on the current form values.
   * This logic needs to carefully map form controls to restriction names and their type IDs.
   */
  updateAffirmativeRestrictionsList(): void {
    const formValue = this.restrictionForm.getRawValue();
    this.affirmativeRestrictionsList = []; // Reset the list

    const checkAndAddRestriction = (
      controlValue: any,
      restrictionName: string,
      typeId: RestrictionTypeEnum
    ) => {
      let isAffirmative = false;
      if (typeof controlValue === 'string') {
        try {
          const parsed = JSON.parse(controlValue);
          isAffirmative = Array.isArray(parsed) && parsed.length > 0;
        } catch (e) {
          isAffirmative =
            controlValue === 'true' && (controlValue as string).trim() !== '';
        }
      } else if (Array.isArray(controlValue)) {
        isAffirmative = controlValue.length > 0;
      } else {
        isAffirmative = !!controlValue; // Catches non-empty strings, numbers, true
      }

      if (isAffirmative) {
        const existsInList = this.affirmativeRestrictionsList.some(
          (r) => r.name === restrictionName
        );
        if (!existsInList) {
          this.affirmativeRestrictionsList.push({
            name: restrictionName,
            typeId: typeId,
          });
        }

        // Ensure a form group exists for this restriction in conditionalAllocations
        if (
          !this.conditionalAllocations.controls.some(
            (c) => c.get('restrictionName')?.value === restrictionName
          )
        ) {
          // Default to currentPersonId if only one person, or no pre-existing config
          const defaultAffectedPersons =
            this.allPersonsInPlan.length === 1
              ? [this.allPersonsInPlan[0].id]
              : [];
          this.addConditionalAllocation(
            restrictionName,
            false, // Default to not applying to entire plan
            defaultAffectedPersons
          );
        }
      } else {
        // If no longer affirmative, remove from list and from conditionalAllocations FormArray
        this.affirmativeRestrictionsList =
          this.affirmativeRestrictionsList.filter(
            (r) => r.name !== restrictionName
          );
        const indexInFormArray = this.conditionalAllocations.controls.findIndex(
          (c) => c.get('restrictionName')?.value === restrictionName
        );
        if (indexInFormArray !== -1) {
          this.conditionalAllocations.removeAt(indexInFormArray);
        }
      }
    };

    // Mapping form controls to restriction names and their types (adjust as per your exact question list)
    checkAndAddRestriction(
      formValue.societalReligiousEthical,
      'Dietary Foundations',
      RestrictionTypeEnum.SocietalReligiousEthical
    );
    checkAndAddRestriction(
      formValue.fastingSchedule,
      'Fasting Schedule',
      RestrictionTypeEnum.SocietalReligiousEthical
    );
    checkAndAddRestriction(
      formValue.mandatoryInclusions,
      'Mandatory Inclusions',
      RestrictionTypeEnum.SocietalReligiousEthical
    );

    checkAndAddRestriction(
      formValue.allergies,
      'Allergies',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.celiacOrGlutenSensitivity,
      'Celiac Disease or Gluten Sensitivity',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.lactoseIntolerance,
      'Lactose Intolerance',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.type1Diabetes,
      'Type 1 Diabetes',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.type2Diabetes,
      'Type 2 Diabetes',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.highBloodPressure,
      'High Blood Pressure',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.highCholesterol,
      'High Cholesterol',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.gastrointestinalConditions,
      'Gastrointestinal Conditions',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.kidneyDiseaseRestrictions,
      'Kidney Disease Restrictions',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.gout,
      'Gout',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.anemia,
      'Anemia',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.pregnancy,
      'Pregnancy',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.vitaminMineralDeficiencies,
      'Vitamin/Mineral Deficiencies',
      RestrictionTypeEnum.AllergyMedical
    );
    checkAndAddRestriction(
      formValue.prescriptionInteractions,
      'Prescription Interactions',
      RestrictionTypeEnum.AllergyMedical
    );

    checkAndAddRestriction(
      formValue.spiceLevelPreferred,
      'Spice Level Preferred',
      RestrictionTypeEnum.PersonalPreference
    );
    checkAndAddRestriction(
      formValue.ingredientsDisliked,
      'Ingredients Disliked',
      RestrictionTypeEnum.PersonalPreference
    );
    checkAndAddRestriction(
      formValue.texturesDisliked,
      'Textures Disliked',
      RestrictionTypeEnum.PersonalPreference
    );
    checkAndAddRestriction(
      formValue.preferredCookingMethods,
      'Preferred Cooking Methods',
      RestrictionTypeEnum.PersonalPreference
    );

    // Sort the list for consistent display order
    this.affirmativeRestrictionsList.sort((a, b) =>
      a.name.localeCompare(b.name)
    );
  }

  /**
   * Gathers data from the form and emits an array of RestrictionModel.
   * Called by the parent workflow component's "Next" or "Submit" button.
   */
  submitForm(): void {
    this.restrictionForm.markAllAsTouched();
    this.restrictionForm.updateValueAndValidity(); // Crucial for conditional validators

    if (this.restrictionForm.invalid) {
      console.error(
        'Restriction form is invalid:',
        this.restrictionForm.errors,
        this.restrictionForm.controls
      );
      return;
    }

    const submittedRestrictions: RestrictionModel[] = [];
    const formValue = this.restrictionForm.getRawValue();

    // Helper to process a single restriction type
    const processRestriction = (
      controlValue: any,
      restrictionName: string,
      restrictionTypeId: RestrictionTypeEnum
    ) => {
      let isAffirmative = false;
      let submittedValue: string | string[] = controlValue;

      if (typeof controlValue === 'string') {
        try {
          const parsed = JSON.parse(controlValue);
          submittedValue = Array.isArray(parsed) ? parsed : controlValue; // Keep parsed array, or original string
          isAffirmative = Array.isArray(parsed)
            ? parsed.length > 0
            : controlValue === 'true' && controlValue.trim() !== '';
        } catch (e) {
          isAffirmative = controlValue === 'true' && controlValue.trim() !== '';
        }
      } else if (Array.isArray(controlValue)) {
        isAffirmative = controlValue.length > 0;
      } else {
        isAffirmative = !!controlValue;
      }

      if (isAffirmative) {
        // Find the corresponding conditional allocation for this restriction
        const allocation = this.conditionalAllocations.controls.find(
          (c) => c.get('restrictionName')?.value === restrictionName
        );
        const appliesToEntirePlan =
          allocation?.get('appliesToEntirePlan')?.value || false;
        const affectedPersonIds = appliesToEntirePlan
          ? []
          : allocation?.get('affectedPersonIds')?.value || [];

        const newRestriction = new RestrictionModel({
          personId: appliesToEntirePlan ? null : this.currentPersonId, // Default to current person if not entire plan
          planId: appliesToEntirePlan ? 0 : null, // 0 if applies to plan (needs real plan ID from workflow)
          name:
            typeof submittedValue === 'string'
              ? submittedValue
              : restrictionName, // Use value for text, name for selections
          description: null, // Can add more specific descriptions later
          restrictionTypeId: restrictionTypeId,
          appliesToEntirePlan: appliesToEntirePlan,
          affectedPersonIds: affectedPersonIds,
        });

        // For multi-selects, the 'value' of the form control is the array. The RestrictionModel 'name' is the category.
        // For individual options, we might create separate RestrictionModels or include them in the 'description'/'name'
        if (Array.isArray(submittedValue)) {
          submittedValue.forEach((item) => {
            submittedRestrictions.push(
              new RestrictionModel({
                personId: appliesToEntirePlan ? null : this.currentPersonId,
                planId: appliesToEntirePlan ? 0 : null,
                name: item, // The specific selected item name
                description: restrictionName, // Use the category as description
                restrictionTypeId: restrictionTypeId,
                appliesToEntirePlan: appliesToEntirePlan,
                affectedPersonIds: affectedPersonIds,
              })
            );
          });
        } else if (
          typeof submittedValue === 'string' &&
          (submittedValue === 'true' || submittedValue === 'false')
        ) {
          // Yes/No questions, only add if 'true'
          if (submittedValue === 'true') {
            submittedRestrictions.push(
              new RestrictionModel({
                personId: appliesToEntirePlan ? null : this.currentPersonId,
                planId: appliesToEntirePlan ? 0 : null,
                name: restrictionName, // The question's name
                description: null,
                restrictionTypeId: restrictionTypeId,
                appliesToEntirePlan: appliesToEntirePlan,
                affectedPersonIds: affectedPersonIds,
              })
            );
          }
        } else {
          // For text input or other direct values
          submittedRestrictions.push(
            new RestrictionModel({
              personId: appliesToEntirePlan ? null : this.currentPersonId,
              planId: appliesToEntirePlan ? 0 : null,
              name: submittedValue, // The actual text input value
              description: restrictionName, // The question's name
              restrictionTypeId: restrictionTypeId,
              appliesToEntirePlan: appliesToEntirePlan,
              affectedPersonIds: affectedPersonIds,
            })
          );
        }
      }
    };

    // Process each restriction type based on the form controls
    processRestriction(
      formValue.societalReligiousEthical,
      'Dietary Foundations',
      RestrictionTypeEnum.SocietalReligiousEthical
    );
    processRestriction(
      formValue.fastingSchedule,
      'Fasting Schedule',
      RestrictionTypeEnum.SocietalReligiousEthical
    );
    processRestriction(
      formValue.mandatoryInclusions,
      'Mandatory Inclusions',
      RestrictionTypeEnum.SocietalReligiousEthical
    );

    processRestriction(
      formValue.allergies,
      'Allergies',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.celiacOrGlutenSensitivity,
      'Celiac Disease or Gluten Sensitivity',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.lactoseIntolerance,
      'Lactose Intolerance',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.type1Diabetes,
      'Type 1 Diabetes',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.type2Diabetes,
      'Type 2 Diabetes',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.highBloodPressure,
      'High Blood Pressure',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.highCholesterol,
      'High Cholesterol',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.gastrointestinalConditions,
      'Gastrointestinal Conditions',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.kidneyDiseaseRestrictions,
      'Kidney Disease Restrictions',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.gout,
      'Gout',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.anemia,
      'Anemia',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.pregnancy,
      'Pregnancy',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.vitaminMineralDeficiencies,
      'Vitamin/Mineral Deficiencies',
      RestrictionTypeEnum.AllergyMedical
    );
    processRestriction(
      formValue.prescriptionInteractions,
      'Prescription Interactions',
      RestrictionTypeEnum.AllergyMedical
    );

    processRestriction(
      formValue.spiceLevelPreferred,
      'Spice Level Preferred',
      RestrictionTypeEnum.PersonalPreference
    );
    processRestriction(
      formValue.ingredientsDisliked,
      'Ingredients Disliked',
      RestrictionTypeEnum.PersonalPreference
    );
    processRestriction(
      formValue.texturesDisliked,
      'Textures Disliked',
      RestrictionTypeEnum.PersonalPreference
    );
    processRestriction(
      formValue.preferredCookingMethods,
      'Preferred Cooking Methods',
      RestrictionTypeEnum.PersonalPreference
    );

    this.formSubmitted.emit(submittedRestrictions);
  }

  // Method to handle checkbox changes for multi-selects within the form
  // Assumes the form control stores a JSON string of selected options
  onMultiSelectChange(
    controlName: string,
    option: string,
    isChecked: boolean
  ): void {
    const control = this.restrictionForm.get(controlName);
    if (control) {
      let currentSelections: string[] = [];
      try {
        currentSelections = JSON.parse(control.value);
      } catch (e) {
        // If parsing fails, it means it's not a valid JSON array string yet, so start fresh
        currentSelections = [];
      }

      if (isChecked && !currentSelections.includes(option)) {
        currentSelections.push(option);
      } else if (!isChecked && currentSelections.includes(option)) {
        currentSelections = currentSelections.filter((item) => item !== option);
      }
      control.setValue(JSON.stringify(currentSelections));
      control.markAsDirty(); // Mark as dirty when value changes
    }
  }

  // Helper to check if an option is selected in a multi-select checkbox group
  isMultiSelectOptionSelected(controlName: string, option: string): boolean {
    const control = this.restrictionForm.get(controlName);
    if (control && control.value) {
      try {
        const currentSelections: string[] = JSON.parse(control.value);
        return currentSelections.includes(option);
      } catch (e) {
        return false;
      }
    }
    return false;
  }

  onSkip(): void {
    this.skipStep.emit();
  }
}
