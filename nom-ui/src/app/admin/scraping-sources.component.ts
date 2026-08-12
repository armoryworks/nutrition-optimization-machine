import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { ScrapingSourceService } from '../core/services/scraping-source.service';
import { ScrapingSourceModel, ScrapingSourceStatus } from '../core/models/scraping-source.model';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-scraping-sources',
  imports: [
    DatePipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    ErrorBanner,
  ],
  templateUrl: './scraping-sources.component.html',
  styleUrl: './scraping-sources.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScrapingSources implements OnInit {
  private scrapingSourceService = inject(ScrapingSourceService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  readonly statuses: ScrapingSourceStatus[] = ['Pending', 'Approved', 'Rejected'];

  sources = signal<ScrapingSourceModel[]>([]);
  statusFilter = signal<ScrapingSourceStatus>('Pending');
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  /** Source id whose inline reject-notes prompt is open. */
  rejectingId = signal<number | null>(null);
  rejectNotes = new FormControl('');

  ngOnInit(): void {
    this.loadSources();
  }

  setFilter(status: ScrapingSourceStatus): void {
    if (this.statusFilter() === status) return;
    this.statusFilter.set(status);
    this.rejectingId.set(null);
    this.rejectNotes.setValue('');
    this.loadSources();
  }

  loadSources(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.scrapingSourceService.getSources(this.statusFilter()).pipe(
      this.loadingService.loading('Loading scraping sources...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (sources) => {
        this.sources.set(sources);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load scraping sources. You may not have permission.');
      },
    });
  }

  approve(source: ScrapingSourceModel): void {
    this.processing.set(true);
    this.scrapingSourceService.approve(source.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (updated) => this.applyReview(updated),
      error: () => {
        this.errorMessage.set('Failed to approve source.');
        this.processing.set(false);
      },
    });
  }

  startReject(source: ScrapingSourceModel): void {
    this.rejectNotes.setValue('');
    this.rejectingId.set(source.id);
  }

  cancelReject(): void {
    this.rejectingId.set(null);
    this.rejectNotes.setValue('');
  }

  confirmReject(source: ScrapingSourceModel): void {
    this.processing.set(true);
    this.scrapingSourceService.reject(source.id, this.rejectNotes.value ?? '').pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (updated) => this.applyReview(updated),
      error: () => {
        this.errorMessage.set('Failed to reject source.');
        this.processing.set(false);
      },
    });
  }

  /** Reviewed rows leave the current filter's list unless their new status still matches it. */
  private applyReview(updated: ScrapingSourceModel): void {
    this.sources.update((list) =>
      updated.status === this.statusFilter()
        ? list.map((s) => (s.id === updated.id ? updated : s))
        : list.filter((s) => s.id !== updated.id),
    );
    this.rejectingId.set(null);
    this.rejectNotes.setValue('');
    this.processing.set(false);
  }
}
