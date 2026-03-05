import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IngredientService } from '../core/services/ingredient.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-ingredient-form',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="nom-form--full">
      <div class="nom-form__header">
        <h1 class="nom-form__title">{{ pageTitle() }}</h1>
        <p class="nom-form__subtitle">{{ pageSubtitle() }}</p>
      </div>

      @if (errorMessage()) {
        <div class="nom-form__error">
          <mat-icon>error_outline</mat-icon>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      @if (loading()) {
        <div class="nom-ingredient-form__loading">
          <mat-spinner diameter="40"></mat-spinner>
        </div>
      } @else {
        <form [formGroup]="ingredientForm" (ngSubmit)="onSubmit()">
          <div class="nom-form__section">
            <h2 class="nom-form__section-title">Details</h2>
            <div class="nom-form__fields">
              <mat-form-field appearance="outline">
                <mat-label>Name</mat-label>
                <input matInput formControlName="name" maxlength="255" />
                @if (ingredientForm.get('name')?.hasError('required')) {
                  <mat-error>Name is required</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Plural Name</mat-label>
                <input matInput formControlName="pluralName" maxlength="255" />
                <mat-hint>e.g. "tomatoes" for "tomato"</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Description</mat-label>
                <textarea matInput formControlName="description" rows="3" maxlength="1023"></textarea>
              </mat-form-field>
            </div>
          </div>

          @if (isEditMode() && nutrients().length > 0) {
            <div class="nom-form__section">
              <h2 class="nom-form__section-title">Nutrients</h2>
              <div class="nom-ingredient-form__nutrients">
                @for (n of nutrients(); track n.id) {
                  <div class="nom-ingredient-form__nutrient">
                    <span class="nom-ingredient-form__nutrient-name">{{ n.nutrientName }}</span>
                    <span class="nom-ingredient-form__nutrient-value">{{ n.amount }} {{ n.unitName }}</span>
                  </div>
                }
              </div>
            </div>
          }

          @if (isEditMode() && aliases().length > 0) {
            <div class="nom-form__section">
              <h2 class="nom-form__section-title">Aliases</h2>
              <div class="nom-ingredient-form__aliases">
                @for (a of aliases(); track a.id) {
                  <span class="nom-ingredient-form__alias">{{ a.name }}</span>
                }
              </div>
            </div>
          }

          <div class="nom-form__actions">
            <a mat-button routerLink="/ingredients/mine">Cancel</a>
            <button mat-flat-button type="submit"
                    [disabled]="ingredientForm.invalid || saving()">
              {{ isEditMode() ? 'Save Changes' : 'Create Ingredient' }}
            </button>
          </div>
        </form>
      }
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-ingredient-form__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-12;
    }

    .nom-ingredient-form__nutrients {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: vars.$spacing-2;
    }

    .nom-ingredient-form__nutrient {
      display: flex;
      justify-content: space-between;
      padding: vars.$spacing-2 vars.$spacing-3;
      background: var(--mat-sys-surface-container-low);
      border-radius: vars.$nom-border-radius;
      font-size: vars.$font-size-sm;
    }

    .nom-ingredient-form__nutrient-name {
      color: var(--mat-sys-on-surface-variant);
    }

    .nom-ingredient-form__nutrient-value {
      font-weight: vars.$font-weight-medium;
      color: var(--mat-sys-on-surface);
    }

    .nom-ingredient-form__aliases {
      display: flex;
      flex-wrap: wrap;
      gap: vars.$spacing-2;
    }

    .nom-ingredient-form__alias {
      padding: vars.$spacing-1 vars.$spacing-3;
      background: var(--mat-sys-surface-container-high);
      border-radius: vars.$nom-border-radius-pill;
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
    }
  `],
})
export class IngredientForm implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private ingredientService = inject(IngredientService);
  private loadingService = inject(LoadingService);

  isEditMode = signal(false);
  ingredientId = signal<number | null>(null);
  loading = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  nutrients = signal<{ id: number; nutrientName: string; amount: number; unitName: string }[]>([]);
  aliases = signal<{ id: number; name: string }[]>([]);

  ingredientForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    pluralName: ['', [Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(1023)]],
  });

  pageTitle = computed(() => this.isEditMode() ? 'Edit Ingredient' : 'New Ingredient');
  pageSubtitle = computed(() => this.isEditMode() ? 'Update ingredient details' : 'Create a custom ingredient');

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      const id = params['id'];
      if (id) {
        this.isEditMode.set(true);
        this.ingredientId.set(Number(id));
        this.loadIngredient(Number(id));
      }
    });
  }

  private loadIngredient(id: number): void {
    this.loading.set(true);
    this.ingredientService.getIngredient(id).pipe(
      this.loadingService.loading('Loading ingredient...')
    ).subscribe({
      next: (ing) => {
        this.ingredientForm.patchValue({
          name: ing.name,
          pluralName: ing.pluralName,
          description: ing.description,
        });
        this.nutrients.set(ing.nutrients ?? []);
        this.aliases.set(ing.aliases ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load ingredient.');
        this.loading.set(false);
      },
    });
  }

  onSubmit(): void {
    if (this.ingredientForm.invalid || this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const form = this.ingredientForm.getRawValue();

    if (this.isEditMode()) {
      const id = this.ingredientId()!;
      this.ingredientService.updateIngredient(id, {
        id,
        name: form.name!,
        pluralName: form.pluralName ?? '',
        description: form.description ?? '',
      }).pipe(
        this.loadingService.loading('Saving ingredient...')
      ).subscribe({
        next: () => {
          this.saving.set(false);
          this.router.navigate(['/ingredients/mine']);
        },
        error: () => {
          this.saving.set(false);
          this.errorMessage.set('Failed to save ingredient.');
        },
      });
    } else {
      this.ingredientService.createIngredient({
        name: form.name!,
        pluralName: form.pluralName ?? '',
        description: form.description ?? '',
      }).pipe(
        this.loadingService.loading('Creating ingredient...')
      ).subscribe({
        next: () => {
          this.saving.set(false);
          this.router.navigate(['/ingredients/mine']);
        },
        error: () => {
          this.saving.set(false);
          this.errorMessage.set('Failed to create ingredient.');
        },
      });
    }
  }
}
