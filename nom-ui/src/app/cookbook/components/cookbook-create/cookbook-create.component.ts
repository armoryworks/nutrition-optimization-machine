import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';

import { AmwInputComponent, AmwTextareaComponent, AmwCheckboxComponent, AmwButtonComponent, AmwIconComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { CookbookService } from '../../services/cookbook.service';
import { CookbookCreateRequestModel } from '../../models/cookbook-create-request.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-cookbook-create',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwCheckboxComponent,
    AmwButtonComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './cookbook-create.component.html',
  styleUrls: ['./cookbook-create.component.scss']
})
export class CookbookCreateComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private cookbookService = inject(CookbookService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  cookbookForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(2047)]],
    isPublic: [false]
  });

  isLoading = signal(false);
  validationContext!: ValidationContext;

  ngOnInit(): void {
    const householdId = Number(this.route.snapshot.queryParamMap.get('householdId'));
    if (householdId) {
      this.cookbookForm.patchValue({ householdId });
    }

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-required',
      message: 'Cookbook name is required',
      severity: 'error',
      field: 'name',
      control: this.cookbookForm.get('name') ?? undefined,
      validator: () => !this.cookbookForm.get('name')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-minlength',
      message: 'Cookbook name must be at least 2 characters',
      severity: 'error',
      field: 'name',
      control: this.cookbookForm.get('name') ?? undefined,
      validator: () => !this.cookbookForm.get('name')?.hasError('minlength')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-maxlength',
      message: 'Cookbook name cannot exceed 255 characters',
      severity: 'error',
      field: 'name',
      control: this.cookbookForm.get('name') ?? undefined,
      validator: () => !this.cookbookForm.get('name')?.hasError('maxlength')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'description-maxlength',
      message: 'Description cannot exceed 2047 characters',
      severity: 'error',
      field: 'description',
      control: this.cookbookForm.get('description') ?? undefined,
      validator: () => !this.cookbookForm.get('description')?.hasError('maxlength')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  onSubmit(): void {
    if (this.cookbookForm.valid) {
      this.isLoading.set(true);

      const formValue = this.cookbookForm.value;
      const createRequest = new CookbookCreateRequestModel({
        name: formValue.name,
        description: formValue.description,
        isPublic: formValue.isPublic,
        householdId: formValue.householdId || 0
      });

      this.cookbookService.createCookbook(createRequest)
        .pipe(loading('Creating cookbook...'))
        .subscribe({
          next: (id) => {
            this.isLoading.set(false);
            this.notificationService.success('Cookbook created successfully!');
            this.router.navigate(['/cookbook', id]);
          },
          error: (error) => {
            this.isLoading.set(false);
            console.error('Error creating cookbook:', error);
            this.notificationService.error(ERROR_MESSAGES.COOKBOOK.SAVE_FAILED);
          }
        });
    }
  }

  onCancel(): void {
    this.router.navigate(['/cookbook']);
  }
}
