import { WritableSignal } from '@angular/core';

/**
 * Standard async state signals used across components.
 */
export interface AsyncState {
  isLoading: WritableSignal<boolean>;
  error: WritableSignal<string | null>;
}

/**
 * Extended async state with submission tracking for forms.
 */
export interface FormAsyncState extends AsyncState {
  isSubmitting: WritableSignal<boolean>;
}
