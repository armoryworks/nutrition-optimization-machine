import { Component, inject, signal, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';

import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AdminService, CurationQueueItem } from '../core/services/admin.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-curation-queue',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  template: `
    <div class="nom-dashboard">
      <div class="nom-dashboard__header">
        <div class="nom-dashboard__header-left">
          <h1 class="nom-dashboard__title">Curation Queue</h1>
          <span class="nom-dashboard__subtitle">{{ items().length }} items pending review</span>
        </div>
        <div class="nom-dashboard__header-right">
          <button mat-icon-button (click)="loadQueue()" aria-label="Refresh">
            <mat-icon>refresh</mat-icon>
          </button>
        </div>
      </div>

      @if (errorMessage()) {
        <div class="nom-dashboard__error">
          <mat-icon>error_outline</mat-icon>
          <p>{{ errorMessage() }}</p>
        </div>
      }

      @if (loading()) {
        <div class="nom-curation__loading">
          <mat-spinner diameter="40"></mat-spinner>
        </div>
      } @else if (items().length === 0) {
        <div class="nom-dashboard__empty">
          <mat-icon class="nom-dashboard__empty-icon">fact_check</mat-icon>
          <h2 class="nom-dashboard__empty-title">Queue is empty</h2>
          <p class="nom-dashboard__empty-message">No items waiting for review.</p>
        </div>
      } @else {
        <div class="nom-curation__list">
          @for (item of items(); track item.id) {
            <div class="nom-curation__item">
              <div class="nom-curation__item-header">
                <mat-icon class="nom-curation__item-icon">
                  {{ item.entityType === 'Recipe' ? 'restaurant_menu' : 'egg' }}
                </mat-icon>
                <div class="nom-curation__item-info">
                  <span class="nom-curation__item-name">{{ item.entityName }}</span>
                  <span class="nom-curation__item-meta">
                    {{ item.entityType }} by {{ item.authorName }} · {{ item.submittedDate | date:'mediumDate' }}
                  </span>
                </div>
                <span class="nom-curation__item-status">{{ item.status }}</span>
              </div>

              @if (expandedId() === item.id) {
                <div class="nom-curation__item-actions">
                  <mat-form-field appearance="outline" class="nom-curation__notes-field">
                    <mat-label>Feedback Notes</mat-label>
                    <textarea matInput [(ngModel)]="feedbackNotes" rows="2" placeholder="Optional notes for the author..."></textarea>
                  </mat-form-field>
                  <div class="nom-curation__action-buttons">
                    <button mat-flat-button (click)="approve(item)" [disabled]="processing()">
                      <mat-icon>check</mat-icon>
                      Approve
                    </button>
                    <button mat-stroked-button (click)="requestRevision(item)" [disabled]="processing()">
                      <mat-icon>edit_note</mat-icon>
                      Request Revision
                    </button>
                    <button mat-button class="nom-btn--destructive" (click)="reject(item)" [disabled]="processing()">
                      <mat-icon>close</mat-icon>
                      Reject
                    </button>
                    <span style="flex:1"></span>
                    <button mat-button (click)="expandedId.set(null)">Cancel</button>
                  </div>
                </div>
              } @else {
                <button mat-stroked-button (click)="expandedId.set(item.id)" class="nom-curation__review-btn">
                  Review
                </button>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-curation__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-12;
    }

    .nom-curation__list {
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-3;
    }

    .nom-curation__item {
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: vars.$nom-border-radius-lg;
      padding: vars.$spacing-4;
      background: var(--mat-sys-surface);
    }

    .nom-curation__item-header {
      display: flex;
      align-items: center;
      gap: vars.$spacing-3;
    }

    .nom-curation__item-icon {
      color: var(--mat-sys-primary);
      flex-shrink: 0;
    }

    .nom-curation__item-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-1;
    }

    .nom-curation__item-name {
      font-weight: vars.$font-weight-semibold;
      color: var(--mat-sys-on-surface);
    }

    .nom-curation__item-meta {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
    }

    .nom-curation__item-status {
      font-size: vars.$font-size-xs;
      padding: vars.$spacing-1 vars.$spacing-2;
      border-radius: vars.$nom-border-radius-pill;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
      white-space: nowrap;
    }

    .nom-curation__review-btn {
      margin-top: vars.$spacing-3;
    }

    .nom-curation__item-actions {
      margin-top: vars.$spacing-3;
      padding-top: vars.$spacing-3;
      border-top: 1px solid var(--mat-sys-outline-variant);
    }

    .nom-curation__notes-field {
      width: 100%;
    }

    .nom-curation__action-buttons {
      display: flex;
      align-items: center;
      gap: vars.$spacing-2;
      flex-wrap: wrap;
    }
  `],
})
export class CurationQueue implements OnInit {
  private adminService = inject(AdminService);
  private loadingService = inject(LoadingService);

  items = signal<CurationQueueItem[]>([]);
  loading = signal(true);
  processing = signal(false);
  errorMessage = signal('');
  expandedId = signal<number | null>(null);
  feedbackNotes = '';

  ngOnInit(): void {
    this.loadQueue();
  }

  loadQueue(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.adminService.getCurationQueue().pipe(
      this.loadingService.loading('Loading curation queue...')
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
      feedbackNotes: this.feedbackNotes,
    }).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes = '';
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
      feedbackNotes: this.feedbackNotes,
    }).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes = '';
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
      feedbackNotes: this.feedbackNotes,
    }).subscribe({
      next: () => {
        this.items.update(list => list.filter(i => i.id !== item.id));
        this.expandedId.set(null);
        this.feedbackNotes = '';
        this.processing.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to reject item.');
        this.processing.set(false);
      },
    });
  }
}
