// File: nom-ui/src/app/common/components/confirmation-dialog/confirmation-dialog.component.ts

import { Component, inject } from '@angular/core';

import { MatButtonModule } from '@angular/material/button';
import {
  MatDialogModule,
  MatDialogRef,
  MAT_DIALOG_DATA,
} from '@angular/material/dialog';

export interface ConfirmationDialogData {
  title: string;
  message: string;
  confirmButtonText?: string;
  cancelButtonText?: string;
}

@Component({
  selector: 'nom-confirmation-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './confirmation-dialog.component.html',
})
export class ConfirmationDialogComponent {
  dialogRef = inject<MatDialogRef<ConfirmationDialogComponent>>(MatDialogRef);
  data = inject<ConfirmationDialogData>(MAT_DIALOG_DATA);

  title: string;
  message: string;
  confirmButtonText: string;
  cancelButtonText: string;



  constructor() {
    const data = this.data;

    this.title = data.title;
    this.message = data.message;
    this.confirmButtonText = data.confirmButtonText || 'Confirm';
    this.cancelButtonText = data.cancelButtonText || 'Cancel';
  }

  onConfirm(): void {
    // Close the dialog, return true
    this.dialogRef.close(true);
  }

  onDismiss(): void {
    // Close the dialog, return false
    this.dialogRef.close(false);
  }
}
