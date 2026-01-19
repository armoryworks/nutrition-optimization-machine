import { Component, OnInit, inject, signal } from '@angular/core';

import { NonNullableFormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { ShoppingListUpdateRequest } from '../../models/shopping-list-update-request.model';
import { BaseFormConfig } from '../../../common/components/base-form/base-form.component';

@Component({
  selector: 'nom-shopping-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatProgressSpinnerModule,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwButtonComponent,
    AmwCardComponent
],
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

  isLoading = signal(false);
  shoppingListId = signal(0);
  shoppingList = signal<ShoppingListResponseModel | null>(null);

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
      this.shoppingListId.set(+params['id']);
      this.loadShoppingList();
    });
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
    if (this.shoppingForm.valid && this.shoppingList()) {
      this.isLoading.set(true);

      const updateRequest: ShoppingListUpdateRequest = {
        name: this.shoppingForm.value.name,
        description: this.shoppingForm.value.description
      };

      this.shoppingService.updateShoppingList(this.shoppingListId(), updateRequest).subscribe({
        next: () => {
          this.isLoading.set(false);
          this.snackBar.open('Shopping list updated successfully!', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          });
          this.router.navigate(['/shopping', this.shoppingListId()]);
        },
        error: (error) => {
          this.isLoading.set(false);
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
    this.router.navigate(['/shopping', this.shoppingListId()]);
  }
} 