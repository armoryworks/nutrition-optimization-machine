import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel, ShoppingListUpdateRequestModel } from '../../models/shopping.model';
import { BaseFormComponent, BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'app-shopping-edit',
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
  templateUrl: './shopping-edit.component.html',
  styleUrls: ['./shopping-edit.component.scss']
})
export class ShoppingEditComponent implements OnInit {
  shoppingForm: FormGroup;
  isLoading = false;
  shoppingListId: number = 0;
  shoppingList: ShoppingListResponseModel | null = null;

  formConfig: BaseFormConfig = {
    title: 'Edit Shopping List',
    subtitle: 'Update your shopping list information',
    submitText: 'Update Shopping List',
    showCancelButton: true,
    cancelText: 'Cancel',
    maxWidth: '600px',
  };

  constructor(
    private formBuilder: FormBuilder,
    private shoppingService: ShoppingService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.shoppingForm = this.formBuilder.group({
      Name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      Description: ['', [Validators.maxLength(500)]]
    });
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
          Name: shoppingList.name,
          Description: shoppingList.description || ''
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

      const updateRequest = new ShoppingListUpdateRequestModel({
        name: this.shoppingForm.value.Name,
        description: this.shoppingForm.value.Description
      });

      this.shoppingService.updateShoppingList(this.shoppingListId, updateRequest).subscribe({
        next: (response) => {
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