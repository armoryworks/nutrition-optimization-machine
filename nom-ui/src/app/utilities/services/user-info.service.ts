import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
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
    private currentUserInfo = new BehaviorSubject<UserInfo | null>(null);

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
                this.currentUserInfo.next(userInfo);
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
        return this.currentUserInfo.value;
    }

    getCurrentUserInfoObservable(): Observable<UserInfo | null> {
        return this.currentUserInfo.asObservable();
    }

    hasClaimSync(claimType: string, claimValue?: string): boolean {
        const userInfo = this.currentUserInfo.value;
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
        const userInfo = this.currentUserInfo.value;
        return userInfo?.personId || null;
    }

    clearUserInfo(): void {
        this.currentUserInfo.next(null);
    }

    private loadUserClaims(): void {
        this.getCurrentUserInfo().subscribe({
            next: (userInfo) => {
                this.currentUserInfo.next(userInfo);
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