import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
  OnDestroy,
} from '@angular/core';
import {
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { PersonAttributeModel } from '../../models/person-attribute.model';
import { MatSelectModule } from '@angular/material/select';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { ReferenceService } from '../../../common/services/reference.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { Subject, takeUntil } from 'rxjs';

// Extended interface for attribute types with additional properties
interface AttributeTypeModel extends ReferenceItemModel {
  label?: string;
  unit?: string;
  icon?: string;
  class?: string;
  options?: Array<{ value: string; label: string }>;
}

@Component({
  selector: 'nom-person-health-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSelectModule,
    BaseFormComponent,
    BasePageComponent,
  ],
  templateUrl: './person-health-edit.component.html',
  styleUrls: ['./person-health-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonHealthEditComponent implements OnInit, OnDestroy {
  public readonly HEIGHT_ATTRIBUTE_ID = 2000;
  public readonly HEIGHT_IN_FEET_NAME = 'HeightInFeet';
  public readonly HEIGHT_IN_INCHES_NAME = 'HeightInInches';

  @Input() attributes: PersonAttributeModel[] = [];
  @Input() currentPersonId: number = 0;
  @Output() formSubmitted = new EventEmitter<PersonAttributeModel[]>();
  @Output() skipStep = new EventEmitter<void>();

  healthAttributesForm!: FormGroup;
  attributeTypes: AttributeTypeModel[] = [];
  isSubmitting = false;
  isLoading = false;
  error: string | null = null;

  pageConfig: BasePageConfig = {
    title: 'Health Information',
    subtitle: 'Provide some health details to help us personalize your plan (Optional)',
    showBackButton: true,
    maxWidth: '600px'
  };

  formConfig: BaseFormConfig = {
    title: '',
    subtitle: '',
    submitText: 'Save Health Info',
    showCancelButton: true,
    cancelText: 'Skip',
    maxWidth: '100%'
  };

  private destroy$ = new Subject<void>();

  constructor(
    private fb: NonNullableFormBuilder,
    private referenceService: ReferenceService
  ) {
    // Initialize the form with empty controls to prevent formControlName errors
    this.healthAttributesForm = this.fb.group({});
  }

  ngOnInit(): void {
    this.loadAttributeTypes();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadAttributeTypes(): void {
    this.isLoading = true;
    this.error = null;

    this.referenceService.getAttributeTypes().pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: (attributeTypes) => {
        // Transform basic reference items to extended attribute types
        this.attributeTypes = attributeTypes.map(attr => ({
          ...attr,
          label: attr.name,
          unit: this.getUnitForAttribute(attr.name),
          icon: this.getIconForAttribute(attr.name),
          class: this.getClassForAttribute(attr.name),
          options: this.getOptionsForAttribute(attr.name)
        }));
        this.initializeForm();
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading attribute types:', error);
        this.error = 'Failed to load health attributes. Please try again.';
        this.isLoading = false;
      }
    });
  }

  private initializeForm(): void {
    const formControls: { [key: string]: FormControl } = {};

    // Initialize form controls for each attribute type
    this.attributeTypes.forEach(attrType => {
      const controlName = this.getFormControlName(attrType.name);
      const existingValue = this.attributes.find(attr => attr.attributeTypeRefId === attrType.id)?.value || '';
      formControls[controlName] = new FormControl(existingValue);
    });

    // Special handling for height attributes
    const heightAttribute = this.attributes.find(attr => attr.attributeTypeRefId === this.HEIGHT_ATTRIBUTE_ID);
    if (heightAttribute) {
      const heightInInches = parseInt(heightAttribute.value) || 0;
      const feet = Math.floor(heightInInches / 12);
      const inches = heightInInches % 12;

      formControls[this.HEIGHT_IN_FEET_NAME] = new FormControl(feet);
      formControls[this.HEIGHT_IN_INCHES_NAME] = new FormControl(inches);
    } else {
      formControls[this.HEIGHT_IN_FEET_NAME] = new FormControl('');
      formControls[this.HEIGHT_IN_INCHES_NAME] = new FormControl('');
    }

    this.healthAttributesForm = this.fb.group(formControls);
  }

  getFormControlName(name: string): string {
    return name.replace(/\s+/g, '').replace(/[^a-zA-Z0-9]/g, '');
  }

  onSubmit(): void {
    this.isSubmitting = true;
    this.error = null;

    if (this.healthAttributesForm.valid) {
      const formValue = this.healthAttributesForm.value;
      const attributes: PersonAttributeModel[] = [];

      // Process each attribute type
      this.attributeTypes.forEach(attrType => {
        const controlName = this.getFormControlName(attrType.name);
        const value = formValue[controlName];

        if (value !== null && value !== undefined && value !== '') {
          let processedValue = value;

          // Special handling for height
          if (attrType.id === this.HEIGHT_ATTRIBUTE_ID) {
            const feet = formValue[this.HEIGHT_IN_FEET_NAME] || 0;
            const inches = formValue[this.HEIGHT_IN_INCHES_NAME] || 0;
            processedValue = (feet * 12 + inches).toString();
          }

          attributes.push({
            id: 0, // Will be set by the backend for new attributes
            personId: this.currentPersonId,
            attributeTypeRefId: attrType.id,
            value: processedValue.toString()
          });
        }
      });

      this.formSubmitted.emit(attributes);
    } else {
      this.error = 'Please correct the form errors before submitting.';
    }
    this.isSubmitting = false;
  }

  onCancel(): void {
    this.skipStep.emit();
  }

  submitForm(): void {
    this.onSubmit();
  }

  onSkip(): void {
    this.skipStep.emit();
  }

  onBack(): void {
    this.skipStep.emit();
  }

  onRefresh(): void {
    this.loadAttributeTypes();
  }

  onRetry(): void {
    this.error = null;
    this.loadAttributeTypes();
  }

  private getUnitForAttribute(attributeName: string): string {
    const unitMap: { [key: string]: string } = {
      'Height': 'inches',
      'Weight': 'lbs',
      'Age': 'years',
      'Activity Level': '',
      'Dietary Restrictions': '',
      'Health Goals': '',
      'Medical Conditions': ''
    };
    return unitMap[attributeName] || '';
  }

  private getIconForAttribute(attributeName: string): string {
    const iconMap: { [key: string]: string } = {
      'Height': 'fa-ruler-vertical',
      'Weight': 'fa-weight',
      'Age': 'fa-birthday-cake',
      'Activity Level': 'fa-running',
      'Dietary Restrictions': 'fa-utensils',
      'Health Goals': 'fa-bullseye',
      'Medical Conditions': 'fa-heartbeat'
    };
    return iconMap[attributeName] || 'fa-info-circle';
  }

  private getClassForAttribute(attributeName: string): string {
    const classMap: { [key: string]: string } = {
      'Height': 'height-field',
      'Weight': 'weight-field',
      'Age': 'age-field'
    };
    return classMap[attributeName] || '';
  }

  private getOptionsForAttribute(attributeName: string): Array<{ value: string; label: string }> | undefined {
    const optionsMap: { [key: string]: Array<{ value: string; label: string }> } = {
      'Activity Level': [
        { value: 'sedentary', label: 'Sedentary (Little or no exercise)' },
        { value: 'lightly_active', label: 'Lightly Active (Light exercise/sports 1-3 days/week)' },
        { value: 'moderately_active', label: 'Moderately Active (Moderate exercise/sports 3-5 days/week)' },
        { value: 'very_active', label: 'Very Active (Hard exercise/sports 6-7 days a week)' },
        { value: 'extremely_active', label: 'Extremely Active (Very hard exercise/sports & physical job)' }
      ],
      'Dietary Restrictions': [
        { value: 'none', label: 'No Restrictions' },
        { value: 'vegetarian', label: 'Vegetarian' },
        { value: 'vegan', label: 'Vegan' },
        { value: 'gluten_free', label: 'Gluten-Free' },
        { value: 'dairy_free', label: 'Dairy-Free' },
        { value: 'keto', label: 'Keto' },
        { value: 'paleo', label: 'Paleo' }
      ],
      'Health Goals': [
        { value: 'weight_loss', label: 'Weight Loss' },
        { value: 'weight_gain', label: 'Weight Gain' },
        { value: 'maintenance', label: 'Maintenance' },
        { value: 'muscle_gain', label: 'Muscle Gain' },
        { value: 'general_health', label: 'General Health' }
      ]
    };
    return optionsMap[attributeName];
  }
}
