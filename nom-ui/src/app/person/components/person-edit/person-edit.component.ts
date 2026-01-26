import { Component, OnInit, input, output, ViewEncapsulation, OnDestroy, inject, signal, effect } from '@angular/core';
import {
  FormGroup,
  Validators,
  NonNullableFormBuilder,
  ReactiveFormsModule,
} from '@angular/forms';
import { AmwProgressBarComponent } from 'angular-material-wrap';
import { Subject } from 'rxjs';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwInputComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { PersonModel } from '../../models/person.model';

@Component({
  selector: 'nom-person-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwProgressBarComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwInputComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './person-edit.component.html',
  styleUrls: ['./person-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonEditComponent implements OnInit, OnDestroy {
  private fb = inject(NonNullableFormBuilder);
  private validationService = inject(AmwValidationService);

  person = input<PersonModel | null>(null);
  formSubmitted = output<PersonModel>();
  skipStep = output<void>();

  personForm: FormGroup;
  validationContext!: ValidationContext;
  isSubmitting = signal(false);
  isLoading = signal(false);
  error = signal<string | null>(null);

  // Page configuration
  pageTitle = 'Edit Person';
  pageSubtitle = 'Update your personal information';
  submitText = 'Save Changes';
  cancelText = 'Cancel';

  private destroy$ = new Subject<void>();



  constructor() {
    // Always initialize the form group with default/empty values
    this.personForm = this.fb.group({
      name: ['', [Validators.required]],
      // Add other fields as needed
    });

    // Effect to update form when person input changes
    effect(() => {
      const currentPerson = this.person();
      if (currentPerson) {
        this.personForm.patchValue({
          name: currentPerson.name || '',
          // Add other fields as needed
        });
      }
    });
  }

  ngOnInit(): void {
    if (this.person()) {
      this.personForm.patchValue({
        name: this.person()!.name || '',
        // Add other fields as needed
      });
    }

    // Setup ValidationContext
    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Name validation - required
    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-required',
      message: 'Name is required',
      severity: 'error',
      field: 'name',
      control: this.personForm.get('name') ?? undefined,
      validator: () => !this.personForm.get('name')?.hasError('required')
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();

    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  onSubmit(): void {
    this.isSubmitting.set(true);
    this.error.set(null);
    this.personForm.markAllAsTouched();

    if (this.personForm.valid) {
      const updatedPerson: PersonModel = new PersonModel({
        id: this.person()?.id || 0,
        name: this.personForm.get('name')?.value,
        // ... map other form values to PersonModel properties
      });
      this.formSubmitted.emit(updatedPerson);
    } else {
      console.error('Person details form is invalid. Please correct the errors.');
      this.error.set('Please correct the form errors before submitting.');
    }
    this.isSubmitting.set(false);
  }

  onCancel(): void {
    // Handle cancel action if needed
  }

  onSkip(): void {
    this.skipStep.emit();
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

  submitForm(): void {
    this.onSubmit();
  }
}
