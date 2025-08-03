import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
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
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';

@Component({
    selector: 'nom-shopping-dashboard',
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
        MatFormFieldModule,
        MatInputModule,
        ReactiveFormsModule,
        BasePageComponent,
    ],
    templateUrl: './shopping-dashboard.component.html',
    styleUrls: ['./shopping-dashboard.component.scss']
})
export class ShoppingDashboardComponent implements OnInit {
    shoppingLists: ShoppingListResponseModel[] = [];
    filteredLists: ShoppingListResponseModel[] = [];
    isLoading = true;
    error: string | null = null;
    searchControl = new FormControl('');

    pageConfig: BasePageConfig = {
        title: 'Shopping Lists',
        subtitle: 'Manage your shopping lists and track your purchases',
        showRefreshButton: true,
        refreshButtonText: 'Refresh',
        maxWidth: '1200px',
    };

    constructor(
        private router: Router,
        private shoppingService: ShoppingService,
        private snackBar: MatSnackBar,
        private dialog: MatDialog
    ) { }

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
        this.isLoading = true;
        this.error = null;

        this.shoppingService.getShoppingLists().subscribe({
            next: (shoppingLists) => {
                this.shoppingLists = shoppingLists;
                this.filteredLists = [...this.shoppingLists];
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading shopping lists:', error);
                this.error = 'Failed to load shopping lists';
                this.isLoading = false;
            }
        });
    }

    filterLists(searchTerm: string): void {
        if (!searchTerm.trim()) {
            this.filteredLists = [...this.shoppingLists];
        } else {
            const term = searchTerm.toLowerCase();
            this.filteredLists = this.shoppingLists.filter(list =>
                list.name.toLowerCase().includes(term) ||
                list.description?.toLowerCase().includes(term)
            );
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
        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            width: '400px',
            data: {
                title: 'Delete Shopping List',
                message: 'Are you sure you want to delete this shopping list? This action cannot be undone.',
                confirmText: 'Delete',
                cancelText: 'Cancel',
                confirmColor: 'warn'
            }
        });

        dialogRef.afterClosed().subscribe(result => {
            if (result) {
                this.shoppingService.deleteShoppingList(listId).subscribe({
                    next: () => {
                        this.snackBar.open('Shopping list deleted successfully', 'Close', {
                            duration: 3000,
                            horizontalPosition: 'center',
                            verticalPosition: 'top'
                        });
                        this.loadShoppingLists();
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

    onRefresh(): void {
        this.loadShoppingLists();
    }

    onRetry(): void {
        this.loadShoppingLists();
    }

    getProgressPercentage(list: ShoppingListResponseModel): number {
        if (!list.totalItems || list.totalItems === 0) return 0;
        return Math.round((list.completedItems / list.totalItems) * 100);
    }

    getProgressColor(percentage: number): string {
        if (percentage >= 80) return 'accent';
        if (percentage >= 50) return 'primary';
        return 'warn';
    }
} 