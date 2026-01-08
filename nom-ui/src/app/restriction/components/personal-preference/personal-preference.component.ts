import { Component, OnInit, input, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, FormArray, FormControl, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, takeUntil, startWith, map, Observable } from 'rxjs';
import { RestrictionService } from '../../services/restriction.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
  selector: 'app-personal-preference',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
    MatCheckboxModule,
    MatChipsModule,
    MatIconModule,
    MatAutocompleteModule,
    MatTooltipModule
  ],
  templateUrl: './personal-preference.component.html',
  styleUrls: ['./personal-preference.component.scss']
})
export class PersonalPreferenceRestrictionComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private restrictionService = inject(RestrictionService);
  private referenceDataService = inject(ReferenceDataService);

  personalPreferenceForm = input.required<FormGroup>();

  // FormControls for autocomplete inputs
  public ingredientSearchControl = new FormControl<string>('');
  public filteredCuratedIngredients!: Observable<string[]>;

  // Reference data loaded dynamically
  public spiceLevelOptions: any[] = [];
  public texturesDislikedOptions: any[] = [];
  public preferredCookingMethodsOptions: any[] = [];

  // Make constants available in template
  readonly REFERENCE_IDS = REFERENCE_IDS;

  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.loadReferenceData();
    this.setupIngredientSearch();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadReferenceData(): void {
    // Load spice level options
    this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.SPICE_LEVEL_TYPE)
      .pipe(takeUntil(this.destroy$))
      .subscribe(options => {
        this.spiceLevelOptions = options;
      });

    // Load texture options
    this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.TEXTURE_TYPE)
      .pipe(takeUntil(this.destroy$))
      .subscribe(options => {
        this.texturesDislikedOptions = options;
      });

    // Load cooking method options
    this.referenceDataService.getReferencesByGroup(REFERENCE_IDS.COOKING_METHOD_TYPE)
      .pipe(takeUntil(this.destroy$))
      .subscribe(options => {
        this.preferredCookingMethodsOptions = options;
      });
  }

  private setupIngredientSearch(): void {
    // Fetch real data from service
    this.restrictionService.getCuratedIngredients().subscribe((data) => {
      if (data && data.length > 0) {
        this.filteredCuratedIngredients = this.ingredientSearchControl.valueChanges.pipe(
          startWith(''),
          map((value) =>
            value
              ? this._filter(value, data)
              : data
          )
        );
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
      const formArray = this.personalPreferenceForm().get(
        formArrayName
      ) as FormArray;
      formArray.push(this.fb.control(value));
      if (input) {
        input.value = '';
      }
    }
  }

  public removeChip(index: number, formArrayName: string): void {
    const formArray = this.personalPreferenceForm().get(
      formArrayName
    ) as FormArray;
    formArray.removeAt(index);
  }

  public getFormArray(formArrayName: string): FormArray {
    return this.personalPreferenceForm().get(formArrayName) as FormArray;
  }

  // Getters for FormArrays that the template expects
  get dislikedIngredientsArray(): FormArray {
    return this.personalPreferenceForm().get('dislikedIngredients') as FormArray;
  }

  get dislikedTexturesArray(): FormArray {
    return this.personalPreferenceForm().get('dislikedTextures') as FormArray;
  }

  get preferredCookingMethodsArray(): FormArray {
    return this.personalPreferenceForm().get('preferredCookingMethods') as FormArray;
  }

  public onMultiSelectChange(formControlName: string, event: { value: string[] }): void {
    const selectedValues = event.value;
    const formArray = this.personalPreferenceForm().get(formControlName) as FormArray;
    formArray.clear();
    selectedValues.forEach((value: string) => {
      formArray.push(this.fb.control(value));
    });
  }

  public onSubmit(): void {
    if (this.personalPreferenceForm().valid) {
      console.log('Form submitted:', this.personalPreferenceForm().value);
      // TODO: Implement form submission logic
    }
  }

  public onReset(): void {
    this.personalPreferenceForm().reset();
  }

  public getSpiceLevelOptions(): any[] {
    return this.spiceLevelOptions;
  }

  public getTextureOptions(): any[] {
    return this.texturesDislikedOptions;
  }

  public getCookingMethodOptions(): any[] {
    return this.preferredCookingMethodsOptions;
  }

  public getSpiceLevelName(id: number): string {
    const option = this.spiceLevelOptions.find(o => o.referenceId === id);
    return option?.referenceName || 'Unknown';
  }

  public getTextureName(id: number): string {
    const option = this.texturesDislikedOptions.find(o => o.referenceId === id);
    return option?.referenceName || 'Unknown';
  }

  public getCookingMethodName(id: number): string {
    const option = this.preferredCookingMethodsOptions.find(o => o.referenceId === id);
    return option?.referenceName || 'Unknown';
  }
}
