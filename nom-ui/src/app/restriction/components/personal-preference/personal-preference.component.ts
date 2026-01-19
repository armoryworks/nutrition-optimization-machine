import { Component, OnInit, input, inject, OnDestroy } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, FormArray, FormControl } from '@angular/forms';
import { AmwSelectComponent, AmwChipInputComponent } from 'angular-material-wrap';
import { Subject, takeUntil } from 'rxjs';
import { RestrictionService } from '../../services/restriction.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
  selector: 'app-personal-preference',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwSelectComponent,
    AmwChipInputComponent,
  ],
  templateUrl: './personal-preference.component.html',
  styleUrls: ['./personal-preference.component.scss']
})
export class PersonalPreferenceRestrictionComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private restrictionService = inject(RestrictionService);
  private referenceDataService = inject(ReferenceDataService);

  personalPreferenceForm = input.required<FormGroup>();

  // FormControl for chip input - syncs with FormArray
  public dislikedIngredientsControl = new FormControl<{ value: string; label: string }[]>([]);

  // Curated ingredients list
  private _allCuratedIngredients: string[] = [];

  // Getter that converts strings to ChipInputOption format
  get allCuratedIngredients(): { value: string; label: string }[] {
    return this._allCuratedIngredients.map(item => ({ value: item, label: item }));
  }

  // Reference data loaded dynamically
  public spiceLevelOptions: any[] = [];
  public texturesDislikedOptions: any[] = [];
  public preferredCookingMethodsOptions: any[] = [];

  // Make constants available in template
  readonly REFERENCE_IDS = REFERENCE_IDS;

  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.loadReferenceData();
    this.loadCuratedIngredients();
    this.setupChipSync();
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

  private loadCuratedIngredients(): void {
    this.restrictionService.getCuratedIngredients()
      .pipe(takeUntil(this.destroy$))
      .subscribe((data) => {
        if (data && data.length > 0) {
          this._allCuratedIngredients = data;
        }
      });
  }

  private setupChipSync(): void {
    // Sync chip input control with FormArray
    this.dislikedIngredientsControl.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((values) => {
        const formArray = this.dislikedIngredientsArray;
        formArray.clear();
        (values || []).forEach((chip: { value: string; label: string }) => {
          formArray.push(this.fb.control(chip.value));
        });
      });

    // Initialize control with existing FormArray values (convert strings to ChipInputOption)
    const initialValues = this.dislikedIngredientsArray.value.map((v: string) => ({ value: v, label: v }));
    this.dislikedIngredientsControl.setValue(initialValues);
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

  // Helper methods for AMW select options
  getSpiceLevelSelectOptions(): { value: number; label: string }[] {
    return this.spiceLevelOptions.map(option => ({
      value: option.referenceId ?? 0,
      label: option.referenceName ?? ''
    }));
  }

  getTextureSelectOptions(): { value: number; label: string }[] {
    return this.texturesDislikedOptions.map(option => ({
      value: option.referenceId ?? 0,
      label: option.referenceName ?? ''
    }));
  }

  getCookingMethodSelectOptions(): { value: number; label: string }[] {
    return this.preferredCookingMethodsOptions.map(option => ({
      value: option.referenceId ?? 0,
      label: option.referenceName ?? ''
    }));
  }
}
