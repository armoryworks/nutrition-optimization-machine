import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
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
import { Observable, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { RecipeSearchService } from '../../services/recipe-search.service';
import { RecipeSearchModel, RecipeSearchResponse, RecipeSearchResult } from '../../models/recipe-search.model';

@Component({
    selector: 'app-recipe-search',
    standalone: true,
    imports: [
        CommonModule,
        RouterModule,
        FormsModule,
        ReactiveFormsModule,
        MatCardModule,
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
        MatAutocompleteModule
    ],
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
                if (query && query.length >= 2) {
                    return this.searchService.getSearchSuggestions(query);
                }
                return new Observable<string[]>();
            })
        );
    }

    ngOnInit(): void {
        this.performSearch();
    }

    performSearch(): void {
        this.loading = true;
        const searchModel: RecipeSearchModel = {
            ...this.searchForm.value,
            page: this.currentPage,
            pageSize: this.pageSize
        };

        this.searchService.searchRecipes(searchModel).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.recipes;
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages;
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error searching recipes:', error);
                this.snackBar.open('Error searching recipes', 'Close', { duration: 3000 });
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
        this.searchForm.reset({
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
        this.currentPage = 1;
        this.performSearch();
    }

    loadPopularRecipes(): void {
        this.loading = true;
        this.searchService.getPopularRecipes(10).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.recipes;
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages;
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error loading popular recipes:', error);
                this.snackBar.open('Error loading popular recipes', 'Close', { duration: 3000 });
                this.loading = false;
            }
        });
    }

    loadRecentRecipes(): void {
        this.loading = true;
        this.searchService.getRecentRecipes(10).subscribe({
            next: (response: RecipeSearchResponse) => {
                this.searchResults = response.recipes;
                this.totalCount = response.totalCount;
                this.totalPages = response.totalPages;
                this.hasNextPage = response.hasNextPage;
                this.hasPreviousPage = response.hasPreviousPage;
                this.loading = false;
            },
            error: (error) => {
                console.error('Error loading recent recipes:', error);
                this.snackBar.open('Error loading recent recipes', 'Close', { duration: 3000 });
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
        const hasHalfStar = rating % 1 >= 0.5;

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