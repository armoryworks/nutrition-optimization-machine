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
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PersonModel } from '../../models/person.model';

@Component({
  selector: 'app-person-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './person-edit.component.html',
  styleUrls: ['./person-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonEditComponent implements OnInit {
  @Input() person: PersonModel = new PersonModel(); // Input to pre-populate form
  @Output() formSubmitted = new EventEmitter<PersonModel>();
  @Output() skipStep = new EventEmitter<void>(); // For potential 'skip' functionality (though this step is required)

  personForm!: FormGroup;

  constructor(private fb: NonNullableFormBuilder) {}

  ngOnInit(): void {
    this.personForm = this.fb.group({
      name: [
        this.person.name,
        [Validators.required, Validators.maxLength(100)],
      ],
      // Add other form controls for person properties (e.g., dateOfBirth, gender)
      // dateOfBirth: [this.person.dateOfBirth, [Validators.required]],
      // gender: [this.person.gender, [Validators.required]],
    });
  }

  /**
   * Emits the current form value as a PersonModel.
   * Called by the parent workflow component's "Next" button.
   */
  submitForm(): void {
    this.personForm.markAllAsTouched();
    if (this.personForm.valid) {
      const updatedPerson = new PersonModel({
        ...this.person,
        ...this.personForm.getRawValue(),
      });
      this.formSubmitted.emit(updatedPerson);
    }
  }
}
