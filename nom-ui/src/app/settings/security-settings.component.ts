import { Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TwoFactorService, TwoFactorStatus } from '../core/services/two-factor.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-security-settings',
  imports: [
    RouterLink,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './security-settings.component.html',
  styleUrl: './security-settings.component.scss',
})
export class SecuritySettings implements OnInit {
  private twoFactorService = inject(TwoFactorService);
  private loadingService = inject(LoadingService);

  // State
  loading = signal(true);
  error = signal('');
  success = signal('');

  // 2FA status
  status = signal<TwoFactorStatus | null>(null);

  // Setup flow
  setupMode = signal(false);
  sharedKey = signal('');
  authenticatorUri = signal('');
  verifyCode = '';

  // Recovery codes
  recoveryCodes = signal<string[]>([]);
  showRecoveryCodes = signal(false);

  // Disable flow
  disableMode = signal(false);
  disableCode = '';

  ngOnInit(): void {
    this.loadStatus();
  }

  private loadStatus(): void {
    this.twoFactorService.getStatus().subscribe({
      next: (status) => {
        this.status.set(status);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load 2FA status.');
        this.loading.set(false);
      },
    });
  }

  onSetup(): void {
    this.error.set('');
    this.success.set('');
    this.twoFactorService.setup().pipe(
      this.loadingService.loading('Setting up authenticator...'),
    ).subscribe({
      next: (result) => {
        this.sharedKey.set(result.sharedKey);
        this.authenticatorUri.set(result.authenticatorUri);
        this.setupMode.set(true);
      },
      error: () => this.error.set('Failed to set up authenticator.'),
    });
  }

  onVerify(): void {
    if (!this.verifyCode.trim()) return;
    this.error.set('');

    this.twoFactorService.verify(this.verifyCode).pipe(
      this.loadingService.loading('Verifying code...'),
    ).subscribe({
      next: (result) => {
        this.setupMode.set(false);
        this.recoveryCodes.set(result.recoveryCodes);
        this.showRecoveryCodes.set(true);
        this.success.set('Two-factor authentication has been enabled.');
        this.verifyCode = '';
        this.loadStatus();
      },
      error: () => this.error.set('Invalid verification code. Please try again.'),
    });
  }

  onStartDisable(): void {
    this.disableMode.set(true);
    this.error.set('');
    this.success.set('');
  }

  onDisable(): void {
    if (!this.disableCode.trim()) return;
    this.error.set('');

    this.twoFactorService.disable(this.disableCode).pipe(
      this.loadingService.loading('Disabling 2FA...'),
    ).subscribe({
      next: () => {
        this.disableMode.set(false);
        this.disableCode = '';
        this.success.set('Two-factor authentication has been disabled.');
        this.loadStatus();
      },
      error: () => this.error.set('Invalid verification code. Please try again.'),
    });
  }

  onCancelSetup(): void {
    this.setupMode.set(false);
    this.sharedKey.set('');
    this.authenticatorUri.set('');
    this.verifyCode = '';
  }

  onCancelDisable(): void {
    this.disableMode.set(false);
    this.disableCode = '';
  }

  onCopyKey(): void {
    navigator.clipboard.writeText(this.sharedKey().replace(/\s/g, ''));
    this.success.set('Key copied to clipboard.');
  }

  onCopyRecoveryCodes(): void {
    const codes = this.recoveryCodes().join('\n');
    navigator.clipboard.writeText(codes);
    this.success.set('Recovery codes copied to clipboard.');
  }

  onDismissRecoveryCodes(): void {
    this.showRecoveryCodes.set(false);
    this.recoveryCodes.set([]);
  }
}
