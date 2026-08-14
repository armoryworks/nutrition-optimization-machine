import { Component, computed, inject, signal, ChangeDetectionStrategy, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DishGroupService } from '../../core/services/dish-group.service';
import { DishGroupModel } from '../../core/models/dish-group.model';

export interface MergeDishGroupDialogData {
  /** The group being retired. */
  source: DishGroupModel;
  /** All groups (the dialog excludes the source itself). */
  groups: DishGroupModel[];
}

/** True when the merge happened (the caller refreshes the list). */
export type MergeDishGroupDialogResult = boolean | undefined;

/**
 * Curation-admin dialog: merge one dish group's recipes into another and
 * retire the source. Confirm stays disabled until a real target is picked.
 */
@Component({
  selector: 'nom-merge-dish-group-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './merge-dish-group-dialog.component.html',
  styleUrl: './merge-dish-group-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MergeDishGroupDialog {
  private dialogRef = inject(MatDialogRef<MergeDishGroupDialog>);
  data: MergeDishGroupDialogData = inject(MAT_DIALOG_DATA);
  private dishGroupService = inject(DishGroupService);
  private destroyRef = inject(DestroyRef);

  searchControl = new FormControl('', { nonNullable: true });
  /** Set only by picking an autocomplete option — typing clears it. */
  target = signal<DishGroupModel | null>(null);
  merging = signal(false);
  errorMessage = signal('');

  /** A group can never be merged into itself. */
  private candidates = computed(() =>
    this.data.groups.filter((g) => g.id !== this.data.source.id));

  filteredCandidates(): DishGroupModel[] {
    const search = this.searchControl.value.trim().toLowerCase();
    const candidates = this.candidates();
    if (!search) return candidates.slice(0, 20);
    return candidates.filter((g) => g.name.includes(search)).slice(0, 20);
  }

  onTargetSelected(event: MatAutocompleteSelectedEvent): void {
    const group = event.option.value as DishGroupModel;
    this.target.set(group);
    this.searchControl.setValue(group.name);
  }

  onSearchInput(): void {
    // Any manual edit invalidates the picked target unless it still matches exactly.
    const value = this.searchControl.value.trim().toLowerCase();
    if (this.target()?.name !== value) {
      this.target.set(null);
    }
  }

  displayFn(): string {
    return '';
  }

  confirm(): void {
    const target = this.target();
    if (!target || this.merging()) return;

    this.merging.set(true);
    this.errorMessage.set('');

    this.dishGroupService.merge(this.data.source.id, target.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.merging.set(false);
        this.dialogRef.close(true as MergeDishGroupDialogResult);
      },
      error: () => {
        this.merging.set(false);
        this.errorMessage.set('Unable to merge the groups. Please try again.');
      },
    });
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }
}
