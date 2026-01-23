import { Component, OnInit, inject, signal } from '@angular/core';

import { Router } from '@angular/router';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent, AmwDialogService } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-shopping-dashboard',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwInputComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent
    ],
    templateUrl: './shopping-dashboard.component.html',
    styleUrls: ['./shopping-dashboard.component.scss']
})
export class ShoppingDashboardComponent implements OnInit {
    private router = inject(Router);
    private shoppingService = inject(ShoppingService);
    private notificationService = inject(NotificationService);
    private dialogService = inject(AmwDialogService);

    shoppingLists = signal<ShoppingListResponseModel[]>([]);
    filteredLists = signal<ShoppingListResponseModel[]>([]);
    isLoading = signal(true);
    error = signal<string | null>(null);
    searchControl = new FormControl('');

    pageTitle = 'Shopping Lists';
    pageSubtitle = 'Manage your shopping lists and track your purchases';



    ngOnInit(): void {
        this.loadShoppingLists();
        this.setupSearchFilter();
    }

    setupSearchFilter(): void {
        this.searchControl.valueChanges.pipe(
            debounceTime(300),
            distinctUntilChanged()
        ).subscribe(searchTerm => {
            this.filterLists(searchTerm || '');
        });
    }

    loadShoppingLists(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.shoppingService.getShoppingLists().subscribe({
            next: (shoppingLists) => {
                this.shoppingLists.set(shoppingLists);
                this.filteredLists.set([...this.shoppingLists()]);
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading shopping lists:', error);
                this.error.set('Failed to load shopping lists');
                this.isLoading.set(false);
            }
        });
    }

    filterLists(searchTerm: string): void {
        if (!searchTerm.trim()) {
            this.filteredLists.set([...this.shoppingLists()]);
        } else {
            const term = searchTerm.toLowerCase();
            this.filteredLists.set(this.shoppingLists().filter(list =>
                list.name.toLowerCase().includes(term) ||
                list.description?.toLowerCase().includes(term)
            ));
        }
    }

    onCreateList(): void {
        this.router.navigate(['/shopping/create']);
    }

    onViewList(listId: number): void {
        this.router.navigate(['/shopping', listId]);
    }

    onEditList(listId: number): void {
        this.router.navigate(['/shopping', listId, 'edit']);
    }

    onDeleteList(listId: number): void {
        this.dialogService.confirm(
            'Are you sure you want to delete this shopping list? This action cannot be undone.',
            'Delete Shopping List'
        ).subscribe(result => {
            if (result) {
                this.shoppingService.deleteShoppingList(listId).subscribe({
                    next: () => {
                        this.notificationService.success('Shopping list deleted successfully');
                        this.loadShoppingLists();
                    },
                    error: (error) => {
                        console.error('Error deleting shopping list:', error);
                        this.notificationService.error('Failed to delete shopping list');
                    }
                });
            }
        });
    }

    onRefresh(): void {
        this.loadShoppingLists();
    }

    onRetry(): void {
        this.loadShoppingLists();
    }

    getProgressPercentage(list: ShoppingListResponseModel): number {
        const totalItems = (list as { totalItems?: number }).totalItems || list.itemCount || 0;
        const completedItems = (list as { completedItems?: number }).completedItems || list.completedItemCount || 0;
        if (!list || !totalItems || totalItems === 0) return 0;
        return Math.round((completedItems / totalItems) * 100);
    }

    getProgressColor(percentage: number): string {
        if (percentage >= 80) return 'accent';
        if (percentage >= 50) return 'primary';
        return 'warn';
    }
} 