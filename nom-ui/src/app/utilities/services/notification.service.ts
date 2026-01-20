import { Injectable, inject } from '@angular/core';
import { AmwNotificationService } from 'angular-material-wrap';

@Injectable({
  providedIn: 'root',
})
export class NotificationService {
  private amwNotificationService = inject(AmwNotificationService);

  /**
   * Displays a success notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 3000ms.
   */
  success(message: string, duration = 3000): void {
    this.amwNotificationService.success('Success', message, { duration });
  }

  /**
   * Displays an info notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 3000ms.
   */
  info(message: string, duration = 3000): void {
    this.amwNotificationService.info('Info', message, { duration });
  }

  /**
   * Displays a warning notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 5000ms.
   */
  warning(message: string, duration = 5000): void {
    this.amwNotificationService.warning('Warning', message, { duration });
  }

  /**
   * Displays an error notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 5000ms.
   */
  error(message: string, duration = 5000): void {
    this.amwNotificationService.error('Error', message, { duration });
  }
}
