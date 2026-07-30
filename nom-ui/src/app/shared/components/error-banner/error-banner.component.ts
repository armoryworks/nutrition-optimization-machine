import { Component, input, output, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * Shared error display: announced to assistive tech via role="alert" and
 * offering a retry action when the caller listens for one.
 */
@Component({
  selector: 'nom-error-banner',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './error-banner.component.html',
  styleUrl: './error-banner.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorBanner {
  message = input.required<string>();
  retryLabel = input('Try Again');
  retry = output<void>();
  /** Rendered only when the host listens: set showRetry when binding (retry). */
  showRetry = input(false);
}
