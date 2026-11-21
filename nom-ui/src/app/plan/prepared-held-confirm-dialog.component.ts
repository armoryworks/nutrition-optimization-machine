import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface PreparedHeldConfirmDialogData {
  foodName: string;
  reason: string;
}

export type PreparedHeldConfirmDialogResult = boolean | undefined;

/**
 * Confirmation before a household with a severe restriction (allergy or medical) adds by hand a
 * prepared meal the contents gate holds for it: the reason is named, adding is never a one-tap
 * action, and Cancel is the default.
 */
@Component({
  selector: 'nom-prepared-held-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './prepared-held-confirm-dialog.component.html',
  styleUrl: './diet-conflict-confirm-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PreparedHeldConfirmDialog {
  data = inject<PreparedHeldConfirmDialogData>(MAT_DIALOG_DATA);
}
