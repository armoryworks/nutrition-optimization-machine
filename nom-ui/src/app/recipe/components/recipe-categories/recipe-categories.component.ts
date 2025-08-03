import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatSelectModule } from '@angular/material/select';
import { ViewEncapsulation } from '@angular/core';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { RecipeCategoriesService } from '../../services/recipe-categories.service';
import { RecipeCategoryModel } from '../../models/i-recipe-category.model';

@Component({
    selector: 'nom-recipe-categories',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatIconModule,
        MatButtonModule,
        MatChipsModule,
        MatTooltipModule,
        MatDialogModule,
        MatAutocompleteModule,
        MatSelectModule,
        BasePageComponent,
    ],
    templateUrl: './recipe-categories.component.html',
    styleUrls: ['./recipe-categories.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class RecipeCategoriesComponent implements OnInit {
    @Input() recipeId?: number;
    @Input() categories: RecipeCategoryModel[] = [];
    @Output() categoriesChange = new EventEmitter<RecipeCategoryModel[]>();

    allCategories: RecipeCategoryModel[] = [];
    loading = false;
    error = '';
    searchTerm = '';
    filteredCategories: RecipeCategoryModel[] = [];

    categoryForm: FormGroup;

    pageConfig: BasePageConfig = {
        title: 'Recipe Categories',
        subtitle: 'Organize recipes with categories',
        showRefreshButton: true,
        refreshButtonText: 'Refresh',
        maxWidth: '800px',
    };

    constructor(
        private recipeCategoriesService: RecipeCategoriesService,
        private dialog: MatDialog,
        private fb: NonNullableFormBuilder
    ) {
        this.categoryForm = this.fb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
            description: ['', [Validators.maxLength(200)]],
            icon: ['restaurant', [Validators.required]],
            color: ['#1976d2', [Validators.required]],
            parentCategoryId: [null]
        });
    }

    ngOnInit(): void {
        this.loadAllCategories();
    }

    loadAllCategories(): void {
        this.loading = true;
        this.error = '';

        this.recipeCategoriesService.getAllCategories().subscribe({
            next: (categories) => {
                this.allCategories = categories;
                this.filteredCategories = categories;
                this.loading = false;
            },
            error: (error) => {
                this.error = 'Failed to load categories';
                this.loading = false;
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
        if (!this.categories.find(c => c.id === category.id)) {
            this.categories = [...this.categories, category];
            this.categoriesChange.emit(this.categories);
        }
    }

    removeCategory(category: RecipeCategoryModel): void {
        this.categories = this.categories.filter(c => c.id !== category.id);
        this.categoriesChange.emit(this.categories);
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

            this.recipeCategoriesService.createCategory(newCategory).subscribe({
                next: (createdCategory) => {
                    this.allCategories = [...this.allCategories, createdCategory];
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
        this.recipeCategoriesService.deleteCategory(category.id!).subscribe({
            next: () => {
                this.allCategories = this.allCategories.filter(c => c.id !== category.id);
                this.removeCategory(category);
            },
            error: (error) => {
                console.error('Error deleting category:', error);
            },
        });
    }

    filterCategories(): void {
        if (!this.searchTerm.trim()) {
            this.filteredCategories = this.allCategories;
        } else {
            this.filteredCategories = this.allCategories.filter(category =>
                category.name.toLowerCase().includes(this.searchTerm.toLowerCase()) ||
                category.description?.toLowerCase().includes(this.searchTerm.toLowerCase())
            );
        }
    }

    getCategoryStyle(category: RecipeCategoryModel): { [key: string]: string } {
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
        return this.categories.some(c => c.id === category.id);
    }

    getParentCategoryName(category: RecipeCategoryModel): string {
        if (category.parentCategoryId) {
            const parent = this.allCategories.find(c => c.id === category.parentCategoryId);
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