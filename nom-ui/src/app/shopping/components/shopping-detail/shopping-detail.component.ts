import { Component, OnInit, inject, signal, effect, Injector } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';

import { AmwButtonComponent, AmwCheckboxComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, DialogService } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel, ShoppingListItemUpdateRequestModel } from '../../models/shopping.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ShoppingItemEditorComponent } from '../shopping-item-editor/shopping-item-editor.component';

@Component({
  selector: 'nom-shopping-detail',
  standalone: true,
  imports: [
    AmwButtonComponent,
    AmwCheckboxComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective
  ],
  templateUrl: './shopping-detail.component.html',
  styleUrls: ['./shopping-detail.component.scss']
})
export class ShoppingDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(DialogService);
  private injector = inject(Injector);

  shoppingList = signal<ShoppingListResponseModel | null>(null);
  isLoading = signal(true);
  error = signal<string | null>(null);
  shoppingListId = signal(0);

  pageTitle = 'Shopping List Details';
  pageSubtitle = 'View and manage shopping list items';


  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.shoppingListId.set(+params['id']);
      this.loadShoppingList();
    });
  }

  loadShoppingList(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.shoppingService.getShoppingList(this.shoppingListId()).subscribe({
      next: (shoppingList) => {
        this.shoppingList.set(shoppingList);
        this.isLoading.set(false);
      },
      error: (error: Error | string | unknown) => {
        console.error('Error loading shopping list:', error);
        this.error.set('Failed to load shopping list details');
        this.isLoading.set(false);
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
    this.router.navigate(['/shopping', this.shoppingListId(), 'edit']);
  }

  onDeleteShoppingList(): void {
    if (!this.shoppingList()) return;

    this.dialogService.confirm(
      `Are you sure you want to delete "${this.shoppingList()!.name}"? This action cannot be undone.`,
      'Delete Shopping List'
    ).subscribe(result => {
      if (result) {
        this.shoppingService.deleteShoppingList(this.shoppingListId()).subscribe({
          next: () => {
            this.notificationService.success('Shopping list deleted successfully');
            this.router.navigate(['/shopping']);
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting shopping list:', error);
            this.notificationService.error('Failed to delete shopping list');
          }
        });
      }
    });
  }

  onAddItem(): void {
    const dialogRef = this.dialogService.open('Add Item', ShoppingItemEditorComponent, {
      width: '500px'
    });

    // Set data via instance signals
    dialogRef.instance.mode.set('add');

    // Signal-based communication with the editor component
    effect(() => {
      const formData = dialogRef.instance.confirmed();
      if (formData) {
        dialogRef.close(formData);
      }
    }, { injector: this.injector });

    effect(() => {
      if (dialogRef.instance.cancelled()) {
        dialogRef.close();
      }
    }, { injector: this.injector });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.shoppingService.addShoppingListItem(this.shoppingListId(), result).subscribe({
          next: () => {
            this.notificationService.success('Item added successfully');
            this.loadShoppingList();
          },
          error: (error: Error | string | unknown) => {
            console.error('Error adding item:', error);
            this.notificationService.error('Failed to add item');
          }
        });
      }
    });
  }

  onToggleItemComplete(itemId: number): void {
    if (!this.shoppingList()) return;

    const item = this.shoppingList()!.items?.find(i => i.id === itemId);
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

    this.shoppingService.updateShoppingListItem(this.shoppingListId(), itemId, updatedItem).subscribe({
      next: () => {
        this.notificationService.success(
          updatedItem.isCompleted ? 'Item marked as complete' : 'Item marked as incomplete'
        );
        this.loadShoppingList();
      },
      error: (error: Error | string | unknown) => {
        console.error('Error updating item:', error);
        this.notificationService.error('Failed to update item');
      }
    });
  }

  onDeleteItem(itemId: number): void {
    this.dialogService.confirm(
      'Are you sure you want to delete this item?',
      'Delete Item'
    ).subscribe(result => {
      if (result) {
        this.shoppingService.deleteShoppingListItem(this.shoppingListId(), itemId).subscribe({
          next: () => {
            this.notificationService.success('Item deleted successfully');
            this.loadShoppingList();
          },
          error: (error: Error | string | unknown) => {
            console.error('Error deleting item:', error);
            this.notificationService.error('Failed to delete item');
          }
        });
      }
    });
  }

  getProgressPercentage(): number {
    if (!this.shoppingList() || !this.shoppingList()!.totalItems || this.shoppingList()!.totalItems === 0) return 0;
    return Math.round((this.shoppingList()!.completedItems / this.shoppingList()!.totalItems) * 100);
  }

  getProgressColor(percentage: number): string {
    if (percentage >= 80) return 'accent';
    if (percentage >= 50) return 'primary';
    return 'warn';
  }
} 