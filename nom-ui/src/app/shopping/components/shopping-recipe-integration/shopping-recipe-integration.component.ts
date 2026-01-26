import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil, finalize, forkJoin } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInputComponent,
  AmwProgressSpinnerComponent,
  AmwIconComponent,
} from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { RecipeModel } from '../../../recipe/models/recipe.model';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-shopping-recipe-integration',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwInputComponent,
    AmwProgressSpinnerComponent,
    AmwIconComponent,
  ],
  templateUrl: './shopping-recipe-integration.component.html',
  styleUrl: './shopping-recipe-integration.component.scss',
})
export class ShoppingRecipeIntegrationComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private recipeService = inject(RecipeService);
  private notificationService = inject(NotificationService);
  private fb = inject(FormBuilder);

  // Signals
  shoppingListId = signal<number>(0);
  shoppingList = signal<ShoppingListResponseModel | null>(null);
  recipes = signal<RecipeModel[]>([]);
  filteredRecipes = signal<RecipeModel[]>([]);
  selectedRecipe = signal<RecipeModel | null>(null);
  isLoading = signal(true);
  isAdding = signal(false);
  error = signal<string | null>(null);
  searchTerm = signal('');

  // Computed
  hasRecipes = computed(() => this.filteredRecipes().length > 0);

  // Form
  servingsForm: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  constructor() {
    this.servingsForm = this.fb.group({
      servings: [4, [Validators.required, Validators.min(1), Validators.max(100)]],
    });
  }

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.shoppingListId.set(+id);
        this.loadData();
      } else {
        this.error.set('Invalid shopping list ID');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData(): void {
    this.isLoading.set(true);
    this.error.set(null);

    forkJoin({
      shoppingList: this.shoppingService.getShoppingList(this.shoppingListId()),
      recipes: this.recipeService.getRecipes(),
    })
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: ({ shoppingList, recipes }) => {
          this.shoppingList.set(shoppingList);
          this.recipes.set(recipes);
          this.filteredRecipes.set(recipes);
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.SHOPPING.LOAD_FAILED);
          console.error('Error loading data:', err);
        },
      });
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    const term = input.value.toLowerCase();
    this.searchTerm.set(term);

    if (!term) {
      this.filteredRecipes.set(this.recipes());
      return;
    }

    const filtered = this.recipes().filter(
      (recipe) =>
        recipe.name.toLowerCase().includes(term) ||
        recipe.description?.toLowerCase().includes(term) ||
        recipe.authorName?.toLowerCase().includes(term)
    );
    this.filteredRecipes.set(filtered);
  }

  onSelectRecipe(recipe: RecipeModel): void {
    this.selectedRecipe.set(recipe);
    // Reset servings to default
    this.servingsForm.patchValue({ servings: 4 });
  }

  onClearSelection(): void {
    this.selectedRecipe.set(null);
  }

  onAddToShoppingList(): void {
    if (this.servingsForm.invalid || !this.selectedRecipe()) return;

    const recipe = this.selectedRecipe()!;
    const servings = this.servingsForm.value.servings;

    this.isAdding.set(true);
    this.error.set(null);

    this.shoppingService
      .addRecipeIngredients(this.shoppingListId(), recipe.id, servings)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isAdding.set(false))
      )
      .subscribe({
        next: () => {
          this.notificationService.success(`Added ingredients from "${recipe.name}" to shopping list`);
          this.onClearSelection();
          this.loadData(); // Reload to show updated shopping list
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.SHOPPING.ITEM_ADD_FAILED);
          this.notificationService.error(ERROR_MESSAGES.SHOPPING.ITEM_ADD_FAILED);
          console.error('Error adding recipe ingredients:', err);
        },
      });
  }

  onBack(): void {
    this.router.navigate(['/shopping', this.shoppingListId()]);
  }

  onRetry(): void {
    this.loadData();
  }

  getIngredientCount(recipe: RecipeModel): number {
    return recipe.ingredients?.length || 0;
  }

  getRatingDisplay(recipe: RecipeModel): string {
    if (!recipe.rating || recipe.rating === 0) return 'No ratings';
    return `${recipe.rating.toFixed(1)} ★ (${recipe.ratingCount || 0} ratings)`;
  }
}
