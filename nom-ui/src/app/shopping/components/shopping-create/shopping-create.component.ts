import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCreateRequestModel } from '../../models/shopping-list-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-shopping-create',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwValidationTooltipDirective
],
  templateUrl: './shopping-create.component.html',
  styleUrls: ['./shopping-create.component.scss']
})
export class ShoppingCreateComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private shoppingService = inject(ShoppingService);
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private userInfoService = inject(UserInfoService);
  private validationService = inject(AmwValidationService);

  shoppingForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = signal(false);
  validationContext!: ValidationContext;

  formConfig = {
    title: 'Create Shopping List',
    subtitle: 'Create a new shopping list to organize your groceries',
    submitText: 'Create Shopping List',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor() {
    // Form is now initialized at declaration
  }

  ngOnInit(): void {
    // No need to set AuthorId - it will be handled by the backend

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Name validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-required',
      message: 'Shopping list name is required',
      severity: 'error',
      field: 'name',
      control: this.shoppingForm.get('name') ?? undefined,
      validator: () => !this.shoppingForm.get('name')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-minlength',
      message: 'Name must be at least 2 characters',
      severity: 'error',
      field: 'name',
      control: this.shoppingForm.get('name') ?? undefined,
      validator: () => !this.shoppingForm.get('name')?.hasError('minlength')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'name-maxlength',
      message: 'Name cannot exceed 100 characters',
      severity: 'error',
      field: 'name',
      control: this.shoppingForm.get('name') ?? undefined,
      validator: () => !this.shoppingForm.get('name')?.hasError('maxlength')
    });

    // Description validation (optional field)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'description-maxlength',
      message: 'Description cannot exceed 500 characters',
      severity: 'error',
      field: 'description',
      control: this.shoppingForm.get('description') ?? undefined,
      validator: () => !this.shoppingForm.get('description')?.hasError('maxlength')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  onSubmit(): void {
    if (this.shoppingForm.valid) {
      this.isLoading.set(true);

      const createRequest = new ShoppingListCreateRequestModel({
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description,
        householdId: this.shoppingForm.value.householdId
      });

      this.shoppingService.createShoppingList(createRequest)
        .pipe(loading('Creating shopping list...'))
        .subscribe({
          next: (response) => {
            this.isLoading.set(false);
            this.notificationService.success('Shopping list created successfully!');
            this.router.navigate(['/shopping', response.id]);
          },
          error: (error) => {
            this.isLoading.set(false);
            console.error('Error creating shopping list:', error);
            this.notificationService.error(ERROR_MESSAGES.SHOPPING.SAVE_FAILED);
          }
        });
    }
  }

  onCancel(): void {
    this.router.navigate(['/shopping']);
  }
} 