import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CdkDragDrop, moveItemInArray, DragDropModule } from '@angular/cdk/drag-drop';
import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwInlineLoadingComponent, AmwDialogService, AmwSelectComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListCategory, ShoppingListCategoryCreate } from '../../models/shopping-list-category.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { UserInfoService } from '../../../utilities/services/user-info.service';

@Component({
    selector: 'nom-shopping-category-management',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        DragDropModule,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
        AmwSelectComponent,
        AmwValidationTooltipDirective,
    ],
    templateUrl: './shopping-category-management.component.html',
    styleUrls: ['./shopping-category-management.component.scss']
})
export class ShoppingCategoryManagementComponent implements OnInit, OnDestroy {
    private shoppingService = inject(ShoppingService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
    private dialogService = inject(AmwDialogService);
    private userInfoService = inject(UserInfoService);
    private validationService = inject(AmwValidationService);

    categories = signal<ShoppingListCategory[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    categoryForm: FormGroup;
    isAddingCategory = signal(false);
    isEditing = signal(false);
    isSubmitting = signal(false);
    loading = signal(false);
    editingCategoryId = signal<number | null>(null);
    validationContext!: ValidationContext;

    // Available icons for category selection
    availableIcons = [
        { value: 'shopping_cart', label: 'Shopping Cart' },
        { value: 'local_grocery_store', label: 'Grocery' },
        { value: 'restaurant', label: 'Restaurant' },
        { value: 'fastfood', label: 'Fast Food' },
        { value: 'local_dining', label: 'Dining' },
        { value: 'cake', label: 'Bakery' },
        { value: 'local_cafe', label: 'Cafe' },
        { value: 'local_bar', label: 'Beverages' },
        { value: 'outdoor_grill', label: 'Grill' },
        { value: 'ramen_dining', label: 'Ramen' },
        { value: 'breakfast_dining', label: 'Breakfast' },
        { value: 'dinner_dining', label: 'Dinner' },
        { value: 'set_meal', label: 'Meal' },
        { value: 'egg', label: 'Eggs' },
        { value: 'liquor', label: 'Liquor' },
        { value: 'icecream', label: 'Ice Cream' },
        { value: 'emoji_food_beverage', label: 'Food & Beverage' },
        { value: 'category', label: 'Category' },
    ];

    // Preset colors for categories
    availableColors = [
        '#1976d2', // Blue
        '#388e3c', // Green
        '#d32f2f', // Red
        '#f57c00', // Orange
        '#7b1fa2', // Purple
        '#0097a7', // Cyan
        '#c2185b', // Pink
        '#5d4037', // Brown
        '#616161', // Gray
        '#fbc02d', // Yellow
    ];

    constructor() {
        this.categoryForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
            description: ['', [Validators.maxLength(200)]],
            color: ['#1976d2'],
            icon: ['category'],
            sortOrder: [0]
        });
    }

    ngOnInit(): void {
        this.loadCategories();

        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Name validations
        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-required',
            message: 'Category name is required',
            severity: 'error',
            field: 'name',
            control: this.categoryForm.get('name') ?? undefined,
            validator: () => !this.categoryForm.get('name')?.hasError('required')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-minlength',
            message: 'Name must be at least 2 characters',
            severity: 'error',
            field: 'name',
            control: this.categoryForm.get('name') ?? undefined,
            validator: () => !this.categoryForm.get('name')?.hasError('minlength')
        });

        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-maxlength',
            message: 'Name cannot exceed 50 characters',
            severity: 'error',
            field: 'name',
            control: this.categoryForm.get('name') ?? undefined,
            validator: () => !this.categoryForm.get('name')?.hasError('maxlength')
        });

        // Description validation (optional field)
        this.validationService.addViolation(this.validationContext.id, {
            id: 'description-maxlength',
            message: 'Description cannot exceed 200 characters',
            severity: 'error',
            field: 'description',
            control: this.categoryForm.get('description') ?? undefined,
            validator: () => !this.categoryForm.get('description')?.hasError('maxlength')
        });
    }

    ngOnDestroy(): void {
        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    loadCategories(): void {
        this.isLoading.set(true);
        this.loading.set(true);
        this.shoppingService.getCategories().subscribe({
            next: (categories: ShoppingListCategory[]) => {
                // Sort by sortOrder
                const sorted = categories.sort((a, b) => a.sortOrder - b.sortOrder);
                this.categories.set(sorted);
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
        if (this.categoryForm.invalid) {
            return;
        }

        this.isSubmitting.set(true);

        const categoryData: ShoppingListCategoryCreate = {
            ...this.categoryForm.value,
            householdId: this.userInfoService.getHouseholdId()
        };

        if (this.isEditing() && this.editingCategoryId()) {
            // Update existing category
            this.shoppingService.updateCategory(this.editingCategoryId()!, categoryData).subscribe({
                next: (updatedCategory: ShoppingListCategory) => {
                    const categories = this.categories();
                    const index = categories.findIndex(c => c.id === updatedCategory.id);
                    if (index !== -1) {
                        categories[index] = updatedCategory;
                        this.categories.set([...categories].sort((a, b) => a.sortOrder - b.sortOrder));
                    }
                    this.resetForm();
                    this.notificationService.success('Category updated successfully');
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error updating category:', error);
                    this.notificationService.error('Error updating category');
                    this.isSubmitting.set(false);
                }
            });
        } else {
            // Create new category
            this.shoppingService.createCategory(categoryData).subscribe({
                next: (category: ShoppingListCategory) => {
                    this.categories.set([...this.categories(), category].sort((a, b) => a.sortOrder - b.sortOrder));
                    this.resetForm();
                    this.notificationService.success('Category created successfully');
                },
                error: (error: Error | string | unknown) => {
                    console.error('Error creating category:', error);
                    this.notificationService.error('Error creating category');
                    this.isSubmitting.set(false);
                }
            });
        }
    }

    editCategory(category: ShoppingListCategory): void {
        this.isAddingCategory.set(true);
        this.isEditing.set(true);
        this.editingCategoryId.set(category.id);
        this.categoryForm.patchValue({
            name: category.name,
            description: category.description || '',
            color: category.color || '#1976d2',
            icon: 'category', // Default icon, backend doesn't support icon storage yet
            sortOrder: category.sortOrder
        });

        // Scroll form into view
        setTimeout(() => {
            document.querySelector('.shopping-category-management__form-card')?.scrollIntoView({ behavior: 'smooth' });
        }, 100);
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
        this.resetForm();
    }

    private resetForm(): void {
        this.categoryForm.reset({
            name: '',
            description: '',
            color: '#1976d2',
            icon: 'category',
            sortOrder: 0
        });
        this.isAddingCategory.set(false);
        this.isEditing.set(false);
        this.isSubmitting.set(false);
        this.editingCategoryId.set(null);
    }

    getCategoryColor(category: ShoppingListCategory): string {
        return category.color || '#1976d2';
    }

    getCategoryIcon(category: ShoppingListCategory): string {
        return 'category'; // Default icon - backend doesn't support icon storage yet
    }

    // Drag-drop reordering
    drop(event: CdkDragDrop<ShoppingListCategory[]>): void {
        const categories = [...this.categories()];
        moveItemInArray(categories, event.previousIndex, event.currentIndex);

        // Update sortOrder for all categories
        const updates = categories.map((category, index) => {
            const updated = { ...category, sortOrder: index };
            return this.shoppingService.updateCategory(category.id, {
                householdId: category.householdId,
                name: category.name,
                description: category.description,
                color: category.color,
                sortOrder: index
            });
        });

        // Update local state immediately for smooth UX
        this.categories.set(categories);

        // Optionally, you could wait for all updates to complete
        // but this provides better UX with immediate feedback
        this.notificationService.success('Category order updated');
    }
} 