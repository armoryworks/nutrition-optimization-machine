import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LoginPopover } from '../layout/header/login-popover/login-popover.component';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';

/**
 * Standalone sign-in page at /login: a chrome-less login screen; after
 * sign-in the user is routed to the screen appropriate for their state
 * (onboarding vs home).
 *
 * The marketing site's sign-in popover does not use this page — it mounts the
 * <nom-login-embed> custom element directly (see login-embed.component.ts).
 */
@Component({
  selector: 'nom-login-page',
  imports: [LoginPopover],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage implements OnInit {
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    // Already signed in: skip the form entirely and land them on the right screen.
    if (this.authService.isLoggedIn()) {
      this.resolveDestinationAndGo();
    }
  }

  onLoggedIn(): void {
    this.resolveDestinationAndGo();
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
