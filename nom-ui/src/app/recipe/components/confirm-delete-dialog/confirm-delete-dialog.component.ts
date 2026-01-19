import { Component, inject } from '@angular/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { AmwButtonComponent } from 'angular-material-wrap';

@Component({
    selector: 'app-confirm-delete-dialog',
    template: `
        <h2 mat-dialog-title>Confirm Delete</h2>
        <mat-dialog-content>{{ data.message }}</mat-dialog-content>
        <mat-dialog-actions align="end">
            <amw-button variant="text" (click)="onCancel()">Cancel</amw-button>
            <amw-button variant="filled" color="warn" (click)="onConfirm()">Delete</amw-button>
        </mat-dialog-actions>
    `,
    standalone: true,
    imports: [MatDialogModule, AmwButtonComponent]
})
export class ConfirmDeleteDialogComponent {
    private dialogRef = inject(MatDialogRef<ConfirmDeleteDialogComponent>);
    data = inject<{ message: string }>(MAT_DIALOG_DATA);

    onCancel(): void {
        this.dialogRef.close(false);
    }

    onConfirm(): void {
        this.dialogRef.close(true);
    }
}
