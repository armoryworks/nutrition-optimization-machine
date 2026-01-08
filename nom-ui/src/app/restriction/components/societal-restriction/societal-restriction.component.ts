import { Component, OnInit, input, inject } from '@angular/core';
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
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';

@Component({
  selector: 'nom-societal-restriction',
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
  private fb = inject(NonNullableFormBuilder);
  private restrictionService = inject(RestrictionService);
  private referenceDataService = inject(ReferenceDataService);

  societalRestrictionForm = input.required<FormGroup>(); // Input FormGroup for this section

  // FormControls for autocomplete inputs
  public ingredientSearchControl = new FormControl<string>('');
  public filteredCuratedIngredients!: Observable<string[]>;

  // Options for select dropdowns - loaded from backend
  public societalReligiousEthicalOptions: ReferenceItemModel[] = [];

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


  ngOnInit(): void {
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

    this.loadSocietalRestrictionOptions();
  }

  private loadSocietalRestrictionOptions(): void {
    // Load societal/religious/ethical restriction options from backend
    // Using a specific reference group for societal restrictions
    this.referenceDataService.getReferencesByGroup(2000).subscribe({
      next: (options) => {
        this.societalReligiousEthicalOptions = options;
      },
      error: (error) => {
        console.error('Error loading societal restriction options:', error);
        // Fallback to empty array if API fails
        this.societalReligiousEthicalOptions = [];
      }
    });
  }

  private _filter(value: string, options: string[]): string[] {
    const filterValue = value ? value.toLowerCase() : '';
    return options.filter((option) =>
      option.toLowerCase().includes(filterValue)
    );
  }

  public addChip(event: { input?: HTMLInputElement | null; value?: string }, formArrayName: string): void {
    const input = event.input;
    const value = (event.value || '').trim();
    if (value) {
      const formArray = this.societalRestrictionForm().get(
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
    const formArray = this.societalRestrictionForm().get(
      formArrayName
    ) as FormArray;
    const index = formArray.value.indexOf(chip);
    if (index >= 0) {
      formArray.removeAt(index);
    }
  }

  public onMultiSelectChange(formControlName: string, event: { value: string[] }): void {
    const selectedValues = event.value;
    const formArray = this.societalRestrictionForm().get(
      formControlName
    ) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: string) => {
      formArray.push(this.fb.control(value));
    });
  }

  // Getter for FormArray: societalReligiousEthicalTypeIds
  get societalReligiousEthicalTypeIdsArray(): FormArray {
    return this.societalRestrictionForm().get(
      'societalReligiousEthicalTypeIds'
    ) as FormArray;
  }

  // Getter for FormArray: mandatoryInclusions
  get mandatoryInclusionsArray(): FormArray {
    return this.societalRestrictionForm().get('mandatoryInclusions') as FormArray;
  }
}
