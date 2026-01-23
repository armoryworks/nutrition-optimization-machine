import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap, filter } from 'rxjs/operators';
import { EventBusService } from './event-bus.service';

import { UserClaim } from './user-claim.interface';
import { UserInfo } from './user-info.interface';

@Injectable({
    providedIn: 'root'
})
export class UserInfoService {
    private http = inject(HttpClient);
    private eventBus = inject(EventBusService);

    private readonly apiUrl = 'api/UserInfo';
    private currentUserInfo = signal<UserInfo | null>(null);

    constructor() {
        // Listen to authentication events
        this.eventBus.events$.pipe(
            filter(event => event.type === 'auth:claims-loaded')
        ).subscribe(() => {
            this.loadUserClaims();
        });

        this.eventBus.events$.pipe(
            filter(event => event.type === 'auth:login')
        ).subscribe(() => {
            this.loadUserClaims();
        });

        this.eventBus.events$.pipe(
            filter(event => event.type === 'auth:logout')
        ).subscribe(() => {
            this.clearUserInfo();
        });
    }

    getCurrentUserInfo(): Observable<UserInfo> {
        return this.http.get<UserInfo>(`${this.apiUrl}/current`).pipe(
            tap(userInfo => {
                this.currentUserInfo.set(userInfo);
            })
        );
    }

    getUserClaims(): Observable<UserClaim[]> {
        return this.http.get<UserClaim[]>(`${this.apiUrl}/claims`);
    }

    hasClaim(claimType: string, claimValue?: string): Observable<{ hasClaim: boolean }> {
        let url = `${this.apiUrl}/has-claim?claimType=${encodeURIComponent(claimType)}`;
        if (claimValue) {
            url += `&claimValue=${encodeURIComponent(claimValue)}`;
        }
        return this.http.get<{ hasClaim: boolean }>(url);
    }

    getCurrentUserInfoValue(): UserInfo | null {
        return this.currentUserInfo();
    }

    // For components that need the signal directly
    getCurrentUserInfoSignal() {
        return this.currentUserInfo.asReadonly();
    }

    hasClaimSync(claimType: string, claimValue?: string): boolean {
        const userInfo = this.currentUserInfo();
        if (!userInfo) return false;

        if (claimValue) {
            return userInfo.claims.some(claim => claim.type === claimType && claim.value === claimValue);
        } else {
            return userInfo.claims.some(claim => claim.type === claimType);
        }
    }

    hasCurationPermission(): boolean {
        return this.hasClaimSync('CanManageCuration');
    }

    hasUserRoleManagementPermission(): boolean {
        return this.hasClaimSync('CanManageUserRoles');
    }

    getPersonId(): number | null {
        const userInfo = this.currentUserInfo();
        return userInfo?.personId || null;
    }

    getHouseholdId(): number {
        const userInfo = this.currentUserInfo();
        // Return household ID if available, otherwise default to 1
        return userInfo?.householdId || 1;
    }

    clearUserInfo(): void {
        this.currentUserInfo.set(null);
    }

    private loadUserClaims(): void {
        this.getCurrentUserInfo().subscribe({
            next: (userInfo) => {
                this.currentUserInfo.set(userInfo);
                // Emit event to notify other services about user info update
                this.eventBus.emitUserInfoUpdated(userInfo);
            },
            error: (error) => {
                console.error('Error loading user claims:', error);
                this.clearUserInfo();
            }
        });
    }
} 