import { Component, OnInit, inject } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatCheckboxModule } from '@angular/material/checkbox';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel, ShoppingListItemUpdateRequestModel } from '../../models/shopping.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BaseDetailComponent, BaseDetailConfig } from '../../../common/components/base-detail/base-detail.component';
import { ShoppingItemDialogComponent } from '../shopping-item-dialog/shopping-item-dialog.component';

@Component({
  selector: 'nom-shopping-detail',
  standalone: true,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule,
    MatCheckboxModule,
    BaseDetailComponent
],
  templateUrl: './shopping-detail.component.html',
  styleUrls: ['./shopping-detail.component.scss']
})
export class ShoppingDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  shoppingList: ShoppingListResponseModel | null = null;
  isLoading = true;
  error: string | null = null;
  shoppingListId = 0;

  detailConfig: BaseDetailConfig = {
    title: 'Shopping List Details',
    subtitle: 'View and manage shopping list items',
    showBackButton: true,
    maxWidth: '800px',
  };


  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.shoppingListId = +params['id'];
      this.loadShoppingList();
    });
  }

  loadShoppingList(): void {
    this.isLoading = true;
    this.error = null;

    this.shoppingService.getShoppingList(this.shoppingListId).subscribe({
      next: (shoppingList) => {
        this.shoppingList = shoppingList;
        this.isLoading = false;
      },
      error: (error: Error | string | unknown) => {
        console.error('Error loading shopping list:', error);
        this.error = 'Failed to load shopping list details';
        this.isLoading = false;
      }
    });
  }

  onBack(): void {
    this.router.navigate(['/shopping']);
  }

  onRetry(): void {
    this.loadShoppingList();
  }

  onEditShoppingList(): void {
    this.router.navigate(['/shopping', this.shoppingListId, 'edit']);
  }

  onDeleteShoppingList(): void {
    if (!this.shoppingList) return;

    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Shopping List',
        message: `Are you sure you want to delete "${this.shoppingList.name}"? This action cannot be undone.`,
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.shoppingService.deleteShoppingList(this.shoppingListId).subscribe({
          next: () => {
            this.snackBar.open('Shopping list deleted successfully', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
            this.router.navigate(['/shopping']);
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting shopping list:', error);
            this.snackBar.open('Failed to delete shopping list', 'Close', {
              duration: 5000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
          }
        });
      }
    });
  }

  onAddItem(): void {
    // Open dialog to add new item
    const dialogRef = this.dialog.open(ShoppingItemDialogComponent, {
      width: '500px',
      data: {
        shoppingListId: this.shoppingListId,
        mode: 'add'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.shoppingService.addShoppingListItem(this.shoppingListId, result).subscribe({
          next: () => {
            this.snackBar.open('Item added successfully', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
            this.loadShoppingList(); // Refresh the list
          },
          error: (error: Error | string | unknown) => {
            console.error('Error adding item:', error);
            this.snackBar.open('Failed to add item', 'Close', {
              duration: 5000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
          }
        });
      }
    });
  }

  onToggleItemComplete(itemId: number): void {
    if (!this.shoppingList) return;

    const item = this.shoppingList.items?.find(i => i.id === itemId);
    if (!item) return;

    const updatedItem: ShoppingListItemUpdateRequestModel = {
      id: item.id,
      shoppingListId: item.shoppingListId,
      ingredientId: item.ingredientId,
      name: item.name,
      quantity: item.quantity || 0,
      measurementUnit: item.measurementUnit || '',
      notes: item.notes,
      categoryId: item.categoryId,
      isCompleted: !item.isCompleted
    };

    this.shoppingService.updateShoppingListItem(this.shoppingListId, itemId, updatedItem).subscribe({
      next: () => {
        this.snackBar.open(
          updatedItem.isCompleted ? 'Item marked as complete' : 'Item marked as incomplete',
          'Close',
          {
            duration: 2000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
          }
        );
        this.loadShoppingList(); // Refresh the list
      },
      error: (error: Error | string | unknown) => {
        console.error('Error updating item:', error);
        this.snackBar.open('Failed to update item', 'Close', {
          duration: 5000,
          horizontalPosition: 'center',
          verticalPosition: 'top'
        });
      }
    });
  }

  onDeleteItem(itemId: number): void {
    const dialogRef = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        title: 'Delete Item',
        message: 'Are you sure you want to delete this item?',
        confirmText: 'Delete',
        cancelText: 'Cancel',
        confirmColor: 'warn'
      }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.shoppingService.deleteShoppingListItem(this.shoppingListId, itemId).subscribe({
          next: () => {
            this.snackBar.open('Item deleted successfully', 'Close', {
              duration: 3000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
            this.loadShoppingList(); // Refresh the list
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting item:', error);
            this.snackBar.open('Failed to delete item', 'Close', {
              duration: 5000,
              horizontalPosition: 'center',
              verticalPosition: 'top'
            });
          }
        });
      }
    });
  }

  getProgressPercentage(): number {
    if (!this.shoppingList || !this.shoppingList.totalItems || this.shoppingList.totalItems === 0) return 0;
    return Math.round((this.shoppingList.completedItems / this.shoppingList.totalItems) * 100);
  }

  getProgressColor(percentage: number): string {
    if (percentage >= 80) return 'accent';
    if (percentage >= 50) return 'primary';
    return 'warn';
  }
} 