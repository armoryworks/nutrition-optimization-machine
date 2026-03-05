import { Component } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export type ShuffleConfirmResult = 'empty' | 'replace' | undefined;

@Component({
  selector: 'nom-shuffle-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Shuffle meals</h2>
    <mat-dialog-content>
      <p>Some meals already have recipes assigned. What would you like to do?</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-stroked-button [mat-dialog-close]="'empty'">Fill empty only</button>
      <button mat-flat-button [mat-dialog-close]="'replace'">Replace all</button>
    </mat-dialog-actions>
  `,
})
export class ShuffleConfirmDialog {}
