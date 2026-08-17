import { Component, DestroyRef, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { PlatformFeatureService } from '../core/services/platform-feature.service';
import { PlatformFeatureModel } from '../core/models/platform-feature.model';
import { LoadingService } from '../core/services/loading.service';
import {
  PlatformFeatureConfirmDialog,
  PlatformFeatureConfirmDialogData,
  PlatformFeatureConfirmDialogResult,
} from './platform-feature-confirm-dialog.component';

/**
 * Platform-wide feature switches (kill switches). Flipping one changes what
 * every user of this instance can reach, so each change goes through a
 * confirmation naming the feature and its consequence. The toggle always
 * reflects server state: it reverts when a save fails.
 */
@Component({
  selector: 'nom-platform-features',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    ErrorBanner,
  ],
  templateUrl: './platform-features.component.html',
  styleUrl: './platform-features.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformFeatures implements OnInit {
  private featureService = inject(PlatformFeatureService);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  features = signal<PlatformFeatureModel[]>([]);
  loading = signal(true);
  errorMessage = signal('');
  /** Key of the switch currently being saved (its toggle is locked meanwhile). */
  savingKey = signal<string | null>(null);
  /**
   * Bumped to force the rows (and their toggles) to be recreated. A toggle the
   * user flipped holds that state internally; re-binding the unchanged value
   * would not move it back, so the row is rebuilt from server state instead.
   */
  renderToken = signal(0);

  ngOnInit(): void {
    this.loadFeatures();
  }

  loadFeatures(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.featureService.list().pipe(
      this.loadingService.loading('Loading platform features...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (features) => {
        this.features.set(features);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(
          err instanceof HttpErrorResponse && err.status === 403
            ? 'You do not have permission to manage platform features.'
            : 'Failed to load platform features.',
        );
      },
    });
  }

  /**
   * Confirm first, then save. The toggle is bound to server state, so a
   * cancelled or failed change simply leaves the rendered state untouched.
   */
  onToggle(feature: PlatformFeatureModel, checked: boolean): void {
    if (checked === feature.isEnabled || this.savingKey() != null) {
      this.restore();
      return;
    }

    const dialogRef = this.dialog.open(PlatformFeatureConfirmDialog, {
      width: '460px',
      data: { feature, enabling: checked } as PlatformFeatureConfirmDialogData,
    });

    dialogRef.afterClosed().subscribe((confirmed: PlatformFeatureConfirmDialogResult) => {
      if (confirmed) {
        this.save(feature, checked);
      } else {
        // Revert the visual flip the user just made.
        this.restore();
      }
    });
  }

  private save(feature: PlatformFeatureModel, isEnabled: boolean): void {
    this.savingKey.set(feature.key);
    this.errorMessage.set('');

    this.featureService.set(feature.key, isEnabled).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (updated) => {
        this.savingKey.set(null);
        // Reflect exactly what the server stored.
        this.features.update((list) =>
          list.map((f) => (f.key === updated.key ? updated : f)),
        );
        this.snackBar.open(
          `${updated.key} is now ${updated.isEnabled ? 'on' : 'off'}`,
          'OK',
          { duration: 4000 },
        );
      },
      error: (err) => {
        this.savingKey.set(null);
        this.errorMessage.set(
          err instanceof HttpErrorResponse && err.status === 403
            ? 'You do not have permission to change platform features.'
            : `Failed to change "${feature.key}". The switch is unchanged.`,
        );
        // Revert: the rendered toggle must never claim a state the server rejected.
        this.restore();
      },
    });
  }

  /** Rebuild the rows so bound toggles snap back to server state. */
  private restore(): void {
    this.renderToken.update((n) => n + 1);
  }
}
