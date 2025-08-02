import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
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

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BaseDetailComponent, BaseDetailConfig } from '../../../common/components/base-detail/base-detail.component';

@Component({
  selector: 'app-shopping-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule,
    BaseDetailComponent,
  ],
  templateUrl: './shopping-detail.component.html',
  styleUrls: ['./shopping-detail.component.scss']
})
export class ShoppingDetailComponent implements OnInit {
  shoppingList: ShoppingListResponseModel | null = null;
  isLoading = true;
  error: string | null = null;
  shoppingListId: number = 0;

  detailConfig: BaseDetailConfig = {
    title: 'Shopping List Details',
    subtitle: 'View and manage shopping list items',
    showBackButton: true,
    maxWidth: '800px',
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private shoppingService: ShoppingService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) { }

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
      error: (error) => {
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
          error: (error) => {
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
    // TODO: Implement add item functionality
    this.snackBar.open('Add item functionality coming soon', 'Close', {
      duration: 3000,
      horizontalPosition: 'center',
      verticalPosition: 'top'
    });
  }

  onToggleItemComplete(itemId: number): void {
    // TODO: Implement toggle item completion
    this.snackBar.open('Toggle completion functionality coming soon', 'Close', {
      duration: 3000,
      horizontalPosition: 'center',
      verticalPosition: 'top'
    });
  }

  onDeleteItem(itemId: number): void {
    // TODO: Implement delete item
    this.snackBar.open('Delete item functionality coming soon', 'Close', {
      duration: 3000,
      horizontalPosition: 'center',
      verticalPosition: 'top'
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