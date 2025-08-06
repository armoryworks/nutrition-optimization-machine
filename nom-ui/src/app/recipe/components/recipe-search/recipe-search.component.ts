import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, NonNullableFormBuilder, FormGroup } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Observable, of } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';

import { RecipeService } from '../../services/recipe.service';
import { RecipeSearchModel, RecipeSearchResponse, RecipeSearchResult } from '../../models/recipe-search.model';
import { ConfirmDialogComponent } from '../../../common/components/confirm-dialog/confirm-dialog.component';
import { BaseListComponent, BaseListConfig } from '../../../common/components/base-list/base-list.component';

@Component({
    selector: 'nom-recipe-search',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        RouterModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatChipsModule,
        MatDividerModule,
        MatSelectModule,
        MatCheckboxModule,
        MatDialogModule,
        MatListModule,
        MatMenuModule,
        MatAutocompleteModule,
        MatExpansionModule,
        MatPaginatorModule,
        BaseListComponent,
    ],
    templateUrl: './recipe-search.component.html',
    styleUrls: ['./recipe-search.component.scss']
})
export class RecipeSearchComponent implements OnInit {
    searchResults: RecipeSearchResult[] = [];
    isLoading = false;
    error: string | null = null;
    searchForm: FormGroup;
    isSearching = false;

    // Pagination properties
    currentPage = 1;
    pageSize = 10;
    totalCount = 0;
    totalPages = 0;
    hasNextPage = false;
    hasPreviousPage = false;

    // Search suggestions
    searchSuggestions$: Observable<string[]> = of([]);

    // Configuration
    listConfig: BaseListConfig = {
        title: 'Recipe Search',
        subtitle: 'Find recipes by name, ingredients, or criteria',
        showSearch: true,
        showCreateButton: false,
        showRefreshButton: true,
        refreshButtonText: 'Clear Search'
    };

    // Options for dropdowns
    sortOptions = [
        { value: 'relevance', label: 'Relevance' },
        { value: 'rating', label: 'Rating' },
        { value: 'name', label: 'Name' },
        { value: 'prepTime', label: 'Prep Time' },
        { value: 'cookTime', label: 'Cook Time' }
    ];

    sortDirections = [
        { value: 'asc', label: 'Ascending' },
        { value: 'desc', label: 'Descending' }
    ];

    constructor(
        private recipeService: RecipeService,
        private router: Router,
        private nonNullableFb: NonNullableFormBuilder,
        private snackBar: MatSnackBar,
        private dialog: MatDialog
    ) {
        this.searchForm = this.nonNullableFb.group({
            query: [''],
            minRating: [null],
            maxPrepTime: [null],
            maxCookTime: [null],
            sortBy: ['relevance'],
            sortDirection: ['desc'],
            isPublic: [false],
            isApproved: [false],
            includeIngredients: [false]
        });
    }

    ngOnInit(): void {
        // Load initial data
        this.loadPopularRecipes();
    }

    performSearch(): void {
        this.isLoading = true;
        this.error = null;

        const searchParams: RecipeSearchModel = {
            query: this.searchForm.get('query')?.value || '',
            minRating: this.searchForm.get('minRating')?.value,
            maxPrepTime: this.searchForm.get('maxPrepTime')?.value,
            maxCookTime: this.searchForm.get('maxCookTime')?.value,
            maxTotalTime: this.searchForm.get('maxTotalTime')?.value,
            isPublic: this.searchForm.get('isPublic')?.value,
            isApproved: this.searchForm.get('isApproved')?.value,
            sortBy: this.searchForm.get('sortBy')?.value,
            sortDirection: this.searchForm.get('sortDirection')?.value,
            includeIngredients: this.searchForm.get('includeIngredients')?.value,
            includeSteps: this.searchForm.get('includeSteps')?.value,
            includeNutrition: this.searchForm.get('includeNutrition')?.value,
            page: this.currentPage,
            pageSize: this.pageSize
        };

        this.recipeService.searchRecipesAdvanced(searchParams).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results || response.recipes || [];
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages || Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.isLoading = false;
            },
            error: (error: any) => {
                console.error('Search error:', error);
                this.error = 'Failed to search recipes. Please try again.';
                this.isLoading = false;
            }
        });
    }

    onPageChange(event: PageEvent): void {
        this.currentPage = event.pageIndex + 1;
        this.pageSize = event.pageSize;
        this.performSearch();
    }

    onSuggestionSelected(suggestion: string): void {
        this.searchForm.patchValue({ query: suggestion });
        this.performSearch();
    }

    clearFilters(): void {
        this.searchForm.patchValue({
            query: '',
            minRating: null,
            maxPrepTime: null,
            maxCookTime: null,
            maxTotalTime: null,
            isPublic: true,
            isApproved: true,
            sortBy: 'date',
            sortDirection: 'desc',
            includeIngredients: true,
            includeSteps: false,
            includeNutrition: false
        });
    }

    loadPopularRecipes(): void {
        this.isLoading = true;
        this.error = null;

        this.recipeService.getPopularRecipes(this.currentPage, this.pageSize).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results || response.recipes || [];
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages || Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.isLoading = false;
            },
            error: (error: any) => {
                console.error('Error loading popular recipes:', error);
                this.error = 'Failed to load popular recipes.';
                this.isLoading = false;
            }
        });
    }

    loadRecentRecipes(): void {
        this.isLoading = true;
        this.error = null;

        this.recipeService.getRecentRecipes(this.currentPage, this.pageSize).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results || response.recipes || [];
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages || Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.isLoading = false;
            },
            error: (error: any) => {
                console.error('Error loading recent recipes:', error);
                this.error = 'Failed to load recent recipes.';
                this.isLoading = false;
            }
        });
    }

    formatTime(minutes: number): string {
        if (minutes < 60) {
            return `${minutes}m`;
        }
        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes > 0 ? `${hours}h ${remainingMinutes}m` : `${hours}h`;
    }

    getRatingStars(rating: number): string[] {
        const stars = [];
        const fullStars = Math.floor(rating);
        const hasHalfStar = rating % 1 !== 0;

        for (let i = 0; i < fullStars; i++) {
            stars.push('star');
        }
        if (hasHalfStar) {
            stars.push('star_half');
        }
        const emptyStars = 5 - stars.length;
        for (let i = 0; i < emptyStars; i++) {
            stars.push('star_border');
        }
        return stars;
    }


} 