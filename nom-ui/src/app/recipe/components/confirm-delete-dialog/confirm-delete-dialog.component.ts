import { Component, inject, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

@Component({
    selector: 'app-confirm-delete-dialog',
    template: `
        <h2 mat-dialog-title>Confirm Delete</h2>
        <mat-dialog-content>{{ data.message }}</mat-dialog-content>
        <mat-dialog-actions align="end">
            <button mat-button mat-dialog-close>Cancel</button>
            <button mat-raised-button color="warn" [mat-dialog-close]="true">Delete</button>
        </mat-dialog-actions>
    `,
    standalone: true,
    imports: [CommonModule, MatDialogModule, MatButtonModule]
})
export class ConfirmDeleteDialogComponent {
    data = inject<{ message: string }>(MAT_DIALOG_DATA);
}
