import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCreateRequestModel } from '../../models/shopping-list-create-request.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-shopping-create',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    BaseFormComponent,
  ],
  templateUrl: './shopping-create.component.html',
  styleUrls: ['./shopping-create.component.scss']
})
export class ShoppingCreateComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private shoppingService = inject(ShoppingService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private userInfoService = inject(UserInfoService);

  shoppingForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]],
    householdId: [1]
  });

  isLoading = false;

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
      this.isLoading = true;

      const createRequest = new ShoppingListCreateRequestModel({
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description,
        householdId: this.shoppingForm.value.householdId
      });

      this.shoppingService.createShoppingList(createRequest).subscribe({
        next: (response) => {
          this.isLoading = false;
          this.snackBar.open('Shopping list created successfully!', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
          this.router.navigate(['/shopping', response.id]);
        },
        error: (error) => {
          this.isLoading = false;
          console.error('Error creating shopping list:', error);
          this.snackBar.open('Failed to create shopping list. Please try again.', 'Close', {
            duration: 5000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/shopping']);
  }
} 