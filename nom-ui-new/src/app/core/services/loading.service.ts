import { Injectable, computed, signal } from '@angular/core';
import { MonoTypeOperatorFunction, defer, finalize } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class LoadingService {
  private messagesMap = signal<Map<string, string>>(new Map());
  private counter = 0;

  readonly messages = computed(() => [...this.messagesMap().values()].slice(0, 5));
  readonly isLoading = computed(() => this.messagesMap().size > 0);

  add(message: string): string {
    const key = `loading-${++this.counter}`;
    this.messagesMap.update(m => new Map(m).set(key, message));
    return key;
  }

  remove(key: string): void {
    this.messagesMap.update(m => {
      const next = new Map(m);
      next.delete(key);
      return next;
    });
  }

  loading<T>(message: string): MonoTypeOperatorFunction<T> {
    return (source) => defer(() => {
      const key = this.add(message);
      return source.pipe(finalize(() => this.remove(key)));
    });
  }
}
