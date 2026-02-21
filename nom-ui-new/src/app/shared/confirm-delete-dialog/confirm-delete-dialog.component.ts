import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface ConfirmDeleteDialogData {
  title: string;
  message: string;
  confirmText?: string;
}

@Component({
  selector: 'nom-confirm-delete-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button class="nom-btn--destructive" [mat-dialog-close]="true">
        {{ data.confirmText || 'Delete' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDeleteDialog {
  data = inject<ConfirmDeleteDialogData>(MAT_DIALOG_DATA);
}
