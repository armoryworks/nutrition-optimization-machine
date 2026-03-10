import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { PrivacyService } from '../core/services/privacy.service';
import { LoadingService } from '../core/services/loading.service';
import { ConfirmDeleteDialog, ConfirmDeleteDialogData } from '../shared/confirm-delete-dialog/confirm-delete-dialog.component';

@Component({
  selector: 'nom-privacy-settings',
  imports: [RouterLink, MatIconModule, MatButtonModule],
  templateUrl: './privacy-settings.component.html',
  styleUrl: './privacy-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrivacySettings {
  private privacyService = inject(PrivacyService);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);

  exportRequested = signal(false);
  error = signal('');
  success = signal('');

  onExportJson(): void {
    this.exportData('json');
  }

  onExportCsv(): void {
    this.exportData('csv');
  }

  private exportData(format: 'json' | 'csv'): void {
    this.error.set('');
    this.success.set('');

    this.privacyService.requestDataExport({ format }).pipe(
      this.loadingService.loading('Requesting data export...'),
    ).subscribe({
      next: () => {
        this.exportRequested.set(true);
        this.success.set(`Your ${format.toUpperCase()} export has been requested. You'll receive it shortly.`);
      },
      error: () => this.error.set('Failed to request data export.'),
    });
  }

  onDeleteAccount(): void {
    const dialogRef = this.dialog.open(ConfirmDeleteDialog, {
      data: {
        title: 'Delete Account',
        message: 'This will permanently delete your account and all associated data. This action cannot be undone.',
        confirmText: 'Delete My Account',
      } as ConfirmDeleteDialogData,
    });

    dialogRef.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      this.error.set('');
      this.privacyService.requestDataDeletion({ confirm: true }).pipe(
        this.loadingService.loading('Processing account deletion...'),
      ).subscribe({
        next: () => this.success.set('Your account deletion request has been submitted.'),
        error: () => this.error.set('Failed to process account deletion.'),
      });
    });
  }
}
