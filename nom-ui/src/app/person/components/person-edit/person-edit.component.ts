import { Component, OnInit, Input, Output, EventEmitter, ViewEncapsulation, OnDestroy, OnChanges, SimpleChanges, inject } from '@angular/core';
import {
  FormGroup,
  Validators,
  NonNullableFormBuilder,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { PersonModel } from '../../models/person.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { Subject } from 'rxjs';

@Component({
  selector: 'nom-person-edit',
  standalone: true,
  imports: [
    CommonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    BaseFormComponent,
    BasePageComponent,
  ],
  templateUrl: './person-edit.component.html',
  styleUrls: ['./person-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PersonEditComponent implements OnInit, OnDestroy, OnChanges {
  private fb = inject(NonNullableFormBuilder);

  @Input() person: PersonModel | null = null;
  @Output() formSubmitted = new EventEmitter<PersonModel>();
  @Output() skipStep = new EventEmitter<void>();

  personForm: FormGroup;
  isSubmitting = false;
  isLoading = false;
  error: string | null = null;

  pageConfig: BasePageConfig = {
    title: 'Edit Person',
    subtitle: 'Update your personal information',
    showBackButton: true,
    maxWidth: '600px',
  };

  formConfig: BaseFormConfig = {
    title: '',
    subtitle: '',
    submitText: 'Save Changes',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '100%',
  };

  private destroy$ = new Subject<void>();



  constructor() {
    // Always initialize the form group with default/empty values
    this.personForm = this.fb.group({
      name: ['', [Validators.required]],
      // Add other fields as needed
    });
  }

  ngOnInit(): void {
    if (this.person) {
      this.personForm.patchValue({
        name: this.person.name || '',
        // Add other fields as needed
      });
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['person'] && this.person) {
      this.personForm.patchValue({
        name: this.person.name || '',
        // Add other fields as needed
      });
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onSubmit(): void {
    this.isSubmitting = true;
    this.error = null;
    this.personForm.markAllAsTouched();

    if (this.personForm.valid) {
      const updatedPerson: PersonModel = new PersonModel({
        id: this.person?.id || 0,
        name: this.personForm.get('name')?.value,
        // ... map other form values to PersonModel properties
      });
      this.formSubmitted.emit(updatedPerson);
    } else {
      console.error('Person details form is invalid. Please correct the errors.');
      this.error = 'Please correct the form errors before submitting.';
    }
    this.isSubmitting = false;
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
    this.error = null;
  }

  submitForm(): void {
    this.onSubmit();
  }
}
