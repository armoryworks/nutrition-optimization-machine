import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

import { AppEvent } from './app-event.interface';

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