import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TwoFactorSetup {
  sharedKey: string;
  authenticatorUri: string;
}

export interface TwoFactorRecoveryCodes {
  recoveryCodes: string[];
}

export interface TwoFactorStatus {
  isEnabled: boolean;
  hasAuthenticator: boolean;
  recoveryCodesLeft: number;
}

@Injectable({ providedIn: 'root' })
export class TwoFactorService {
  private http = inject(HttpClient);

  getStatus(): Observable<TwoFactorStatus> {
    return this.http.get<TwoFactorStatus>('/api/auth/2fa/status');
  }

  setup(): Observable<TwoFactorSetup> {
    return this.http.post<TwoFactorSetup>('/api/auth/2fa/setup', {});
  }

  verify(code: string): Observable<TwoFactorRecoveryCodes> {
    return this.http.post<TwoFactorRecoveryCodes>('/api/auth/2fa/verify', { code });
  }

  disable(code: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/auth/2fa/disable', { code });
  }

  generateRecoveryCodes(code: string): Observable<TwoFactorRecoveryCodes> {
    return this.http.post<TwoFactorRecoveryCodes>('/api/auth/2fa/recovery-codes', { code });
  }
}
