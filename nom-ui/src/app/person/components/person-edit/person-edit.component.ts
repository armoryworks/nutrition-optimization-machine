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
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { PersonModel } from '../../models/person.model';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-person-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule, // Assuming you have a button in the template that triggers submitForm
  ],
  templateUrl: './person-edit.component.html',
  styleUrls: ['./person-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonEditComponent implements OnInit {
  @Input() person: PersonModel | null = null;
  @Output() formSubmitted = new EventEmitter<PersonModel>();
  @Output() skipStep = new EventEmitter<void>(); // If there's a skip option for this step

  personForm!: FormGroup;

  constructor(private fb: NonNullableFormBuilder) {}

  ngOnInit(): void {
    this.personForm = this.fb.group({
      name: [this.person?.name || '', Validators.required],
      // Add other fields as per your PersonModel and FR-1.2
      // e.g., gender: [this.person?.gender || '', Validators.required],
    });
  }

  // This public method is what the parent (OnboardingWorkflowComponent) will call
  public submitForm(): void {
    this.personForm.markAllAsTouched(); // Show validation errors

    if (this.personForm.valid) {
      const updatedPerson: PersonModel = new PersonModel({
        // Assuming personId is handled by parent or backend on first create
        id: this.person?.id || 0, // Keep existing ID if present, otherwise 0 for new
        name: this.personForm.get('name')?.value,
        // ... map other form values to PersonModel properties
      });
      this.formSubmitted.emit(updatedPerson);
    } else {
      this.formSubmitted.error('Form is invalid'); // Optionally emit an error or just do nothing
      console.error(
        'Person details form is invalid. Please correct the errors.'
      );
    }
  }

  onSkip(): void {
    this.skipStep.emit();
  }
}
