import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ShoppingListCategoryService } from '../../services/shopping-list-category.service';
import { ShoppingListCategory, ShoppingListCategoryCreate } from '../../models/shopping-list-category.model';

@Component({
    selector: 'app-shopping-category-management',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        ReactiveFormsModule,
        MatDialogModule,
        MatButtonModule,
        MatIconModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        MatCardModule,
        MatChipsModule,
        MatSnackBarModule
    ],
    templateUrl: './shopping-category-management.component.html',
    styleUrls: ['./shopping-category-management.component.scss']
})
export class ShoppingCategoryManagementComponent implements OnInit {
    categories: ShoppingListCategory[] = [];
    categoryForm: FormGroup;
    isEditing = false;
    editingCategoryId?: number;
    loading = false;

    constructor(
        private categoryService: ShoppingListCategoryService,
        private fb: FormBuilder,
        private snackBar: MatSnackBar
    ) {
        this.categoryForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(255)]],
            description: ['', Validators.maxLength(2047)],
            sortOrder: [0],
            color: ['']
        });
    }

    ngOnInit(): void {
        this.loadCategories();
    }

    loadCategories(): void {
        this.loading = true;
        this.categoryService.getAllCategories().subscribe({
            next: (categories) => {
                this.categories = categories;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error loading categories:', error);
                this.snackBar.open('Error loading categories', 'Close', { duration: 3000 });
                this.loading = false;
            }
        });
    }

    createCategory(): void {
        if (this.categoryForm.valid) {
            const categoryData: ShoppingListCategoryCreate = this.categoryForm.value;
            this.loading = true;

            this.categoryService.createCategory(categoryData).subscribe({
                next: (category) => {
                    this.categories.push(category);
                    this.categoryForm.reset();
                    this.snackBar.open('Category created successfully', 'Close', { duration: 3000 });
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error creating category:', error);
                    this.snackBar.open('Error creating category', 'Close', { duration: 3000 });
                    this.loading = false;
                }
            });
        }
    }

    editCategory(category: ShoppingListCategory): void {
        this.isEditing = true;
        this.editingCategoryId = category.id;
        this.categoryForm.patchValue({
            name: category.name,
            description: category.description,
            sortOrder: category.sortOrder,
            color: category.color
        });
    }

    updateCategory(): void {
        if (this.categoryForm.valid && this.editingCategoryId) {
            const categoryData: ShoppingListCategoryCreate = this.categoryForm.value;
            this.loading = true;

            this.categoryService.updateCategory(this.editingCategoryId, categoryData).subscribe({
                next: (updatedCategory) => {
                    const index = this.categories.findIndex(c => c.id === this.editingCategoryId);
                    if (index !== -1) {
                        this.categories[index] = updatedCategory;
                    }
                    this.categoryForm.reset();
                    this.isEditing = false;
                    this.editingCategoryId = undefined;
                    this.snackBar.open('Category updated successfully', 'Close', { duration: 3000 });
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error updating category:', error);
                    this.snackBar.open('Error updating category', 'Close', { duration: 3000 });
                    this.loading = false;
                }
            });
        }
    }

    deleteCategory(categoryId: number): void {
        if (confirm('Are you sure you want to delete this category? Items will be moved to uncategorized.')) {
            this.loading = true;
            this.categoryService.deleteCategory(categoryId).subscribe({
                next: () => {
                    this.categories = this.categories.filter(c => c.id !== categoryId);
                    this.snackBar.open('Category deleted successfully', 'Close', { duration: 3000 });
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error deleting category:', error);
                    this.snackBar.open('Error deleting category', 'Close', { duration: 3000 });
                    this.loading = false;
                }
            });
        }
    }

    cancelEdit(): void {
        this.categoryForm.reset();
        this.isEditing = false;
        this.editingCategoryId = undefined;
    }

    getCategoryColor(category: ShoppingListCategory): string {
        return category.color || '#1976d2';
    }
} 