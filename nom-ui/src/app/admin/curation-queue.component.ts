import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { AdminService } from '../core/services/admin.service';
import { DishGroupService } from '../core/services/dish-group.service';
import { CurationQueueItem } from '../core/models/curation-queue-item.model';
import { DishGroupModel } from '../core/models/dish-group.model';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-curation-queue',
  imports: [
    DatePipe,
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
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  items = signal<CurationQueueItem[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  expandedId = signal<number | null>(null);
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

  expand(item: CurationQueueItem): void {
    this.expandedId.set(item.id);
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
    this.dishGroupService.assignRecipe(item.entityId, match
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

  approve(item: CurationQueueItem): void {
    this.processing.set(true);
    this.adminService.approveCuration({
      entityId: item.entityId,
      entityType: item.entityType,
      feedbackNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to approve item.');
        this.processing.set(false);
      },
    });
  }

  requestRevision(item: CurationQueueItem): void {
    this.processing.set(true);
    this.adminService.requestRevision({
      entityId: item.entityId,
      entityType: item.entityType,
      feedbackNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to request revision.');
        this.processing.set(false);
      },
    });
  }

  reject(item: CurationQueueItem): void {
    this.processing.set(true);
    this.adminService.rejectCuration({
      entityId: item.entityId,
      entityType: item.entityType,
      feedbackNotes: this.feedbackNotes.value ?? '',
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes.setValue('');
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to reject item.');
        this.processing.set(false);
      },
    });
  }
}
