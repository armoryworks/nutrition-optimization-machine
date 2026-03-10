import { Component, inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { CookbookResponseModel } from '../core/models/cookbook-response.model';

export interface CookbookFormDialogData {
  cookbook?: CookbookResponseModel; // null = create mode
}

export interface CookbookFormDialogResult {
  name: string;
  description: string;
  isPublic: boolean;
}

@Component({
  selector: 'nom-cookbook-form-dialog',
  standalone: true,
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    ReactiveFormsModule,
  ],
  templateUrl: './cookbook-form-dialog.component.html',
  styleUrl: './cookbook-form-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CookbookFormDialog implements OnInit {
  private fb = inject(FormBuilder);
  private dialogRef = inject(MatDialogRef<CookbookFormDialog>);
  data = inject<CookbookFormDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(2047)]],
    isPublic: [false],
  });

  ngOnInit(): void {
    if (this.data?.cookbook) {
      this.form.patchValue({
        name: this.data.cookbook.name,
        description: this.data.cookbook.description ?? '',
        isPublic: this.data.cookbook.isPublic,
      });
    }
  }

  onSave(): void {
    if (this.form.valid) {
      const value = this.form.getRawValue();
      const result: CookbookFormDialogResult = {
        name: value.name,
        description: value.description,
        isPublic: value.isPublic,
      };
      this.dialogRef.close(result);
    }
  }
}
