import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwInlineLoadingComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { ShoppingListUpdateRequest } from '../../models/shopping-list-update-request.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-shopping-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwInlineLoadingComponent,
    AmwValidationTooltipDirective
],
  templateUrl: './shopping-edit.component.html',
  styleUrls: ['./shopping-edit.component.scss']
})
export class ShoppingEditComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private shoppingService = inject(ShoppingService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  shoppingForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]]
  });

  isLoading = signal(false);
  shoppingListId = signal(0);
  shoppingList = signal<ShoppingListResponseModel | null>(null);
  validationContext!: ValidationContext;

  formConfig = {
    title: 'Edit Shopping List',
    subtitle: 'Update your shopping list information',
    submitText: 'Update Shopping List',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor() {
    // Form is now initialized at declaration
  }

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.shoppingListId.set(+params['id']);
      this.loadShoppingList();
    });

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

  loadShoppingList(): void {
    this.isLoading.set(true);

    this.shoppingService.getShoppingList(this.shoppingListId()).subscribe({
      next: (shoppingList) => {
        this.shoppingList.set(shoppingList);
        this.shoppingForm.patchValue({
          name: shoppingList.name,
          description: shoppingList.description || ''
        });
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading shopping list:', error);
        this.notificationService.error(ERROR_MESSAGES.SHOPPING.LOAD_FAILED);
        this.router.navigate(['/shopping']);
      }
    });
  }

  onSubmit(): void {
    if (this.shoppingForm.valid && this.shoppingList()) {
      this.isLoading.set(true);

      const updateRequest: ShoppingListUpdateRequest = {
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description
      };

      this.shoppingService.updateShoppingList(this.shoppingListId(), updateRequest)
        .pipe(loading('Updating shopping list...'))
        .subscribe({
          next: () => {
            this.isLoading.set(false);
            this.notificationService.success('Shopping list updated successfully!');
            this.router.navigate(['/shopping', this.shoppingListId()]);
          },
          error: (error) => {
            this.isLoading.set(false);
            console.error('Error updating shopping list:', error);
            this.notificationService.error(ERROR_MESSAGES.SHOPPING.SAVE_FAILED);
          }
        });
    }
  }

  onCancel(): void {
    this.router.navigate(['/shopping', this.shoppingListId()]);
  }
} 