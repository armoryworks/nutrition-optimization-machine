import { Component, OnInit, output, ViewEncapsulation, OnDestroy, inject, signal } from '@angular/core';
import {
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { finalize, takeUntil } from 'rxjs/operators';
import { Subject } from 'rxjs';
import { AmwProgressBarComponent } from 'angular-material-wrap';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwInputComponent } from 'angular-material-wrap';

import { PersonService } from '../../services/person.service';
import { PersonCreateResponseModel } from '../../models/person-create-response.model';

@Component({
  selector: 'nom-person-creation',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwInputComponent,
  ],
  templateUrl: './person-creation.component.html',
  styleUrls: ['./person-creation.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonCreationComponent implements OnInit, OnDestroy {
  ngOnInit(): void {
    // Initialize person creation form and load any required data
    this.initializeForm();
    this.loadInitialData();
  }

  private initializeForm(): void {
    // TODO: Initialize the person creation form
    console.log('Initializing person creation form');
  }

  private loadInitialData(): void {
    // TODO: Load any initial data needed for person creation
    console.log('Loading initial data for person creation');
  }
  private nonNullableFb = inject(NonNullableFormBuilder);
  private personService = inject(PersonService);

  personSubmitted = output<PersonCreateResponseModel>();

  personForm: FormGroup = this.nonNullableFb.group({
    name: new FormControl('', [Validators.required, Validators.minLength(2)]),
  });

  isSubmitting = signal(false);
  isLoading = signal(false);
  error = signal<string | null>(null);

  // Page configuration
  pageTitle = 'Create Your Profile';
  pageSubtitle = 'Set up your personal profile to get started';
  submitText = 'Create Profile';

  private destroy$ = new Subject<void>();

  constructor() {
    // Form is now initialized at declaration
  }



  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onSubmit(): void {
    if (this.personForm.invalid) {
      this.error.set('Please provide a valid name.');
      return;
    }

    this.isSubmitting.set(true);
    this.error.set(null);

    const personName = this.personForm.get('name')?.value;

    this.personService
      .upsertPerson({ personName })
      .pipe(
        finalize(() => this.isSubmitting.set(false)),
        takeUntil(this.destroy$)
      )
      .subscribe({
        next: (response) => {
          this.personSubmitted.emit(response);
        },
        error: (err) => {
          console.error('Error creating person:', err);
          this.error.set(`Failed to create person: ${err.message || 'Unknown error'}`);
        },
      });
  }

  submitPerson(): void {
    this.onSubmit();
  }

  onBack(): void {
    // Handle back navigation if needed
  }

  onRefresh(): void {
    // Reload data if needed
  }

  onRetry(): void {
    this.error.set(null);
  }
}
