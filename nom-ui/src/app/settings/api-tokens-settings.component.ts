import { Component, inject, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ApiTokenService } from '../core/services/api-token.service';
import { ApiToken, CreatedApiToken } from '../core/models/api-token.model';

@Component({
  selector: 'nom-api-tokens-settings',
  imports: [
    DatePipe,
    RouterLink,
    ReactiveFormsModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './api-tokens-settings.component.html',
  styleUrl: './api-tokens-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiTokensSettings implements OnInit {
  private apiTokenService = inject(ApiTokenService);
  private destroyRef = inject(DestroyRef);

  loading = signal(true);
  error = signal('');
  tokens = signal<ApiToken[]>([]);

  creating = signal(false);
  createdToken = signal<CreatedApiToken | null>(null);
  copyStatus = signal<'idle' | 'copied' | 'failed'>('idle');

  confirmingId = signal<string | null>(null);
  revokingId = signal<string | null>(null);

  form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(255), (c) => (c.value ?? '').trim().length >= 3 ? null : { minlength: true }],
    }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  ngOnInit(): void {
    this.loadTokens();
  }

  loadTokens(): void {
    this.loading.set(true);
    this.apiTokenService.getTokens().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (tokens) => {
        this.tokens.set(tokens);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load your API tokens.');
        this.loading.set(false);
      },
    });
  }

  onCreate(): void {
    if (this.form.invalid || this.creating()) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, description } = this.form.getRawValue();
    this.error.set('');
    this.creating.set(true);
    this.apiTokenService
      .createToken({ name: name.trim(), description: description.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.creating.set(false);
          this.createdToken.set(created);
          this.copyStatus.set('idle');
          this.form.reset();
          this.loadTokens();
        },
        error: () => {
          this.creating.set(false);
          this.error.set('Failed to create the API token.');
        },
      });
  }

  async onCopy(): Promise<void> {
    const token = this.createdToken()?.token;
    if (!token) return;
    try {
      await navigator.clipboard.writeText(token);
      this.copyStatus.set('copied');
    } catch {
      this.copyStatus.set('failed');
    }
  }

  onDismissSecret(): void {
    this.createdToken.set(null);
    this.copyStatus.set('idle');
  }

  onStartRevoke(token: ApiToken): void {
    this.error.set('');
    this.confirmingId.set(token.id);
  }

  onCancelRevoke(): void {
    this.confirmingId.set(null);
  }

  onConfirmRevoke(token: ApiToken): void {
    this.error.set('');
    this.revokingId.set(token.id);
    this.apiTokenService.revokeToken(token.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.revokingId.set(null);
        this.confirmingId.set(null);
        this.loadTokens();
      },
      error: () => {
        this.revokingId.set(null);
        this.error.set(`Failed to revoke "${token.name}".`);
      },
    });
  }
}
