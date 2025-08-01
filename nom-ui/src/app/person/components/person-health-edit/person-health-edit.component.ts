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
  NonNullableFormBuilder,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PersonAttributeModel } from '../../models/person-attribute.model';
import { MatSelectModule } from '@angular/material/select'; // For dropdowns if needed
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { ReferenceService } from '../../../common/services/reference.service';

// Extended interface for attribute types with additional properties
interface AttributeTypeModel extends ReferenceItemModel {
  label?: string;
  unit?: string;
  icon?: string;
  class?: string;
  options?: Array<{ value: string; label: string }>;
}

@Component({
  selector: 'app-person-health-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
  ],
  templateUrl: './person-health-edit.component.html',
  styleUrls: ['./person-health-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonHealthEditComponent implements OnInit {
  public readonly HEIGHT_ATTRIBUTE_ID = 2000;
  public readonly HEIGHT_IN_FEET_NAME = 'HeightInFeet';
  public readonly HEIGHT_IN_INCHES_NAME = 'HeightInInches';

  @Input() attributes: PersonAttributeModel[] = []; // Input to pre-populate
  @Input() currentPersonId: number = 0; // The ID of the person these attributes belong to
  @Output() formSubmitted = new EventEmitter<PersonAttributeModel[]>();
  @Output() skipStep = new EventEmitter<void>();

  healthAttributesForm!: FormGroup;

  attributeTypes: AttributeTypeModel[] = [];

  constructor(
    private fb: NonNullableFormBuilder,
    private referenceService: ReferenceService
  ) { }

  ngOnInit(): void {
    this.loadAttributeTypes();
  }

  loadAttributeTypes(): void {
    this.referenceService.getAttributeTypes().subscribe({
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
      },
      error: (error) => {
        console.error('Error loading attribute types:', error);
      }
    });
  }

  private initializeForm(): void {
    const formControls: { [key: string]: FormControl } = {};

    this.attributeTypes.forEach((attrType) => {
      const controlName = this.getFormControlName(attrType.name);
      const existingAttribute = this.attributes.find(
        (a) => a.attributeTypeRefId === attrType.id
      );

      if (attrType.id === this.HEIGHT_ATTRIBUTE_ID) {
        if (attrType.name === this.HEIGHT_IN_FEET_NAME) {
          let heightInFeet = 0;
          if (existingAttribute?.value) {
            const totalHeightInInches = parseInt(existingAttribute.value);
            heightInFeet = Math.floor(totalHeightInInches / 12);
          }
          formControls[controlName] = this.fb.control(heightInFeet);
        } else if (attrType.name === this.HEIGHT_IN_INCHES_NAME) {
          let heightInInches = 0;
          if (existingAttribute?.value) {
            const totalHeightInInches = parseInt(existingAttribute.value);
            heightInInches = totalHeightInInches % 12;
          }
          formControls[controlName] = this.fb.control(heightInInches);
        } else {
          formControls[controlName] = this.fb.control('');
        }
      } else {
        formControls[controlName] = this.fb.control(
          existingAttribute?.value || ''
        );
      }
    });

    this.healthAttributesForm = this.fb.group(formControls);
  }

  /**
   * Helper method to generate a valid form control name from a given string.
   * Removes spaces and converts to lowercase.
   * This is used to avoid regular expression literals in the template.
   */
  getFormControlName(name: string): string {
    return name.toLowerCase().replace(/\s/g, '');
  }

  /**
   * Gathers data from the form and emits an array of PersonAttributeModel.
   * Called by the parent workflow component's "Next" button.
   */
  submitForm(): void {
    const submittedAttributes: PersonAttributeModel[] = [];
    let heightProcessed = false;

    this.attributeTypes.forEach((attrType) => {
      const controlName = this.getFormControlName(attrType.name);

      if (attrType.id === this.HEIGHT_ATTRIBUTE_ID) {
        if (!heightProcessed) {
          const heightInFeet =
            this.healthAttributesForm.get(
              this.getFormControlName(this.HEIGHT_IN_FEET_NAME)
            )?.value || 0;
          const heightInInches =
            this.healthAttributesForm.get(
              this.getFormControlName(this.HEIGHT_IN_INCHES_NAME)
            )?.value || 0;
          const totalHeightInInches = heightInFeet * 12 + heightInInches;

          if (totalHeightInInches > 0) {
            submittedAttributes.push(
              new PersonAttributeModel({
                personId: this.currentPersonId,
                attributeTypeRefId: attrType.id,
                value: totalHeightInInches.toString(),
              })
            );
          }
          heightProcessed = true;
        }
      } else {
        const control = this.healthAttributesForm.get(controlName);
        if (control?.value) {
          submittedAttributes.push(
            new PersonAttributeModel({
              personId: this.currentPersonId,
              attributeTypeRefId: attrType.id,
              value: control.value.toString(),
            })
          );
        }
      }
    });
    this.formSubmitted.emit(submittedAttributes);
  }

  onSkip(): void {
    this.skipStep.emit();
  }

  private getUnitForAttribute(attributeName: string): string {
    switch (attributeName.toLowerCase()) {
      case 'height':
        return 'inches';
      case 'weight':
        return 'lbs';
      case 'activity level':
        return '';
      case 'goal':
        return '';
      default:
        return '';
    }
  }

  private getIconForAttribute(attributeName: string): string {
    switch (attributeName.toLowerCase()) {
      case 'height':
        return 'fa-ruler-vertical';
      case 'weight':
        return 'fa-weight';
      case 'activity level':
        return 'fa-running';
      case 'goal':
        return 'fa-bullseye';
      default:
        return 'fa-info-circle';
    }
  }

  private getClassForAttribute(attributeName: string): string {
    switch (attributeName.toLowerCase()) {
      case 'height':
        return 'height-input';
      case 'weight':
        return 'weight-input';
      case 'activity level':
        return 'activity-input';
      case 'goal':
        return 'goal-input';
      default:
        return '';
    }
  }

  private getOptionsForAttribute(attributeName: string): Array<{ value: string; label: string }> | undefined {
    switch (attributeName.toLowerCase()) {
      case 'activity level':
        return [
          { value: 'sedentary', label: 'Sedentary (little or no exercise)' },
          { value: 'lightly_active', label: 'Lightly Active (light exercise 1-3 days/week)' },
          { value: 'moderately_active', label: 'Moderately Active (moderate exercise 3-5 days/week)' },
          { value: 'very_active', label: 'Very Active (hard exercise 6-7 days/week)' },
          { value: 'extremely_active', label: 'Extremely Active (very hard exercise, physical job)' }
        ];
      case 'goal':
        return [
          { value: 'lose_weight', label: 'Lose Weight' },
          { value: 'maintain_weight', label: 'Maintain Weight' },
          { value: 'gain_weight', label: 'Gain Weight' },
          { value: 'build_muscle', label: 'Build Muscle' },
          { value: 'improve_fitness', label: 'Improve Fitness' }
        ];
      default:
        return undefined;
    }
  }
}
