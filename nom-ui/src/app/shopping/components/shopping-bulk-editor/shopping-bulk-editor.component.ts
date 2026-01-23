import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { Subject, takeUntil, finalize, forkJoin } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwCheckboxComponent,
  AmwSelectComponent,
  AmwProgressSpinnerComponent,
  AmwIconComponent,
  AmwDialogService,
} from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel, ShoppingListItemUpdateRequestModel } from '../../models/shopping.model';
import { ShoppingListCategory } from '../../models/shopping-list-category.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'nom-shopping-bulk-editor',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwCheckboxComponent,
    AmwSelectComponent,
    AmwProgressSpinnerComponent,
    AmwIconComponent,
  ],
  templateUrl: './shopping-bulk-editor.component.html',
  styleUrl: './shopping-bulk-editor.component.scss',
})
export class ShoppingBulkEditorComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private notificationService = inject(NotificationService);
  private dialogService = inject(AmwDialogService);
  private fb = inject(FormBuilder);

  // Signals
  shoppingListId = signal<number>(0);
  shoppingList = signal<ShoppingListResponseModel | null>(null);
  categories = signal<ShoppingListCategory[]>([]);
  selectedItemIds = signal<Set<number>>(new Set());
  isLoading = signal(true);
  isProcessing = signal(false);
  error = signal<string | null>(null);

  // Computed
  selectedCount = computed(() => this.selectedItemIds().size);
  hasSelection = computed(() => this.selectedCount() > 0);
  allSelected = computed(() => {
    const list = this.shoppingList();
    if (!list || !list.items || list.items.length === 0) return false;
    return this.selectedItemIds().size === list.items.length;
  });

  // Form
  bulkActionForm: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  constructor() {
    this.bulkActionForm = this.fb.group({
      targetCategory: [''],
    });
  }

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.shoppingListId.set(+id);
        this.loadData();
      } else {
        this.error.set('Invalid shopping list ID');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData(): void {
    this.isLoading.set(true);
    this.error.set(null);

    forkJoin({
      shoppingList: this.shoppingService.getShoppingList(this.shoppingListId()),
      categories: this.shoppingService.getCategories(),
    })
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: ({ shoppingList, categories }) => {
          this.shoppingList.set(shoppingList);
          this.categories.set(categories);
        },
        error: (err) => {
          this.error.set('Failed to load data');
          console.error('Error loading data:', err);
        },
      });
  }

  onToggleItem(itemId: number, checked: boolean | null): void {
    if (checked === null) return;
    const newSet = new Set(this.selectedItemIds());
    if (checked) {
      newSet.add(itemId);
    } else {
      newSet.delete(itemId);
    }
    this.selectedItemIds.set(newSet);
  }

  onToggleAll(checked: boolean | null): void {
    if (checked === null) return;
    if (checked) {
      const allIds = new Set(this.shoppingList()!.items?.map((item) => item.id) || []);
      this.selectedItemIds.set(allIds);
    } else {
      this.selectedItemIds.set(new Set());
    }
  }

  isItemSelected(itemId: number): boolean {
    return this.selectedItemIds().has(itemId);
  }

  onMarkComplete(): void {
    this.bulkUpdateItems(true);
  }

  onMarkIncomplete(): void {
    this.bulkUpdateItems(false);
  }

  private bulkUpdateItems(isCompleted: boolean): void {
    const selectedIds = Array.from(this.selectedItemIds());
    if (selectedIds.length === 0) return;

    this.isProcessing.set(true);
    this.error.set(null);

    const updates = selectedIds.map((itemId) => {
      const item = this.shoppingList()!.items?.find((i) => i.id === itemId);
      if (!item) return null;

      const updateRequest: ShoppingListItemUpdateRequestModel = {
        id: item.id,
        shoppingListId: item.shoppingListId,
        ingredientId: item.ingredientId,
        name: item.name || '',
        quantity: item.quantity || 0,
        measurementUnit: item.measurementUnit || '',
        notes: item.notes,
        categoryId: item.categoryId,
        isCompleted,
      };
      return this.shoppingService.updateItem(itemId, updateRequest);
    }).filter(req => req !== null);

    forkJoin(updates as any[])
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isProcessing.set(false))
      )
      .subscribe({
        next: () => {
          this.notificationService.success(
            `Marked ${selectedIds.length} item(s) as ${isCompleted ? 'complete' : 'incomplete'}`
          );
          this.selectedItemIds.set(new Set());
          this.loadData();
        },
        error: (err) => {
          this.error.set('Failed to update items');
          this.notificationService.error('Failed to update items');
          console.error('Error updating items:', err);
        },
      });
  }

  onChangeCategory(): void {
    const selectedIds = Array.from(this.selectedItemIds());
    if (selectedIds.length === 0) return;

    const targetCategoryId = this.bulkActionForm.value.targetCategory;
    if (!targetCategoryId) {
      this.notificationService.error('Please select a category');
      return;
    }

    this.isProcessing.set(true);
    this.error.set(null);

    const updates = selectedIds.map((itemId) => {
      const item = this.shoppingList()!.items?.find((i) => i.id === itemId);
      if (!item) return null;

      const updateRequest: ShoppingListItemUpdateRequestModel = {
        id: item.id,
        shoppingListId: item.shoppingListId,
        ingredientId: item.ingredientId,
        name: item.name || '',
        quantity: item.quantity || 0,
        measurementUnit: item.measurementUnit || '',
        notes: item.notes,
        categoryId: +targetCategoryId,
        isCompleted: item.isCompleted,
      };
      return this.shoppingService.updateItem(itemId, updateRequest);
    }).filter(req => req !== null);

    forkJoin(updates as any[])
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isProcessing.set(false))
      )
      .subscribe({
        next: () => {
          this.notificationService.success(`Updated category for ${selectedIds.length} item(s)`);
          this.selectedItemIds.set(new Set());
          this.bulkActionForm.patchValue({ targetCategory: '' });
          this.loadData();
        },
        error: (err) => {
          this.error.set('Failed to update category');
          this.notificationService.error('Failed to update category');
          console.error('Error updating category:', err);
        },
      });
  }

  onDeleteSelected(): void {
    const selectedIds = Array.from(this.selectedItemIds());
    if (selectedIds.length === 0) return;

    this.dialogService
      .confirm(
        `Are you sure you want to delete ${selectedIds.length} selected item(s)? This action cannot be undone.`,
        'Delete Items'
      )
      .subscribe((confirmed) => {
        if (confirmed) {
          this.performBulkDelete(selectedIds);
        }
      });
  }

  private performBulkDelete(itemIds: number[]): void {
    this.isProcessing.set(true);
    this.error.set(null);

    const deletes = itemIds.map((itemId) => this.shoppingService.deleteItem(itemId));

    forkJoin(deletes)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isProcessing.set(false))
      )
      .subscribe({
        next: () => {
          this.notificationService.success(`Deleted ${itemIds.length} item(s)`);
          this.selectedItemIds.set(new Set());
          this.loadData();
        },
        error: (err) => {
          this.error.set('Failed to delete items');
          this.notificationService.error('Failed to delete items');
          console.error('Error deleting items:', err);
        },
      });
  }

  onBack(): void {
    this.router.navigate(['/shopping', this.shoppingListId()]);
  }

  onRetry(): void {
    this.loadData();
  }

  getCategoryName(categoryId?: number): string {
    if (!categoryId) return 'Uncategorized';
    const category = this.categories().find((c) => c.id === categoryId);
    return category?.name || 'Uncategorized';
  }
}
