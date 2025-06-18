import {
  Component,
  OnInit,
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
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PersonService } from '../../services/person.service';

@Component({
  selector: 'app-person-creation',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './person-creation.component.html',
  styleUrls: ['./person-creation.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonCreationComponent implements OnInit {
  @Output() personCreated = new EventEmitter<number>();

  personForm!: FormGroup;
  isSubmitting: boolean = false;
  error: string | null = null;

  constructor(
    private nonNullableFb: NonNullableFormBuilder,
    private personService: PersonService
  ) {}

  ngOnInit(): void {
    this.initializeForm();
  }

  private initializeForm(): void {
    this.personForm = this.nonNullableFb.group({
      name: new FormControl('', [Validators.required, Validators.minLength(2)]),
    });
  }

  submitPerson(): void {
    if (this.personForm.invalid) {
      this.error = 'Please provide a valid name.';
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const personName = this.personForm.get('name')?.value;

    this.personService
      .createPerson({ personName })
      .pipe(finalize(() => (this.isSubmitting = false)))
      .subscribe({
        next: (response) => {
          this.personCreated.emit(response.id);
        },
        error: (err) => {
          console.error('Error creating person:', err);
          this.error = `Failed to create person: ${
            err.message || 'Unknown error'
          }`;
        },
      });
  }
}
