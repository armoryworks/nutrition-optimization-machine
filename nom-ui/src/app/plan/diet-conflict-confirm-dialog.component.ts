import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RecipeDietMatchModel } from '../core/models/recipe.model';
import { DietConflictsPipe } from '../core/utils/diet-conflict';

export interface DietConflictConfirmDialogData {
  recipeName: string;
  conflicts: RecipeDietMatchModel[];
}

export type DietConflictConfirmDialogResult = boolean | undefined;

/**
 * Confirmation before planning a recipe that conflicts with a severe restriction
 * (allergy, celiac): adding it is never a one-tap action, and Cancel is the default.
 */
@Component({
  selector: 'nom-diet-conflict-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, DietConflictsPipe],
  templateUrl: './diet-conflict-confirm-dialog.component.html',
  styleUrl: './diet-conflict-confirm-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DietConflictConfirmDialog {
  data = inject<DietConflictConfirmDialogData>(MAT_DIALOG_DATA);
}
