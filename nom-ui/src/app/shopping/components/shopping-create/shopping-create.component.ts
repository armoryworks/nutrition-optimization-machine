import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCreateRequestModel } from '../../models/shopping-list-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-shopping-create',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent
],
  templateUrl: './shopping-create.component.html',
  styleUrls: ['./shopping-create.component.scss']
})
export class ShoppingCreateComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private shoppingService = inject(ShoppingService);
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private userInfoService = inject(UserInfoService);

  shoppingForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = signal(false);

  formConfig: BaseFormConfig = {
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
  }

  onSubmit(): void {
    if (this.shoppingForm.valid) {
      this.isLoading.set(true);

      const createRequest = new ShoppingListCreateRequestModel({
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description,
        householdId: this.shoppingForm.value.householdId
      });

      this.shoppingService.createShoppingList(createRequest).subscribe({
        next: (response) => {
          this.isLoading.set(false);
          this.notificationService.success('Shopping list created successfully!');
          this.router.navigate(['/shopping', response.id]);
        },
        error: (error) => {
          this.isLoading.set(false);
          console.error('Error creating shopping list:', error);
          this.notificationService.error('Failed to create shopping list. Please try again.');
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/shopping']);
  }
} 