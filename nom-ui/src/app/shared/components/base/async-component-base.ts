import { inject, signal, OnDestroy } from '@angular/core';
import { Subject, Observable, EMPTY } from 'rxjs';
import { takeUntil, finalize, catchError } from 'rxjs/operators';
import { NotificationService } from '../../../utilities/services/notification.service';

export interface ExecuteAsyncOptions<T> {
  onSuccess?: (result: T) => void;
  onError?: (error: any) => void;
  errorMessage: string;
  /** Show notification on error. Defaults to false. */
  notify?: boolean;
  /** Show notification on success. Defaults to false. */
  successMessage?: string;
}

/**
 * Base class for components with standard async state management.
 *
 * Provides:
 * - isLoading / error / isSubmitting signals
 * - destroy$ subject for RxJS cleanup
 * - executeAsync() helper to reduce boilerplate
 *
 * Usage:
 *   export class MyComponent extends AsyncComponentBase {
 *     loadData(): void {
 *       this.executeAsync(
 *         this.service.getData(),
 *         {
 *           onSuccess: (data) => this.data.set(data),
 *           errorMessage: ERROR_MESSAGES.RECIPE.LOAD_FAILED
 *         }
 *       );
 *     }
 *   }
 */
export abstract class AsyncComponentBase implements OnDestroy {
  protected notificationService = inject(NotificationService);
  protected destroy$ = new Subject<void>();

  isLoading = signal(false);
  error = signal<string | null>(null);
  isSubmitting = signal(false);

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Execute an async operation with automatic loading/error management.
   */
  protected executeAsync<T>(
    source: Observable<T>,
    options: ExecuteAsyncOptions<T>
  ): void {
    this.isLoading.set(true);
    this.error.set(null);

    source.pipe(
      takeUntil(this.destroy$),
      catchError((err) => {
        const message = err?.message || options.errorMessage;
        this.error.set(message);
        console.error(options.errorMessage, err);

        if (options.notify) {
          this.notificationService.error(message);
        }

        options.onError?.(err);
        return EMPTY;
      }),
      finalize(() => this.isLoading.set(false))
    ).subscribe((result) => {
      options.onSuccess?.(result);

      if (options.successMessage) {
        this.notificationService.success(options.successMessage);
      }
    });
  }

  /**
   * Execute a submission (form save, create, etc.) with isSubmitting tracking.
   */
  protected executeSubmit<T>(
    source: Observable<T>,
    options: ExecuteAsyncOptions<T>
  ): void {
    this.isSubmitting.set(true);
    this.error.set(null);

    source.pipe(
      takeUntil(this.destroy$),
      catchError((err) => {
        const message = err?.message || options.errorMessage;
        this.error.set(message);
        console.error(options.errorMessage, err);

        if (options.notify !== false) {
          this.notificationService.error(message);
        }

        options.onError?.(err);
        return EMPTY;
      }),
      finalize(() => this.isSubmitting.set(false))
    ).subscribe((result) => {
      options.onSuccess?.(result);

      if (options.successMessage) {
        this.notificationService.success(options.successMessage);
      }
    });
  }
}
