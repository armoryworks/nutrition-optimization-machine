import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';

/**
 * Landing route for the cross-origin sign-in handoff. The marketing site's
 * embedded login popover signs the user in on its own origin, then redirects
 * here with a one-time code in the URL fragment (#code=…). We redeem the code
 * for this origin's tokens and route on (onboarding vs home).
 *
 * The code rides the fragment, not the query string, so it never reaches the
 * server or its logs; it is scrubbed from history before the redeem call.
 */
@Component({
  selector: 'nom-auth-handoff',
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="nom-handoff" data-testid="auth-handoff">
      @if (failed()) {
        <p class="nom-handoff__error" data-testid="auth-handoff-error">
          This sign-in link has expired. Redirecting to sign in…
        </p>
      } @else {
        <mat-spinner diameter="32"></mat-spinner>
        <p>Signing you in…</p>
      }
    </div>
  `,
  styles: `
    .nom-handoff {
      min-height: 60dvh;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 16px;
      color: var(--mat-sys-on-surface);
    }
    .nom-handoff__error {
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthHandoff implements OnInit {
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  failed = signal(false);

  ngOnInit(): void {
    const code = new URLSearchParams(window.location.hash.slice(1)).get('code');
    // Scrub the code from the address bar / history before doing anything else.
    history.replaceState(null, '', window.location.pathname);

    if (!code) {
      this.router.navigateByUrl(this.authService.isLoggedIn() ? '/home' : '/login');
      return;
    }

    this.authService
      .redeemHandoffCode(code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.resolveDestinationAndGo(),
        error: () => {
          this.failed.set(true);
          setTimeout(() => this.router.navigateByUrl('/login'), 1500);
        },
      });
  }

  private resolveDestinationAndGo(): void {
    const personId = this.authService.personId();
    if (!personId) {
      this.router.navigateByUrl('/home');
      return;
    }
    this.personService
      .getOnboardingState(personId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => this.router.navigateByUrl(state.isComplete ? '/home' : '/onboarding'),
        error: () => this.router.navigateByUrl('/home'),
      });
  }
}
