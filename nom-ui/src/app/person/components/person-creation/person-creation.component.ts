import { Component, OnInit, Output, EventEmitter, ViewEncapsulation, OnDestroy, inject } from '@angular/core';
import {
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { finalize, takeUntil } from 'rxjs/operators';
import { Subject } from 'rxjs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PersonService } from '../../services/person.service';
import { PersonCreateResponseModel } from '../../models/person-create-response.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
  selector: 'nom-person-creation',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    BaseFormComponent,
    BasePageComponent,
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

  @Output() personSubmitted = new EventEmitter<PersonCreateResponseModel>();

  personForm: FormGroup = this.nonNullableFb.group({
    name: new FormControl('', [Validators.required, Validators.minLength(2)]),
  });

  isSubmitting = false;
  isLoading = false;
  error: string | null = null;

  pageConfig: BasePageConfig = {
    title: 'Create Your Profile',
    subtitle: 'Set up your personal profile to get started',
    showBackButton: false,
    maxWidth: '600px'
  };

  formConfig: BaseFormConfig = {
    title: '',
    subtitle: '',
    submitText: 'Create Profile',
    showCancelButton: false,
    maxWidth: '100%'
  };

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
      this.error = 'Please provide a valid name.';
      return;
    }

    this.isSubmitting = true;
    this.error = null;

    const personName = this.personForm.get('name')?.value;

    this.personService
      .upsertPerson({ personName })
      .pipe(
        finalize(() => (this.isSubmitting = false)),
        takeUntil(this.destroy$)
      )
      .subscribe({
        next: (response) => {
          this.personSubmitted.emit(response);
        },
        error: (err) => {
          console.error('Error creating person:', err);
          this.error = `Failed to create person: ${err.message || 'Unknown error'}`;
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
    this.error = null;
  }
}
