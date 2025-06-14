import { Injectable } from '@angular/core';
import { MatSnackBar, MatSnackBarConfig } from '@angular/material/snack-bar';

@Injectable({
  providedIn: 'root',
})
export class NotificationService {
  constructor(private snackBar: MatSnackBar) {}

  /**
   * Displays a success notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 3000ms.
   * @param action Optional action text (e.g., 'Dismiss', 'Close'). Defaults to 'Close'.
   */
  success(
    message: string,
    duration: number = 3000,
    action: string = 'Close'
  ): void {
    const config: MatSnackBarConfig = {
      duration: duration,
      panelClass: ['snackbar-success'], // Custom CSS class for success theme
      horizontalPosition: 'center', // Position the snackbar horizontally in the center
      verticalPosition: 'bottom', // Position the snackbar vertically at the bottom
    };
    this.snackBar.open(message, action, config);
  }

  /**
   * Displays an info notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 3000ms.
   * @param action Optional action text (e.g., 'Dismiss', 'Close'). Defaults to 'Close'.
   */
  info(
    message: string,
    duration: number = 3000,
    action: string = 'Close'
  ): void {
    const config: MatSnackBarConfig = {
      duration: duration,
      panelClass: ['snackbar-info'], // Custom CSS class for info theme
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    };
    this.snackBar.open(message, action, config);
  }

  /**
   * Displays a warning notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 5000ms.
   * @param action Optional action text (e.g., 'Dismiss', 'Close'). Defaults to 'Close'.
   */
  warning(
    message: string,
    duration: number = 5000,
    action: string = 'Close'
  ): void {
    const config: MatSnackBarConfig = {
      duration: duration,
      panelClass: ['snackbar-warning'], // Custom CSS class for warning theme
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    };
    this.snackBar.open(message, action, config);
  }

  /**
   * Displays an error notification.
   * @param message The message to display.
   * @param duration Optional duration in milliseconds. Defaults to 5000ms.
   * @param action Optional action text (e.g., 'Dismiss', 'Close'). Defaults to 'Close'.
   */
  error(
    message: string,
    duration: number = 5000,
    action: string = 'Close'
  ): void {
    const config: MatSnackBarConfig = {
      duration: duration,
      panelClass: ['snackbar-error'], // Custom CSS class for error theme
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    };
    this.snackBar.open(message, action, config);
  }
}
