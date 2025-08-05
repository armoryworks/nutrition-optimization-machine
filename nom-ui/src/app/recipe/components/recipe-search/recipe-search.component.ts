import { Component, OnInit, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatSliderModule } from '@angular/material/slider';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatCardModule } from '@angular/material/card';
import { Observable, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { RecipeSearchService } from '../../services/recipe-search.service';
import { RecipeSearchModel, RecipeSearchResponse, RecipeSearchResult } from '../../models/recipe-search.model';
import { BaseListComponent, BaseListConfig } from '../../../common/components/base-list/base-list.component';

@Component({
    selector: 'nom-recipe-search',
    standalone: true,
    imports: [
        CommonModule,
        RouterModule,
        FormsModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        MatButtonModule,
        MatIconModule,
        MatChipsModule,
        MatPaginatorModule,
        MatProgressSpinnerModule,
        MatSnackBarModule,
        MatExpansionModule,
        MatSliderModule,
        MatCheckboxModule,
        MatAutocompleteModule,
        MatCardModule,
        BaseListComponent
    ],
    schemas: [CUSTOM_ELEMENTS_SCHEMA],
    templateUrl: './recipe-search.component.html',
    styleUrls: ['./recipe-search.component.scss']
})
export class RecipeSearchComponent implements OnInit {
    searchForm: FormGroup;
    searchResults: RecipeSearchResult[] = [];
    loading = false;
    totalCount = 0;
    currentPage = 1;
    pageSize = 20;
    totalPages = 0;
    hasNextPage = false;
    hasPreviousPage = false;
    error: string | null = null;

    listConfig: BaseListConfig = {
        title: 'Recipe Search',
        subtitle: 'Discover recipes with advanced filtering',
        showSearch: true,
        maxWidth: '1200px'
    };

    // Filter options
    sortOptions = [
        { value: 'name', label: 'Name' },
        { value: 'rating', label: 'Rating' },
        { value: 'date', label: 'Date Created' },
        { value: 'prepTime', label: 'Prep Time' },
        { value: 'cookTime', label: 'Cook Time' }
    ];

    sortDirections = [
        { value: 'asc', label: 'Ascending' },
        { value: 'desc', label: 'Descending' }
    ];

    // Autocomplete suggestions
    searchSuggestions$: Observable<string[]> = new Observable();

    constructor(
        private searchService: RecipeSearchService,
        private fb: FormBuilder,
        private snackBar: MatSnackBar
    ) {
        this.searchForm = this.fb.group({
            query: [''],
            minRating: [null],
            maxPrepTime: [null],
            maxCookTime: [null],
            maxTotalTime: [null],
            isPublic: [true],
            isApproved: [true],
            sortBy: ['date'],
            sortDirection: ['desc'],
            includeIngredients: [true],
            includeSteps: [false],
            includeNutrition: [false]
        });

        // Setup autocomplete
        this.searchSuggestions$ = this.searchForm.get('query')!.valueChanges.pipe(
            debounceTime(300),
            distinctUntilChanged(),
            switchMap(query => {
                if (query && query.trim().length > 2) {
                    return this.searchService.getSearchSuggestions(query.trim());
                }
                return new Observable<string[]>();
            })
        );
    }

    ngOnInit(): void {
        // Load initial data
        this.loadPopularRecipes();
    }

    performSearch(): void {
        this.loading = true;
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

        this.searchService.searchRecipes(searchParams).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results;
                this.totalCount = response.totalCount;
                this.totalPages = Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = this.currentPage < this.totalPages;
                this.hasPreviousPage = this.currentPage > 1;
                this.loading = false;
            },
            error: (error) => {
                console.error('Search error:', error);
                this.error = 'Failed to search recipes. Please try again.';
                this.loading = false;
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
        this.loading = true;
        this.error = null;

        this.searchService.getPopularRecipes().subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results;
                this.totalCount = response.totalCount;
                this.totalPages = Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = this.currentPage < this.totalPages;
                this.hasPreviousPage = this.currentPage > 1;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error loading popular recipes:', error);
                this.error = 'Failed to load popular recipes.';
                this.loading = false;
            }
        });
    }

    loadRecentRecipes(): void {
        this.loading = true;
        this.error = null;

        this.searchService.getRecentRecipes().subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.results;
                this.totalCount = response.totalCount;
                this.totalPages = Math.ceil(this.totalCount / this.pageSize);
                this.hasNextPage = this.currentPage < this.totalPages;
                this.hasPreviousPage = this.currentPage > 1;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error loading recent recipes:', error);
                this.error = 'Failed to load recent recipes.';
                this.loading = false;
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