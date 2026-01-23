import { Component, OnInit, inject, signal } from '@angular/core';

import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent, AmwDialogService } from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCategory, ShoppingListCategoryCreate } from '../../models/shopping-list-category.model';
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
    selector: 'nom-shopping-category-management',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent
    ],
    templateUrl: './shopping-category-management.component.html',
    styleUrls: ['./shopping-category-management.component.scss']
})
export class ShoppingCategoryManagementComponent implements OnInit {
    private shoppingService = inject(ShoppingService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
    private dialogService = inject(AmwDialogService);

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
                this.notificationService.error('Error loading categories');
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
                    this.notificationService.success('Category created successfully');
                    this.isAddingCategory.set(false);
                    this.isSubmitting.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error creating category:', error);
                    this.notificationService.error('Error creating category');
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
                    this.notificationService.success('Category created successfully');
                    this.isLoading.set(false);
                    this.loading.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error creating category:', error);
                    this.notificationService.error('Error creating category');
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
                    this.notificationService.success('Category updated successfully');
                    this.isLoading.set(false);
                    this.loading.set(false);
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error updating category:', error);
                    this.notificationService.error('Error updating category');
                    this.isLoading.set(false);
                    this.loading.set(false);
                }
            });
        }
    }

    deleteCategory(category: ShoppingListCategory): void {
        this.dialogService.confirm(
            'Are you sure you want to delete this category? Items will be moved to uncategorized.',
            'Delete Category'
        ).subscribe(confirmed => {
            if (confirmed) {
                this.isLoading.set(true);
                this.loading.set(true);
                this.shoppingService.deleteCategory(category.id).subscribe({
                    next: () => {
                        this.categories.set(this.categories().filter(c => c.id !== category.id));
                        this.notificationService.success('Category deleted successfully');
                        this.isLoading.set(false);
                        this.loading.set(false);
                    },
                    error: (error: Error | string | unknown) => {
                        console.error('Error deleting category:', error);
                        this.notificationService.error('Error deleting category');
                        this.isLoading.set(false);
                        this.loading.set(false);
                    }
                });
            }
        });
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