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
import { AdminService } from '../core/services/admin.service';
import { CurationQueueItem } from '../core/models/curation-queue-item.model';
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
    MatInputModule, ErrorBanner],
  templateUrl: './curation-queue.component.html',
  styleUrl: './curation-queue.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurationQueue implements OnInit {
  private adminService = inject(AdminService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  items = signal<CurationQueueItem[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  expandedId = signal<number | null>(null);
  feedbackNotes = new FormControl('');

  ngOnInit(): void {
    this.loadQueue();
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
