import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export type ShuffleConfirmResult = 'empty' | 'replace' | undefined;

@Component({
  selector: 'nom-shuffle-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './shuffle-confirm-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShuffleConfirmDialog {}
