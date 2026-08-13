import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { DietAdminService } from '../core/services/diet-admin.service';
import {
  RestrictionGroupModel,
  RestrictionCategoryModel,
  RestrictionCriterionModel,
} from '../core/models/diet-admin.model';

/**
 * Diet categories admin: create/curate the restriction categories users pick
 * from (Medical Conditions, Diets, etc.) and the filter criteria that give a
 * category teeth (ingredient patterns / exact ingredients / nutrient caps).
 */
@Component({
  selector: 'nom-diet-categories',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  templateUrl: './diet-categories.component.html',
  styleUrl: './diet-categories.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DietCategories implements OnInit {
  private dietAdminService = inject(DietAdminService);
  private destroyRef = inject(DestroyRef);

  groups = signal<RestrictionGroupModel[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');

  /** Group whose "new category" form is open. */
  addingToGroupId = signal<number | null>(null);
  /** Category whose criteria editor is expanded. */
  expandedCategoryId = signal<number | null>(null);
  criteria = signal<RestrictionCriterionModel[]>([]);
  criteriaLoading = signal(false);

  categoryForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(255)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1023)] }),
  });

  criterionForm = new FormGroup({
    ingredientPattern: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(255)] }),
    severity: new FormControl(3, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(5)] }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1023)] }),
  });

  expandedCategory = computed<RestrictionCategoryModel | null>(() => {
    const id = this.expandedCategoryId();
    if (id === null) return null;
    for (const g of this.groups()) {
      const hit = g.categories.find((c) => c.id === id);
      if (hit) return hit;
    }
    return null;
  });

  ngOnInit(): void {
    this.loadGroups();
  }

  loadGroups(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.dietAdminService.getGroups().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (groups) => {
        this.groups.set(groups);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load diet categories. You may not have permission.');
      },
    });
  }

  startAdd(group: RestrictionGroupModel): void {
    this.categoryForm.reset();
    this.addingToGroupId.set(group.id);
  }

  cancelAdd(): void {
    this.addingToGroupId.set(null);
  }

  createCategory(group: RestrictionGroupModel): void {
    if (this.categoryForm.invalid || this.processing()) return;
    this.processing.set(true);
    const { name, description } = this.categoryForm.getRawValue();
    this.dietAdminService.createCategory(group.id, name.trim(), description.trim() || undefined).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (created) => {
        this.groups.update((gs) => gs.map((g) =>
          g.id === group.id
            ? { ...g, categories: [...g.categories, created].sort((a, b) => a.name.localeCompare(b.name)) }
            : g));
        this.addingToGroupId.set(null);
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to create category.');
        this.processing.set(false);
      },
    });
  }

  deleteCategory(category: RestrictionCategoryModel): void {
    if (this.processing()) return;
    this.processing.set(true);
    this.dietAdminService.deleteCategory(category.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.groups.update((gs) => gs.map((g) =>
          ({ ...g, categories: g.categories.filter((c) => c.id !== category.id) })));
        if (this.expandedCategoryId() === category.id) this.expandedCategoryId.set(null);
        this.processing.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.status === 409
          ? `"${category.name}" is in use by existing restrictions and can't be deleted.`
          : 'Failed to delete category.');
        this.processing.set(false);
      },
    });
  }

  toggleCriteria(category: RestrictionCategoryModel): void {
    if (this.expandedCategoryId() === category.id) {
      this.expandedCategoryId.set(null);
      return;
    }
    this.expandedCategoryId.set(category.id);
    this.criterionForm.reset({ ingredientPattern: '', severity: 3, notes: '' });
    this.loadCriteria(category.id);
  }

  private loadCriteria(categoryId: number): void {
    this.criteriaLoading.set(true);
    this.criteria.set([]);
    this.dietAdminService.getCriteria(categoryId).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (criteria) => {
        this.criteria.set(criteria);
        this.criteriaLoading.set(false);
      },
      error: () => {
        this.criteriaLoading.set(false);
        this.errorMessage.set('Failed to load criteria.');
      },
    });
  }

  addCriterion(): void {
    const categoryId = this.expandedCategoryId();
    if (categoryId === null || this.processing()) return;
    const { ingredientPattern, severity, notes } = this.criterionForm.getRawValue();
    if (!ingredientPattern.trim()) return;
    this.processing.set(true);
    this.dietAdminService.addCriterion(categoryId, {
      ingredientPattern: ingredientPattern.trim(),
      severity,
      notes: notes.trim() || undefined,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (created) => {
        this.criteria.update((list) => [...list, created]);
        this.bumpCriteriaCount(categoryId, +1);
        this.criterionForm.reset({ ingredientPattern: '', severity: 3, notes: '' });
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to add criterion.');
        this.processing.set(false);
      },
    });
  }

  deleteCriterion(criterion: RestrictionCriterionModel): void {
    if (this.processing()) return;
    this.processing.set(true);
    this.dietAdminService.deleteCriterion(criterion.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.criteria.update((list) => list.filter((c) => c.id !== criterion.id));
        this.bumpCriteriaCount(criterion.restrictionTypeId, -1);
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to delete criterion.');
        this.processing.set(false);
      },
    });
  }

  private bumpCriteriaCount(categoryId: number, delta: number): void {
    this.groups.update((gs) => gs.map((g) => ({
      ...g,
      categories: g.categories.map((c) =>
        c.id === categoryId ? { ...c, criteriaCount: c.criteriaCount + delta } : c),
    })));
  }
}
