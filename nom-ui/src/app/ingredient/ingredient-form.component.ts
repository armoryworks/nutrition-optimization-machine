import { Component, inject, signal, computed, OnInit, ChangeDetectionStrategy } from '@angular/core';
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
  templateUrl: './ingredient-form.component.html',
  styleUrl: './ingredient-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
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
