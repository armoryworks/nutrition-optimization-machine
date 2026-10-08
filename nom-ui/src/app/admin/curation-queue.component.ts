import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AdminService } from '../core/services/admin.service';
import { DishGroupService } from '../core/services/dish-group.service';
import { CurationQueueItem } from '../core/models/curation-queue-item.model';
import { DishGroupModel } from '../core/models/dish-group.model';
import { LoadingService } from '../core/services/loading.service';
import { FoodCatalogService } from '../core/services/food-catalog.service';
import { CurationStatus } from '../core/models/food-catalog.model';

@Component({
  selector: 'nom-curation-queue',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatAutocompleteModule, ErrorBanner],
  templateUrl: './curation-queue.component.html',
  styleUrl: './curation-queue.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurationQueue implements OnInit {
  private adminService = inject(AdminService);
  private dishGroupService = inject(DishGroupService);
  private foodCatalogService = inject(FoodCatalogService);
  private loadingService = inject(LoadingService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  items = signal<CurationQueueItem[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  expandedKey = signal<string | null>(null);
  feedbackNotes = new FormControl('');

  // Dish-group control (recipes only): autocomplete existing groups or free-text create.
  dishGroups = signal<DishGroupModel[]>([]);
  dishGroupControl = new FormControl('', { nonNullable: true });
  dishGroupSaving = signal(false);
  dishGroupSaved = signal(false);

  ngOnInit(): void {
    this.loadQueue();
    this.dishGroupService.list(500).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (groups) => this.dishGroups.set(groups),
      error: () => this.dishGroups.set([]),
    });
  }

  itemKey(item: CurationQueueItem): string {
    return `${item.entityType}:${item.id}`;
  }

  itemIcon(item: CurationQueueItem): string {
    switch (item.entityType) {
      case 'Recipe': return 'restaurant_menu';
      case 'Plan': return 'calendar_month';
      default: return 'shopping_basket';
    }
  }

  expand(item: CurationQueueItem): void {
    this.expandedKey.set(this.itemKey(item));
    this.dishGroupControl.setValue('');
    this.dishGroupSaved.set(false);
  }

  filteredDishGroups(): DishGroupModel[] {
    const search = this.dishGroupControl.value.trim().toLowerCase();
    const groups = this.dishGroups();
    if (!search) return groups.slice(0, 20);
    return groups.filter((g) => g.name.includes(search)).slice(0, 20);
  }

  /** Assign by exact-name match (id) or free text (create-by-name); empty clears. */
  saveDishGroup(item: CurationQueueItem): void {
    if (this.dishGroupSaving()) return;
    const value = this.dishGroupControl.value.trim().toLowerCase();
    const match = this.dishGroups().find((g) => g.name === value);

    this.dishGroupSaving.set(true);
    this.dishGroupSaved.set(false);
    this.dishGroupService.assignRecipe(item.id, match
      ? { dishGroupId: match.id }
      : { dishGroupName: value || null },
    ).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.dishGroupSaving.set(false);
        this.dishGroupSaved.set(true);
      },
      error: () => {
        this.dishGroupSaving.set(false);
        this.errorMessage.set('Failed to save dish group.');
      },
    });
  }

  /** Import-vetting problems, one per line. */
  vettingIssueList(item: CurationQueueItem): string[] {
    return (item.vettingIssues ?? '')
      .split('\n')
      .map((issue) => issue.trim())
      .filter((issue) => issue.length > 0);
  }

  loadQueue(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.adminService.getCurationQueue().pipe(
      this.loadingService.loading('Loading curation queue...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load curation queue. You may not have permission.');
      },
    });
  }

  /**
   * Recipe approval is refused while any ingredient is uncurated (N-6). Let the admin
   * clear the blockers from the same card: mark them Curated (planning-usable) and
   * remove them from this recipe's blocker list, so Approve then goes through.
   * Curation status only — nutrition values are never authored here.
   */
  approveBlockingIngredients(item: CurationQueueItem): void {
    const ids = (item.uncuratedIngredients ?? []).map((i) => i.id);
    if (ids.length === 0) return;
    this.processing.set(true);
    this.errorMessage.set('');
    this.foodCatalogService.setCurationStatus(ids, CurationStatus.Curated)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          const cleared = new Set(ids);
          this.items.update((list) => list
            // The ingredients themselves may also sit in the queue as PendingCuration rows.
            .filter((i) => !(i.entityType === 'Ingredient' && cleared.has(i.id)))
            .map((i) => i.entityType === 'Recipe'
              ? { ...i, uncuratedIngredients: (i.uncuratedIngredients ?? []).filter((b) => !cleared.has(b.id)) }
              : i));
          this.processing.set(false);
        },
        error: (err: unknown) => {
          const apiMessage = (err as { error?: { message?: string } } | undefined)?.error?.message;
          this.errorMessage.set(apiMessage ?? 'Failed to curate the ingredients.');
          this.processing.set(false);
        },
      });
  }

  approve(item: CurationQueueItem): void {
    this.processing.set(true);
    this.errorMessage.set('');
    this.adminService.approveCuration({
      entityId: item.id,
      entityType: item.entityType,
      decisionNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => this.itemKey(i) !== this.itemKey(item)));
        this.expandedKey.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
        this.snackBar.open(`Approved "${item.name}"`, undefined, { duration: 3000 });
      },
      error: (err: unknown) => {
        const apiMessage = (err as { error?: { message?: string } } | undefined)?.error?.message;
        this.errorMessage.set(apiMessage ?? 'Failed to approve item.');
        this.processing.set(false);
      },
    });
  }

  requestRevision(item: CurationQueueItem): void {
    this.processing.set(true);
    this.errorMessage.set('');
    this.adminService.requestRevision({
      entityId: item.id,
      entityType: item.entityType,
      decisionNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => this.itemKey(i) !== this.itemKey(item)));
        this.expandedKey.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
      },
      error: (err: unknown) => {
        const apiMessage = (err as { error?: { message?: string } } | undefined)?.error?.message;
        this.errorMessage.set(apiMessage ?? 'Failed to request revision.');
        this.processing.set(false);
      },
    });
  }

  reject(item: CurationQueueItem): void {
    this.processing.set(true);
    this.errorMessage.set('');
    this.adminService.rejectCuration({
      entityId: item.id,
      entityType: item.entityType,
      decisionNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => this.itemKey(i) !== this.itemKey(item)));
        this.expandedKey.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
      },
      error: (err: unknown) => {
        const apiMessage = (err as { error?: { message?: string } } | undefined)?.error?.message;
        this.errorMessage.set(apiMessage ?? 'Failed to reject item.');
        this.processing.set(false);
      },
    });
  }
}
