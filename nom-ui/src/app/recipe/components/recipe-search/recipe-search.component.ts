import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { RecipeService } from '../../services/recipe.service';
import { AuthManagerService } from '../../../utilities/services/auth-manager.service';

interface SearchResult {
    id: number;
    name: string;
    type: 'Recipe' | 'Ingredient';
    curationStatus: string;
    isCurated: boolean;
    authorName?: string;
    description?: string;
}

@Component({
    selector: 'app-recipe-search',
    standalone: true,
    imports: [
        CommonModule,
        RouterLink,
        FormsModule,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatChipsModule,
        MatFormFieldModule,
        MatInputModule
    ],
    templateUrl: './recipe-search.component.html',
    styleUrls: ['./recipe-search.component.scss']
})
export class RecipeSearchComponent implements OnInit {
    searchQuery: string = '';
    searchResults$!: Observable<SearchResult[]>;
    isLoading: boolean = false;
    error: string | null = null;
    curatedCount: number = 0;
    nonCuratedCount: number = 0;
    isLoggedIn: boolean = false;

    constructor(
        private recipeService: RecipeService,
        private route: ActivatedRoute,
        private authManagerService: AuthManagerService
    ) { }

    ngOnInit(): void {
        // Get search query from route params
        this.route.queryParams.subscribe(params => {
            const query = params['q'];

            if (query) {
                this.searchQuery = query;
            }
        });

        // Get login status directly from AuthManagerService
        this.authManagerService.userLogin.subscribe(isLoggedIn => {
            this.isLoggedIn = isLoggedIn;
            console.log('Recipe search - Login status:', isLoggedIn);

            // Perform search if we have a query
            if (this.searchQuery) {
                this.performSearch();
            }
        });
    }

    performSearch(): void {
        if (!this.searchQuery.trim()) {
            this.searchResults$ = of([]);
            return;
        }

        this.isLoading = true;
        this.error = null;

        // This would be replaced with actual API call
        // For now, we'll simulate search results
        this.searchResults$ = this.recipeService.searchRecipes(this.searchQuery).pipe(
            map(results => {
                // Filter results based on login status
                let filteredResults = results;

                if (!this.isLoggedIn) {
                    // Non-logged-in users only see curated items
                    filteredResults = results.filter(r => r.isCurated);
                }

                this.curatedCount = filteredResults.filter(r => r.isCurated).length;
                this.nonCuratedCount = filteredResults.filter(r => !r.isCurated).length;
                return filteredResults;
            }),
            catchError(err => {
                console.error('Search error:', err);
                this.error = 'Failed to perform search. Please try again.';
                this.isLoading = false;
                return of([]);
            })
        );

        this.isLoading = false;
    }

    onSearchInput(event: any): void {
        const query = event.target.value;
        if (query.length >= 2) {
            this.searchQuery = query;
            this.performSearch();
        } else if (query.length === 0) {
            this.searchResults$ = of([]);
            this.curatedCount = 0;
            this.nonCuratedCount = 0;
        }
    }

    getStatusColor(status: string): string {
        switch (status.toLowerCase()) {
            case 'curated':
                return 'primary';
            case 'noncurated':
                return 'warn';
            case 'draft':
                return 'accent';
            default:
                return 'basic';
        }
    }

    getTypeIcon(type: string): string {
        return type === 'Recipe' ? 'restaurant' : 'eco';
    }
} 