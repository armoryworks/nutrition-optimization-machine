import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

export interface AppEvent {
    type: string;
    data?: unknown;
}

@Injectable({
    providedIn: 'root',
})
export class EventBusService {
    private eventBus = new Subject<AppEvent>();

    public events$ = this.eventBus.asObservable();

    emit(event: AppEvent): void {
        this.eventBus.next(event);
    }

    emitLogin(): void {
        this.emit({ type: 'auth:login' });
    }

    emitLogout(): void {
        this.emit({ type: 'auth:logout' });
    }

    emitClaimsLoaded(): void {
        this.emit({ type: 'auth:claims-loaded' });
    }

    emitUserInfoUpdated(userInfo: unknown): void {
        this.emit({ type: 'user:info-updated', data: userInfo });
    }
} 