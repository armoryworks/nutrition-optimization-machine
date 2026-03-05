import { Component, inject, OnInit } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { CookbookResponseModel } from '../core/models/cookbook.model';

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
  template: `
    <h2 mat-dialog-title>{{ data?.cookbook ? 'Edit Cookbook' : 'New Cookbook' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="nom-cookbook-form">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" maxlength="255" data-testid="cookbook-name-input">
          @if (form.controls.name.hasError('required')) {
            <mat-error>Name is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Description</mat-label>
          <textarea matInput formControlName="description" rows="3" maxlength="2047"
                    data-testid="cookbook-description-input"></textarea>
        </mat-form-field>

        <mat-slide-toggle formControlName="isPublic" data-testid="cookbook-public-toggle">
          Public cookbook
        </mat-slide-toggle>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button
              [disabled]="form.invalid"
              (click)="onSave()"
              data-testid="cookbook-save-btn">
        Save
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .nom-cookbook-form {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-width: 360px;
      padding-top: 0.5rem;
    }
  `],
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
