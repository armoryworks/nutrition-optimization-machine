import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCreateRequestModel } from '../../models/shopping.model';
import { UserInfoService } from '../../../utilities/services/user-info.service';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'app-shopping-create',
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
  shoppingForm: FormGroup;
  isLoading = false;

  formConfig: BaseFormConfig = {
    title: 'Create Shopping List',
    subtitle: 'Create a new shopping list to organize your purchases',
    submitText: 'Create Shopping List',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor(
    private formBuilder: FormBuilder,
    private shoppingService: ShoppingService,
    private router: Router,
    private snackBar: MatSnackBar,
    private userInfoService: UserInfoService
  ) {
    this.shoppingForm = this.formBuilder.group({
      Name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      Description: ['', [Validators.maxLength(500)]],
      GroupId: [null],
      AuthorId: [null]
    });
  }

  ngOnInit(): void {
    // Set default values or get from current user context
    const currentPersonId = this.userInfoService.getCurrentUserInfoValue()?.personId;
    this.shoppingForm.patchValue({
      AuthorId: currentPersonId || 1 // Use current person ID or fallback
    });
  }

  onSubmit(): void {
    if (this.shoppingForm.valid) {
      this.isLoading = true;

      const createRequest = new ShoppingListCreateRequestModel({
        name: this.shoppingForm.value.Name,
        description: this.shoppingForm.value.Description
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