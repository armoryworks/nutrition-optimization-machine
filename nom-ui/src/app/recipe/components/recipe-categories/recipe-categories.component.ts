import { Component, OnInit, inject, output, signal } from '@angular/core';
import { NgFor, NgIf } from '@angular/common';
import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators, FormsModule } from '@angular/forms';
import { MatChipsModule } from '@angular/material/chips';

import { AmwButtonComponent, AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwCardComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { RecipeService } from '../../services/recipe.service';
import { RecipeCategoryModel, RecipeCategoryResponseModel } from '../../models/i-recipe-category.model';


@Component({
    selector: 'nom-recipe-categories',
    standalone: true,
    imports: [
        NgFor,
        NgIf,
        ReactiveFormsModule,
        FormsModule,
        MatChipsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwSelectComponent,
        AmwTextareaComponent,
        AmwCardComponent,
        AmwIconButtonComponent,
        AmwTooltipDirective,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
    ],
    templateUrl: './recipe-categories.component.html',
    styleUrls: ['./recipe-categories.component.scss']
})
export class RecipeCategoriesComponent implements OnInit {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);

    categoriesChange = output<RecipeCategoryResponseModel[]>();

    categories = signal<RecipeCategoryResponseModel[]>([]);
    allCategories = signal<RecipeCategoryResponseModel[]>([]);
    filteredCategories = signal<RecipeCategoryResponseModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    categoryForm: FormGroup;
    isAddingCategory = signal(false);
    searchTerm = signal('');



    constructor() {
        this.categoryForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
            icon: ['', [Validators.required]],
            color: ['#1976d2', [Validators.required]],
            parentCategoryId: [null],
            description: ['', [Validators.maxLength(200)]]
        });
    }

    ngOnInit(): void {
        this.loadAllCategories();
    }

    loadAllCategories(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.recipeService.getAllCategories().subscribe({
            next: (categories) => {
                this.categories.set(categories);
                this.allCategories.set(categories);
                this.filteredCategories.set(categories);
                this.isLoading.set(false);
            },
            error: (error) => {
                this.error.set('Failed to load categories');
                this.isLoading.set(false);
                console.error('Error loading categories:', error);
            },
        });
    }

    onRefresh(): void {
        this.loadAllCategories();
    }

    onRetry(): void {
        this.loadAllCategories();
    }

    addCategory(category: RecipeCategoryModel): void {
        if (!this.categories().find(c => c.id === category.id)) {
            const updatedCategories = [...this.categories(), category];
            this.categories.set(updatedCategories);
            this.categoriesChange.emit(updatedCategories);
        }
    }

    removeCategory(category: RecipeCategoryModel): void {
        const updatedCategories = this.categories().filter(c => c.id !== category.id);
        this.categories.set(updatedCategories);
        this.categoriesChange.emit(updatedCategories);
    }

    createNewCategory(): void {
        if (this.categoryForm.valid) {
            const newCategory: Partial<RecipeCategoryModel> = {
                name: this.categoryForm.get('name')?.value,
                description: this.categoryForm.get('description')?.value,
                icon: this.categoryForm.get('icon')?.value,
                color: this.categoryForm.get('color')?.value,
                parentCategoryId: this.categoryForm.get('parentCategoryId')?.value
            };

            this.recipeService.createCategory(newCategory).subscribe({
                next: (createdCategory) => {
                    this.allCategories.set([...this.allCategories(), createdCategory]);
                    this.addCategory(createdCategory);
                    this.categoryForm.reset({
                        name: '',
                        description: '',
                        icon: 'restaurant',
                        color: '#1976d2',
                        parentCategoryId: null
                    });
                },
                error: (error) => {
                    console.error('Error creating category:', error);
                },
            });
        }
    }

    deleteCategory(category: RecipeCategoryModel): void {
        this.recipeService.deleteCategory(category.id!).subscribe({
            next: () => {
                this.allCategories.set(this.allCategories().filter(c => c.id !== category.id));
                this.removeCategory(category);
            },
            error: (error) => {
                console.error('Error deleting category:', error);
            },
        });
    }

    filterCategories(): void {
        if (!this.searchTerm().trim()) {
            this.filteredCategories.set(this.allCategories());
        } else {
            this.filteredCategories.set(this.allCategories().filter(category =>
                category.name.toLowerCase().includes(this.searchTerm().toLowerCase()) ||
                category.description?.toLowerCase().includes(this.searchTerm().toLowerCase())
            ));
        }
    }

    getCategoryStyle(category: RecipeCategoryModel): Record<string, string> {
        return {
            'background-color': category.color || '#1976d2',
            'color': this.getContrastColor(category.color || '#1976d2')
        };
    }

    private getContrastColor(hexColor: string): string {
        // Convert hex to RGB
        const r = parseInt(hexColor.slice(1, 3), 16);
        const g = parseInt(hexColor.slice(3, 5), 16);
        const b = parseInt(hexColor.slice(5, 7), 16);

        // Calculate luminance
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;

        return luminance > 0.5 ? '#000000' : '#ffffff';
    }

    isCategorySelected(category: RecipeCategoryModel): boolean {
        return this.categories().some(c => c.id === category.id);
    }

    getParentCategoryName(category: RecipeCategoryModel): string {
        if (category.parentCategoryId) {
            const parent = this.allCategories().find(c => c.id === category.parentCategoryId);
            return parent ? parent.name : 'Unknown';
        }
        return 'None';
    }

    getIconOptions(): string[] {
        return [
            'restaurant', 'local_dining', 'fastfood', 'cake', 'local_pizza',
            'local_cafe', 'local_bar', 'wine_bar', 'bakery_dining', 'ramen_dining',
            'dinner_dining', 'lunch_dining', 'breakfast_dining', 'brunch_dining',
            'food_bank', 'takeout_dining', 'delivery_dining', 'dining'
        ];
    }
} 