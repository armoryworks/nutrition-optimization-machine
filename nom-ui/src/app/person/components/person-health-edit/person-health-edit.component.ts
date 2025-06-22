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

  // Mock data for attribute types. In a real app, this would come from a service fetching Reference Data.
  attributeTypes = [
    {
      id: this.HEIGHT_ATTRIBUTE_ID,
      name: this.HEIGHT_IN_FEET_NAME,
      label: 'Height',
      unit: 'feet',
      icon: 'fa-ruler-vertical',
      class: 'half-width',
    },
    {
      id: this.HEIGHT_ATTRIBUTE_ID,
      name: this.HEIGHT_IN_INCHES_NAME,
      label: 'Height',
      unit: 'inches',
      icon: 'fa-ruler-vertical',
      class: 'half-width',
    }, // Example AttributeTypeRefIds
    { id: 2001, name: 'Weight', unit: 'lbs', icon: 'fa-weight-hanging' },
    {
      id: 2002,
      name: 'Activity Level',
      label: 'Activity Level',
      icon: 'fa-person-running',
      options: [
        'Sedentary',
        'Lightly Active',
        'Moderately Active',
        'Very Active',
      ],
    },
  ];

  constructor(private fb: NonNullableFormBuilder) {}

  ngOnInit(): void {
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
}
