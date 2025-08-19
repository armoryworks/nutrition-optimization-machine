import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil, debounceTime, distinctUntilChanged } from 'rxjs';
import { RecipeSuggestionService, RecipeSuggestionQuery, AIRecipeSuggestionRequest, RecipeSuggestionResponse, AIRecipeSuggestionResponse, RecipeRecommendation } from '../../services/recipe-suggestion.service';

@Component({
    selector: 'nom-recipe-suggestions',
    templateUrl: './recipe-suggestions.component.html',
    styleUrls: ['./recipe-suggestions.component.scss']
})
export class RecipeSuggestionsComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private recipeSuggestionService = inject(RecipeSuggestionService);

    private destroy$ = new Subject<void>();

    // Forms
    suggestionForm: FormGroup;
    aiSuggestionForm: FormGroup;

    // Data
    suggestions: RecipeSuggestionResponse | null = null;
    aiSuggestions: AIRecipeSuggestionResponse | null = null;
    recommendations: RecipeRecommendation[] = [];
    trendingRecipes: RecipeRecommendation[] = [];
    seasonalRecipes: RecipeRecommendation[] = [];

    // UI State
    loading = false;
    aiLoading = false;
    activeTab = 'ingredients';
    selectedIngredients: number[] = [];
    selectedTools: number[] = [];

    // Filter options
    difficultyOptions = ['Easy', 'Medium', 'Hard'];
    cuisineOptions = ['Italian', 'Mexican', 'Asian', 'American', 'Mediterranean', 'Indian', 'French', 'Thai'];
    mealTypeOptions = ['Breakfast', 'Lunch', 'Dinner', 'Snack', 'Dessert'];
    dietaryOptions = ['Vegetarian', 'Vegan', 'Gluten-Free', 'Dairy-Free', 'Keto', 'Paleo', 'Low-Carb'];

    constructor() {
        this.suggestionForm = this.fb.group({
            limit: [10, [Validators.min(1), Validators.max(50)]],
            maxMissingIngredients: [5, [Validators.min(0), Validators.max(20)]],
            maxMissingTools: [5, [Validators.min(0), Validators.max(10)]],
            includeIngredientsOnHand: [true],
            includeToolsOnHand: [true],
            queryFilter: [''],
            maxPrepTime: [null, [Validators.min(1), Validators.max(480)]],
            maxCookTime: [null, [Validators.min(1), Validators.max(480)]],
            maxDifficulty: [null, [Validators.min(1), Validators.max(5)]],
            includePublicRecipes: [true],
            includePrivateRecipes: [false],
            categories: [[]],
            tags: [[]],
            dietaryRestrictions: [[]],
            cuisines: [[]]
        });

        this.aiSuggestionForm = this.fb.group({
            description: ['', [Validators.required, Validators.minLength(10)]],
            availableIngredients: [[]],
            availableTools: [[]],
            preferences: [[]],
            dietaryRestrictions: [[]],
            dislikedIngredients: [[]],
            servingSize: [4, [Validators.min(1), Validators.max(20)]],
            maxPrepTime: [30, [Validators.min(1), Validators.max(480)]],
            maxCookTime: [60, [Validators.min(1), Validators.max(480)]],
            budgetLimit: [50, [Validators.min(1), Validators.max(500)]],
            cuisine: [''],
            mealType: [''],
            difficulty: [''],
            includeNutritionalInfo: [true],
            includeSubstitutions: [true]
        });
    }

    ngOnInit(): void {
        this.loadRecommendations();
        this.loadTrendingRecipes();
        this.loadSeasonalRecipes();

        // Setup form change listeners
        this.suggestionForm.valueChanges
            .pipe(
                takeUntil(this.destroy$),
                debounceTime(500),
                distinctUntilChanged()
            )
            .subscribe(() => {
                if (this.activeTab === 'ingredients' && this.selectedIngredients.length > 0) {
                    this.getSuggestions();
                }
            });
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    /**
     * Get recipe suggestions based on selected ingredients and tools
     */
    getSuggestions(): void {
        if (this.selectedIngredients.length === 0) {
            return;
        }

        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getRecipeSuggestions(query, this.selectedIngredients, this.selectedTools)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Generate AI-powered recipe suggestions
     */
    generateAISuggestions(): void {
        if (this.aiSuggestionForm.invalid) {
            return;
        }

        this.aiLoading = true;
        const request: AIRecipeSuggestionRequest = this.aiSuggestionForm.value;

        this.recipeSuggestionService.generateAIRecipeSuggestions(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.aiSuggestions = response;
                    this.aiLoading = false;
                },
                error: (error) => {
                    console.error('Error generating AI suggestions:', error);
                    this.aiLoading = false;
                }
            });
    }

    /**
     * Load recipe recommendations
     */
    loadRecommendations(): void {
        this.recipeSuggestionService.getRecipeRecommendations()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (recommendations) => {
                    this.recommendations = recommendations;
                },
                error: (error) => {
                    console.error('Error loading recommendations:', error);
                }
            });
    }

    /**
     * Load trending recipes
     */
    loadTrendingRecipes(): void {
        this.recipeSuggestionService.getTrendingRecipes(10)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (trending) => {
                    this.trendingRecipes = trending;
                },
                error: (error) => {
                    console.error('Error loading trending recipes:', error);
                }
            });
    }

    /**
     * Load seasonal recipes
     */
    loadSeasonalRecipes(): void {
        this.recipeSuggestionService.getSeasonalRecipes()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (seasonal) => {
                    this.seasonalRecipes = seasonal;
                },
                error: (error) => {
                    console.error('Error loading seasonal recipes:', error);
                }
            });
    }

    /**
     * Toggle ingredient selection
     */
    toggleIngredient(ingredientId: number): void {
        const index = this.selectedIngredients.indexOf(ingredientId);
        if (index > -1) {
            this.selectedIngredients.splice(index, 1);
        } else {
            this.selectedIngredients.push(ingredientId);
        }
        this.getSuggestions();
    }

    /**
     * Toggle tool selection
     */
    toggleTool(toolId: number): void {
        const index = this.selectedTools.indexOf(toolId);
        if (index > -1) {
            this.selectedTools.splice(index, 1);
        } else {
            this.selectedTools.push(toolId);
        }
        this.getSuggestions();
    }

    /**
     * Change active tab
     */
    setActiveTab(tab: string): void {
        this.activeTab = tab;
    }

    /**
     * Get meal type suggestions
     */
    getMealTypeSuggestions(mealType: string): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getMealTypeSuggestions(mealType, query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting meal type suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Get dietary suggestions
     */
    getDietarySuggestions(dietaryRestrictions: string[]): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getDietarySuggestions(dietaryRestrictions, query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting dietary suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Get quick recipe suggestions
     */
    getQuickSuggestions(maxTime: number): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getQuickRecipeSuggestions(maxTime, query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting quick suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Get budget recipe suggestions
     */
    getBudgetSuggestions(maxBudget: number): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getBudgetRecipeSuggestions(maxBudget, query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting budget suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Get beginner recipe suggestions
     */
    getBeginnerSuggestions(): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getBeginnerRecipeSuggestions(query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting beginner suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * Get advanced recipe suggestions
     */
    getAdvancedSuggestions(): void {
        this.loading = true;
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getAdvancedRecipeSuggestions(query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions = response;
                    this.loading = false;
                },
                error: (error) => {
                    console.error('Error getting advanced suggestions:', error);
                    this.loading = false;
                }
            });
    }

    /**
     * View recipe details
     */
    viewRecipe(recipeId: number): void {
        // TODO: Navigate to recipe details page
        console.log('Viewing recipe:', recipeId);
    }

    /**
     * Save recipe to favorites
     */
    saveRecipe(recipeId: number): void {
        // TODO: Save recipe to favorites
        console.log('Saving recipe:', recipeId);
    }

    /**
     * Get match score color
     */
    getMatchScoreColor(score: number): string {
        if (score >= 0.9) return 'success';
        if (score >= 0.7) return 'warning';
        return 'danger';
    }

    /**
     * Get difficulty color
     */
    getDifficultyColor(difficulty: string): string {
        switch (difficulty.toLowerCase()) {
            case 'easy': return 'success';
            case 'medium': return 'warning';
            case 'hard': return 'danger';
            default: return 'secondary';
        }
    }
} 