import { Directive, Input, HostListener, inject, ElementRef, OnDestroy } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { ValidationTooltipService } from '../services/validation-tooltip.service';

/**
 * Directive that shows a validation tooltip when hovering over a disabled submit button.
 * The tooltip displays all current form validation errors.
 *
 * Usage:
 *   <amw-button [nomValidationTooltip]="myForm" [disabled]="myForm.invalid">
 *     Submit
 *   </amw-button>
 */
@Directive({
  selector: '[nomValidationTooltip]',
  standalone: true,
})
export class ValidationTooltipDirective implements OnDestroy {
  @Input('nomValidationTooltip') form!: FormGroup;

  private tooltipService = inject(ValidationTooltipService);
  private el = inject(ElementRef);

  @HostListener('mouseenter')
  onMouseEnter(): void {
    if (this.form?.invalid) {
      this.tooltipService.show(this.form, this.el.nativeElement);
    }
  }

  @HostListener('mouseleave')
  onMouseLeave(): void {
    this.tooltipService.hide();
  }

  @HostListener('focus')
  onFocus(): void {
    if (this.form?.invalid) {
      this.tooltipService.show(this.form, this.el.nativeElement);
    }
  }

  @HostListener('blur')
  onBlur(): void {
    this.tooltipService.hide();
  }

  ngOnDestroy(): void {
    this.tooltipService.hide();
  }
}
