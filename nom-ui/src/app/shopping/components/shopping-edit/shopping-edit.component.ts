import { Component, OnInit, CUSTOM_ELEMENTS_SCHEMA, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { ShoppingListUpdateRequest } from '../../models/shopping-list-update-request.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-shopping-edit',
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
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './shopping-edit.component.html',
  styleUrls: ['./shopping-edit.component.scss']
})
export class ShoppingEditComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private shoppingService = inject(ShoppingService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  shoppingForm: FormGroup = this.nonNullableFb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]]
  });

  isLoading = false;
  shoppingListId = 0;
  shoppingList: ShoppingListResponseModel | null = null;

  formConfig: BaseFormConfig = {
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
      this.shoppingListId = +params['id'];
      this.loadShoppingList();
    });
  }

  loadShoppingList(): void {
    this.isLoading = true;

    this.shoppingService.getShoppingList(this.shoppingListId).subscribe({
      next: (shoppingList) => {
        this.shoppingList = shoppingList;
        this.shoppingForm.patchValue({
          name: shoppingList.name,
          description: shoppingList.description || ''
        });
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading shopping list:', error);
        this.snackBar.open('Failed to load shopping list details', 'Close', {
          duration: 5000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
        this.router.navigate(['/shopping']);
      }
    });
  }

  onSubmit(): void {
    if (this.shoppingForm.valid && this.shoppingList) {
      this.isLoading = true;

      const updateRequest: ShoppingListUpdateRequest = {
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description
      };

      this.shoppingService.updateShoppingList(this.shoppingListId, updateRequest).subscribe({
        next: () => {
          this.isLoading = false;
          this.snackBar.open('Shopping list updated successfully!', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
          this.router.navigate(['/shopping', this.shoppingListId]);
        },
        error: (error) => {
          this.isLoading = false;
          console.error('Error updating shopping list:', error);
          this.snackBar.open('Failed to update shopping list. Please try again.', 'Close', {
            duration: 5000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
        }
      });
    }
  }

  onCancel(): void {
    this.router.navigate(['/shopping', this.shoppingListId]);
  }
} 