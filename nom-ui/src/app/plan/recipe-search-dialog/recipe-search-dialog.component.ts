import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of } from 'rxjs';
import { RecipeSearchService } from '../../core/services/recipe-search.service';
import { MealPlanService } from '../../core/services/meal-plan.service';
import { RecipeSearchResult } from '../../core/models/recipe-search-result.model';
import { MealPlanEntry } from '../../core/models/meal-plan-entry.model';

export interface RecipeSearchDialogData {
  householdId: number;
  date: string;
  mealTypeId: number;
  mealType: string;
  entries: MealPlanEntry[];
}

export type RecipeSearchDialogResult = { changed: boolean } | undefined;

@Component({
  selector: 'nom-recipe-search-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './recipe-search-dialog.component.html',
  styleUrl: './recipe-search-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeSearchDialog implements OnInit {
  private dialogRef = inject(MatDialogRef<RecipeSearchDialog>);
  data: RecipeSearchDialogData = inject(MAT_DIALOG_DATA);
  private recipeSearchService = inject(RecipeSearchService);
  private mealPlanService = inject(MealPlanService);
  private destroyRef = inject(DestroyRef);

  entries = signal<MealPlanEntry[]>([]);
  changed = signal(false);
  busy = signal(false);

  searchQuery = signal('');
  results = signal<RecipeSearchResult[]>([]);
  searching = signal(false);

  rouletteLoading = signal(false);

  noteTitle = signal('');
  noteText = signal('');

  private searchSubject = new Subject<string>();

  ngOnInit(): void {
    this.entries.set([...this.data.entries]);

    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(query => {
        if (!query || query.length < 2) {
          return of(null);
        }
        this.searching.set(true);
        return this.recipeSearchService.search({
          query,
          page: 1,
          pageSize: 10,
          includeIngredients: false,
          includeSteps: false,
          includeNutrition: false,
        });
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (response) => {
        this.searching.set(false);
        this.results.set(response?.results ?? []);
      },
      error: () => {
        this.searching.set(false);
        this.results.set([]);
      },
    });
  }

  onSearchInput(query: string): void {
    this.searchQuery.set(query);
    this.searchSubject.next(query);
  }

  surpriseMe(): void {
    this.rouletteLoading.set(true);
    this.recipeSearchService.getRandom(1, this.data.householdId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (response) => {
        this.rouletteLoading.set(false);
        if (response.results.length > 0) {
          this.addRecipe(response.results[0]);
        }
      },
      error: () => {
        this.rouletteLoading.set(false);
      },
    });
  }

  addRecipe(recipe: RecipeSearchResult): void {
    this.busy.set(true);
    this.mealPlanService.createMealPlan({
      householdId: this.data.householdId,
      date: this.data.date,
      mealTypeId: this.data.mealTypeId,
      title: recipe.name,
      notes: null,
      recipeId: recipe.id,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (response) => {
        this.entries.update(list => [...list, {
          id: response.id,
          recipeId: response.recipeId,
          recipeName: response.recipeName,
          recipeImage: null,
          title: response.title,
          notes: response.notes,
          calories: null,
          proteinGrams: null,
          carbGrams: null,
          fatGrams: null,
          completedDate: null,
        }]);
        this.changed.set(true);
        this.busy.set(false);
        this.searchQuery.set('');
        this.results.set([]);
      },
      error: () => {
        this.busy.set(false);
      },
    });
  }

  addNote(): void {
    const title = this.noteTitle().trim();
    const notes = this.noteText().trim();
    if (!title && !notes) return;

    this.busy.set(true);
    this.mealPlanService.createMealPlan({
      householdId: this.data.householdId,
      date: this.data.date,
      mealTypeId: this.data.mealTypeId,
      title: title || null,
      notes: notes || null,
      recipeId: null,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (response) => {
        this.entries.update(list => [...list, {
          id: response.id,
          recipeId: null,
          recipeName: null,
          recipeImage: null,
          title: response.title,
          notes: response.notes,
          calories: null,
          proteinGrams: null,
          carbGrams: null,
          fatGrams: null,
          completedDate: null,
        }]);
        this.changed.set(true);
        this.busy.set(false);
        this.noteTitle.set('');
        this.noteText.set('');
      },
      error: () => {
        this.busy.set(false);
      },
    });
  }

  removeEntry(entry: MealPlanEntry): void {
    this.busy.set(true);
    this.mealPlanService.deleteMealPlan(entry.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.entries.update(list => list.filter(e => e.id !== entry.id));
        this.changed.set(true);
        this.busy.set(false);
      },
      error: () => {
        this.busy.set(false);
      },
    });
  }

  clearAll(): void {
    const ids = this.entries().map(e => e.id);
    if (ids.length === 0) return;

    this.busy.set(true);
    let completed = 0;
    for (const id of ids) {
      this.mealPlanService.deleteMealPlan(id).pipe(
        takeUntilDestroyed(this.destroyRef),
      ).subscribe({
        next: () => {
          completed++;
          if (completed === ids.length) {
            this.entries.set([]);
            this.changed.set(true);
            this.busy.set(false);
          }
        },
        error: () => {
          completed++;
          if (completed === ids.length) {
            this.busy.set(false);
          }
        },
      });
    }
  }

  done(): void {
    this.dialogRef.close({ changed: this.changed() } as RecipeSearchDialogResult);
  }

  cancel(): void {
    this.dialogRef.close({ changed: this.changed() } as RecipeSearchDialogResult);
  }
}
