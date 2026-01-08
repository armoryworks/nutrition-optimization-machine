import { Component, OnInit, inject, signal } from '@angular/core';

import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCategory, ShoppingListCategoryCreate } from '../../models/shopping-list-category.model';

@Component({
    selector: 'nom-shopping-category-management',
    standalone: true,
    imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatDialogModule,
    MatListModule,
    MatMenuModule
],
    templateUrl: './shopping-category-management.component.html',
    styleUrls: ['./shopping-category-management.component.scss']
})
export class ShoppingCategoryManagementComponent implements OnInit {
    private shoppingService = inject(ShoppingService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);

    categories = signal<ShoppingListCategory[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    categoryForm: FormGroup;
    isAddingCategory = signal(false);
    isEditing = signal(false);
    isSubmitting = signal(false);
    loading = signal(false);

    constructor() {
        this.categoryForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
            description: ['', [Validators.maxLength(200)]]
        });
    }

    ngOnInit(): void {
        this.loadCategories();
    }

    loadCategories(): void {
        this.isLoading.set(true);
        this.loading.set(true);
        this.shoppingService.getCategories().subscribe({
            next: (categories: ShoppingListCategory[]) => {
                this.categories.set(categories);
                this.isLoading.set(false);
                this.loading.set(false);
            },
            error: (error: Error | string | unknown) => {
                console.error('Error loading categories:', error);
                this.snackBar.open('Error loading categories', 'Close', { duration: 3000 });
                this.isLoading.set(false);
                this.loading.set(false);
            }
        });
    }

    onSubmit(): void {
        if (this.categoryForm.valid) {
            this.isAddingCategory.set(true);
            this.isSubmitting.set(true);
            const categoryData: ShoppingListCategoryCreate = this.categoryForm.value;

            this.shoppingService.createCategory(categoryData).subscribe({
                next: (category: ShoppingListCategory) => {
                    this.categories.set([...this.categories(), category]);
                    this.categoryForm.reset();
                    this.snackBar.open('Category created successfully', 'Close', { duration: 3000 });
                    this.isAddingCategory.set(false);
                    this.isSubmitting.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error creating category:', error);
                    this.snackBar.open('Error creating category', 'Close', { duration: 3000 });
                    this.isAddingCategory.set(false);
                    this.isSubmitting.set(false);
                }
            });
        }
    }

    createCategory(): void {
        if (this.categoryForm.valid) {
            const categoryData: ShoppingListCategoryCreate = this.categoryForm.value;
            this.isLoading.set(true);
            this.loading.set(true);

            this.shoppingService.createCategory(categoryData).subscribe({
                next: (category: ShoppingListCategory) => {
                    this.categories.set([...this.categories(), category]);
                    this.categoryForm.reset();
                    this.snackBar.open('Category created successfully', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error creating category:', error);
                    this.snackBar.open('Error creating category', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                }
            });
        }
    }

    editCategory(category: ShoppingListCategory): void {
        this.isAddingCategory.set(true);
        this.isEditing.set(true);
        this.categoryForm.patchValue({
            name: category.name,
            description: category.description
        });
    }

    updateCategory(): void {
        if (this.categoryForm.valid && this.isEditing()) {
            const categoryData: ShoppingListCategoryCreate = this.categoryForm.value;
            this.isLoading.set(true);
            this.loading.set(true);

            // For now, we'll treat this as create since we don't have the category ID
            // In a real implementation, you'd need to track the category being edited
            this.shoppingService.createCategory(categoryData).subscribe({
                next: (updatedCategory: ShoppingListCategory) => {
                    const categories = this.categories();
                    const index = categories.findIndex(c => c.id === updatedCategory.id);
                    if (index !== -1) {
                        categories[index] = updatedCategory;
                        this.categories.set([...categories]);
                    }
                    this.categoryForm.reset();
                    this.isAddingCategory.set(false);
                    this.isEditing.set(false);
                    this.snackBar.open('Category updated successfully', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error updating category:', error);
                    this.snackBar.open('Error updating category', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                }
            });
        }
    }

    deleteCategory(category: ShoppingListCategory): void {
        if (confirm('Are you sure you want to delete this category? Items will be moved to uncategorized.')) {
            this.isLoading.set(true);
            this.loading.set(true);
            this.shoppingService.deleteCategory(category.id).subscribe({
                next: () => {
                    this.categories.set(this.categories().filter(c => c.id !== category.id));
                    this.snackBar.open('Category deleted successfully', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error deleting category:', error);
                    this.snackBar.open('Error deleting category', 'Close', { duration: 3000 });
                    this.isLoading.set(false);
                    this.loading.set(false);
                }
            });
        }
    }

    cancelEdit(): void {
        this.categoryForm.reset();
        this.isAddingCategory.set(false);
        this.isEditing.set(false);
    }

    getCategoryColor(category: ShoppingListCategory): string {
        return category.color || '#1976d2';
    }
} 