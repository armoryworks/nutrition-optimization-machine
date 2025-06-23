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
  selector: 'app-societal-restriction',
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
  templateUrl: './societal-restriction.component.html',
  styleUrls: ['./societal-restriction.component.scss'],
})
export class SocietalRestrictionComponent implements OnInit {
  @Input() societalRestrictionForm!: FormGroup; // Input FormGroup for this section

  // FormControls for autocomplete inputs
  public ingredientSearchControl = new FormControl<string>('');
  public filteredCuratedIngredients!: Observable<string[]>;

  // Options for select dropdowns
  public societalReligiousEthicalOptions = [
    { id: 100, name: 'Vegan' },
    { id: 101, name: 'Vegetarian' },
    { id: 102, name: 'Kosher' },
    { id: 103, name: 'Halal' },
    { id: 104, name: 'Pescatarian' },
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
    if (!this.societalRestrictionForm) {
      // This should ideally be handled by the parent, but for safety
      console.warn(
        'SocietalRestrictionComponent: societalRestrictionForm input is missing. Initializing a default one.'
      );
      this.societalRestrictionForm = this.fb.group({
        societalReligiousEthicalTypeIds: this.fb.array(
          [] as FormControl<number>[]
        ),
        mandatoryInclusions: this.fb.array([] as FormControl<string>[]),
        fastingSchedules: new FormControl(''),
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
      const formArray = this.societalRestrictionForm.get(
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
    const formArray = this.societalRestrictionForm.get(
      formArrayName
    ) as FormArray;
    const index = formArray.value.indexOf(chip);
    if (index >= 0) {
      formArray.removeAt(index);
    }
  }

  public onMultiSelectChange(formControlName: string, event: any): void {
    const selectedValues = event.value;
    const formArray = this.societalRestrictionForm.get(
      formControlName
    ) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: any) => {
      formArray.push(this.fb.control(value));
    });
  }

  // Getter for FormArray: societalReligiousEthicalTypeIds
  get societalReligiousEthicalTypeIdsArray(): FormArray {
    return this.societalRestrictionForm.get(
      'societalReligiousEthicalTypeIds'
    ) as FormArray;
  }

  // Getter for FormArray: mandatoryInclusions
  get mandatoryInclusionsArray(): FormArray {
    return this.societalRestrictionForm.get('mandatoryInclusions') as FormArray;
  }
}
