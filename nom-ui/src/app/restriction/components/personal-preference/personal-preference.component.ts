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
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { Observable, startWith, map } from 'rxjs';
import { RestrictionService } from '../../services/restriction.service';

@Component({
  selector: 'app-personal-preference',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatChipsModule,
    MatIconModule,
    MatAutocompleteModule,
  ],
  templateUrl: './personal-preference.component.html',
  styleUrls: ['./personal-preference.component.scss'],
})
export class PersonalPreferenceComponent implements OnInit {
  @Input() personalPreferenceForm!: FormGroup; // Input FormGroup for this section

  // FormControls for autocomplete inputs
  public ingredientSearchControl = new FormControl<string>('');
  public filteredCuratedIngredients!: Observable<string[]>;

  // Options for select dropdowns
  public spiceLevelOptions = ['Mild', 'Medium', 'Spicy', 'Very Spicy'];
  public texturesDislikedOptions = [
    'Creamy',
    'Crunchy',
    'Smooth',
    'Chewy',
    'Crispy',
    'Soggy',
    'Slimy',
    'Gritty',
  ];
  public preferredCookingMethodsOptions = [
    'Baked',
    'Grilled',
    'Fried',
    'Steamed',
    'Roasted',
    'Raw',
    'Sautéed',
    'Boiled',
  ];

  private allCuratedIngredients: string[] = [
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

  constructor(
    private fb: NonNullableFormBuilder,
    private restrictionService: RestrictionService
  ) {}

  ngOnInit(): void {
    if (!this.personalPreferenceForm) {
      console.warn(
        'PersonalPreferenceRestrictionComponent: personalPreferenceForm input is missing. Initializing a default one.'
      );
      this.personalPreferenceForm = this.fb.group({
        personalPreferenceSpiceLevel: new FormControl(''),
        dislikedIngredients: this.fb.array([] as FormControl<string>[]),
        dislikedTextures: this.fb.array([] as FormControl<string>[]),
        preferredCookingMethods: this.fb.array([] as FormControl<string>[]),
      });
    }

    this.filteredCuratedIngredients =
      this.ingredientSearchControl.valueChanges.pipe(
        startWith(''),
        map((value) =>
          value
            ? this._filter(value, this.allCuratedIngredients)
            : this.allCuratedIngredients
        )
      );

    // Fetch real data (mocked for now)
    this.restrictionService.getCuratedIngredients().subscribe((data) => {
      if (data && data.length > 0) this.allCuratedIngredients = data;
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
      const formArray = this.personalPreferenceForm.get(
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
    const formArray = this.personalPreferenceForm.get(
      formArrayName
    ) as FormArray;
    const index = formArray.value.indexOf(chip);
    if (index >= 0) {
      formArray.removeAt(index);
    }
  }

  public onMultiSelectChange(formControlName: string, event: any): void {
    const selectedValues = event.value;
    const formArray = this.personalPreferenceForm.get(
      formControlName
    ) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: any) => {
      formArray.push(this.fb.control(value));
    });
  }

  // Getters for FormArrays
  get dislikedIngredientsArray(): FormArray {
    return this.personalPreferenceForm.get('dislikedIngredients') as FormArray;
  }
  get dislikedTexturesArray(): FormArray {
    return this.personalPreferenceForm.get('dislikedTextures') as FormArray;
  }
  get preferredCookingMethodsArray(): FormArray {
    return this.personalPreferenceForm.get(
      'preferredCookingMethods'
    ) as FormArray;
  }
}
