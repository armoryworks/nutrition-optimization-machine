import { Injectable, inject, signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { IFieldError } from '../interfaces/validation.interfaces';
import { ValidationMessageService } from './validation-message.service';

@Injectable({ providedIn: 'root' })
export class ValidationTooltipService {
  private validationMessageService = inject(ValidationMessageService);

  isVisible = signal(false);
  errors = signal<IFieldError[]>([]);
  position = signal({ top: 0, left: 0 });

  show(form: FormGroup, element: HTMLElement): void {
    // Mark all controls as touched so errors surface
    this.markAllTouched(form);

    const fieldErrors = this.validationMessageService.getFieldErrors(form);
    if (fieldErrors.length > 0) {
      const rect = element.getBoundingClientRect();
      this.position.set({
        top: rect.top - 8, // Position above the button
        left: rect.left,
      });
      this.errors.set(fieldErrors);
      this.isVisible.set(true);
    }
  }

  hide(): void {
    this.isVisible.set(false);
  }

  private markAllTouched(form: FormGroup): void {
    Object.values(form.controls).forEach((control) => {
      control.markAsTouched();
      if ((control as FormGroup).controls) {
        this.markAllTouched(control as FormGroup);
      }
    });
  }
}
