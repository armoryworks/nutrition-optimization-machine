import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, takeUntil, debounceTime, distinctUntilChanged } from 'rxjs';
import { RecipeSuggestionService } from '../../services/recipe-suggestion.service';
import { RecipeService } from '../../services/recipe.service';
import { RecipeReferenceService } from '../../services/recipe-reference.service';
import { ReferenceDataService } from '../../../common/services/reference-data.service';
import { ReferenceItemModel } from '../../../common/models/reference-item.model';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

@Component({
    selector: 'app-recipe-suggestions',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatTabsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatSelectModule,
        MatCheckboxModule,
        MatChipsModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatCardModule,
        MatTooltipModule
    ],
    templateUrl: './recipe-suggestions.component.html',
    styleUrls: ['./recipe-suggestions.component.scss']
})
export class RecipeSuggestionsComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private recipeSuggestionService = inject(RecipeSuggestionService);
    private recipeService = inject(RecipeService);
    private recipeReferenceService = inject(RecipeReferenceService);
    private referenceDataService = inject(ReferenceDataService);

    suggestionForm: FormGroup;
    aiSuggestionForm: FormGroup;
    suggestions = signal<any[]>([]);
    trendingRecipes = signal<any[]>([]);
    seasonalRecipes = signal<any[]>([]);
    loading = signal(false);
    aiLoading = signal(false);
    activeTab = signal('ingredients');
    selectedIngredients = signal<number[]>([]);
    selectedTools = signal<number[]>([]);

    // Reference data loaded dynamically
    difficulties = signal<ReferenceItemModel[]>([]);
    cuisines = signal<any[]>([]);
    mealTypes = signal<any[]>([]);
    dietaryOptions = signal<any[]>([]);

    // Make constants available in template
    readonly REFERENCE_IDS = REFERENCE_IDS;

    // Computed signals
    isLoadingSuggestions = computed(() => this.loading());
    isLoadingAISuggestions = computed(() => this.aiLoading());
    hasSuggestions = computed(() => this.suggestions().length > 0);
    hasTrendingRecipes = computed(() => this.trendingRecipes().length > 0);
    hasSeasonalRecipes = computed(() => this.seasonalRecipes().length > 0);
    hasSelectedIngredients = computed(() => this.selectedIngredients().length > 0);
    hasSelectedTools = computed(() => this.selectedTools().length > 0);
    isFormValid = computed(() => this.suggestionForm.valid);
    isAIFormValid = computed(() => this.aiSuggestionForm.valid);

    private destroy$ = new Subject<void>();

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
        this.loadReferenceData();
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
                if (this.activeTab() === 'ingredients' && this.selectedIngredients().length > 0) {
                    this.getSuggestions();
                }
            });
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    private loadReferenceData(): void {
        // Load all recipe reference data in bulk for performance
        this.recipeReferenceService.getRecipeReferencesBulk()
            .pipe(takeUntil(this.destroy$))
            .subscribe(({ difficulties, cuisines, mealTypes, dietaryOptions }) => {
                this.difficulties.set(difficulties);
                this.cuisines.set(cuisines);
                this.mealTypes.set(mealTypes);
                this.dietaryOptions.set(dietaryOptions);
            });
    }

    /**
     * Get recipe suggestions based on selected ingredients and tools
     */
    getSuggestions(): void {
        if (this.selectedIngredients().length === 0) {
            return;
        }

        this.loading.set(true);
        const query: RecipeSuggestionQuery = this.suggestionForm.value;

        this.recipeSuggestionService.getRecipeSuggestions(query, this.selectedIngredients(), this.selectedTools())
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions.set(response);
                    this.loading.set(false);
                },
                error: (error) => {
                    console.error('Error getting suggestions:', error);
                    this.loading.set(false);
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

        this.aiLoading.set(true);
        const query = this.aiSuggestionForm.value;

        this.recipeSuggestionService.generateAISuggestions(query)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (response) => {
                    this.suggestions.set(response);
                    this.aiLoading.set(false);
                },
                error: (error) => {
                    console.error('Error generating AI suggestions:', error);
                    this.aiLoading.set(false);
                }
            });
    }

    /**
     * Load trending recipes
     */
    private loadTrendingRecipes(): void {
        this.recipeService.getTrendingRecipes()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (recipes) => {
                    this.trendingRecipes.set(recipes);
                },
                error: (error) => {
                    console.error('Error loading trending recipes:', error);
                }
            });
    }

    /**
     * Load seasonal recipes
     */
    private loadSeasonalRecipes(): void {
        this.recipeService.getSeasonalRecipes()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (recipes) => {
                    this.seasonalRecipes.set(recipes);
                },
                error: (error) => {
                    console.error('Error loading seasonal recipes:', error);
                }
            });
    }

    /**
     * Load recommendations based on user preferences
     */
    private loadRecommendations(): void {
        this.recipeService.getRecommendations()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (recipes) => {
                    this.suggestions.set(recipes);
                },
                error: (error) => {
                    console.error('Error loading recommendations:', error);
                }
            });
    }

    /**
     * Toggle ingredient selection
     */
    toggleIngredient(ingredientId: number): void {
        const current = this.selectedIngredients();
        const index = current.indexOf(ingredientId);
        if (index > -1) {
            this.selectedIngredients.set(current.filter(id => id !== ingredientId));
        } else {
            this.selectedIngredients.set([...current, ingredientId]);
        }
    }

    /**
     * Toggle tool selection
     */
    toggleTool(toolId: number): void {
        const current = this.selectedTools();
        const index = current.indexOf(toolId);
        if (index > -1) {
            this.selectedTools.set(current.filter(id => id !== toolId));
        } else {
            this.selectedTools.set([...current, toolId]);
        }
    }

    /**
     * Check if ingredient is selected
     */
    isIngredientSelected(ingredientId: number): boolean {
        return this.selectedIngredients().includes(ingredientId);
    }

    /**
     * Check if tool is selected
     */
    isToolSelected(toolId: number): boolean {
        return this.selectedTools().includes(toolId);
    }

    /**
     * Get selected ingredients count
     */
    getSelectedIngredientsCount(): number {
        return this.selectedIngredients().length;
    }

    /**
     * Get selected tools count
     */
    getSelectedToolsCount(): number {
        return this.selectedTools().length;
    }

    /**
     * Set active tab
     */
    setActiveTab(tab: string): void {
        this.activeTab.set(tab);
    }

    /**
     * Clear all selections
     */
    clearSelections(): void {
        this.selectedIngredients.set([]);
        this.selectedTools.set([]);
    }

    /**
     * Get suggestions count
     */
    getSuggestionsCount(): number {
        return this.suggestions().length;
    }

    /**
     * Get trending recipes count
     */
    getTrendingRecipesCount(): number {
        return this.trendingRecipes().length;
    }

    /**
     * Get seasonal recipes count
     */
    getSeasonalRecipesCount(): number {
        return this.seasonalRecipes().length;
    }


    /**
     * Get form errors
     */
    getFormErrors(): any {
        return this.suggestionForm.errors;
    }

    /**
     * Get AI form errors
     */
    getAIFormErrors(): any {
        return this.aiSuggestionForm.errors;
    }

    /**
     * Reset suggestion form
     */
    resetSuggestionForm(): void {
        this.suggestionForm.reset({
            limit: 10,
            maxMissingIngredients: 5,
            maxMissingTools: 5,
            includeIngredientsOnHand: true,
            includeToolsOnHand: true,
            queryFilter: '',
            maxPrepTime: null,
            maxCookTime: null,
            maxDifficulty: null,
            includePublicRecipes: true,
            includePrivateRecipes: false,
            categories: [],
            tags: [],
            dietaryRestrictions: [],
            cuisines: []
        });
    }

    /**
     * Reset AI suggestion form
     */
    resetAISuggestionForm(): void {
        this.aiSuggestionForm.reset({
            description: '',
            availableIngredients: [],
            availableTools: [],
            preferences: [],
            dietaryRestrictions: [],
            dislikedIngredients: [],
            servingSize: 4,
            maxPrepTime: 30,
            maxCookTime: 60,
            budgetLimit: 50,
            cuisine: '',
            mealType: '',
            difficulty: '',
            includeNutritionalInfo: true,
            includeSubstitutions: true
        });
    }

    /**
     * Get difficulty options for display
     */
    getDifficultyOptions(): any[] {
        return this.difficulties();
    }

    /**
     * Get cuisine options for display
     */
    getCuisineOptions(): any[] {
        return this.cuisines();
    }

    /**
     * Get meal type options for display
     */
    getMealTypeOptions(): any[] {
        return this.mealTypes();
    }

    /**
     * Get dietary options for display
     */
    getDietaryOptions(): any[] {
        return this.dietaryOptions();
    }
} 