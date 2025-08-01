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

@Component({
    selector: 'app-shopping-dashboard',
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

        // TODO: Implement get all shopping lists service method
        // For now, using mock data
        setTimeout(() => {
            this.shoppingLists = [
                {
                    id: 1,
                    name: 'Weekly Groceries',
                    description: 'Essential items for the week',
                    householdId: 1,
                    itemCount: 12,
                    completedCount: 3
                },
                {
                    id: 2,
                    name: 'Party Supplies',
                    description: 'Items for the weekend party',
                    householdId: 1,
                    itemCount: 8,
                    completedCount: 0
                }
            ];
            this.filteredLists = [...this.shoppingLists];
            this.isLoading = false;
        }, 1000);
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
        // TODO: Implement delete confirmation dialog
        this.snackBar.open('Delete functionality not yet implemented', 'Close', {
            duration: 3000,
            horizontalPosition: 'center',
            verticalPosition: 'top'
        });
    }

    onRefresh(): void {
        this.loadShoppingLists();
    }

    getProgressPercentage(list: ShoppingListResponseModel): number {
        if (list.itemCount === 0) return 0;
        return Math.round((list.completedCount / list.itemCount) * 100);
    }

    getProgressColor(percentage: number): string {
        if (percentage >= 80) return 'accent';
        if (percentage >= 50) return 'primary';
        return 'warn';
    }
} 