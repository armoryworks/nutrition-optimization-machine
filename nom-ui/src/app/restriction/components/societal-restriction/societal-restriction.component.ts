import { Component, OnInit, input, inject } from '@angular/core';
import {
  FormGroup,
  FormControl,
  FormArray,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { AmwSelectComponent, AmwTextareaComponent, AmwChipInputComponent } from 'angular-material-wrap';
import { RestrictionService } from '../../services/restriction.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';

@Component({
  selector: 'nom-societal-restriction',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwSelectComponent,
    AmwTextareaComponent,
    AmwChipInputComponent,
  ],
  templateUrl: './societal-restriction.component.html',
  styleUrls: ['./societal-restriction.component.scss'],
})
export class SocietalRestrictionComponent implements OnInit {
  private fb = inject(NonNullableFormBuilder);
  private restrictionService = inject(RestrictionService);
  private referenceDataService = inject(ReferenceDataService);

  societalRestrictionForm = input.required<FormGroup>(); // Input FormGroup for this section

  // FormControl for chip input - syncs with FormArray
  public mandatoryInclusionsControl = new FormControl<{ value: string; label: string }[]>([]);

  // Options for select dropdowns - loaded from backend
  public societalReligiousEthicalOptions: ReferenceItemModel[] = [];

  private _allCuratedIngredients: string[] = [
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

  // Getter that converts strings to ChipInputOption format
  get allCuratedIngredients(): { value: string; label: string }[] {
    return this._allCuratedIngredients.map(item => ({ value: item, label: item }));
  }


  ngOnInit(): void {
    // Fetch real data (mocked for now)
    this.restrictionService.getCuratedIngredients().subscribe((data) => {
      if (data && data.length > 0) this._allCuratedIngredients = data;
    });

    this.loadSocietalRestrictionOptions();

    // Sync chip input control with FormArray
    this.mandatoryInclusionsControl.valueChanges.subscribe((values) => {
      const formArray = this.mandatoryInclusionsArray;
      formArray.clear();
      (values || []).forEach((chip: { value: string; label: string }) => {
        formArray.push(this.fb.control(chip.value));
      });
    });

    // Initialize control with existing FormArray values (convert strings to ChipInputOption)
    const initialValues = this.mandatoryInclusionsArray.value.map((v: string) => ({ value: v, label: v }));
    this.mandatoryInclusionsControl.setValue(initialValues);
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

  // Helper method for AMW select options
  getPracticeTypeOptions(): { value: number; label: string }[] {
    return this.societalReligiousEthicalOptions.map(option => ({
      value: option.referenceId ?? 0,
      label: option.referenceName ?? ''
    }));
  }
}
