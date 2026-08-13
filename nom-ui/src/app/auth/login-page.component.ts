import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LoginPopover } from '../layout/header/login-popover/login-popover.component';
import { AuthService } from '../core/services/auth.service';
import { PersonService } from '../core/services/person.service';

/**
 * Standalone sign-in page at /login.
 *
 * Two modes:
 * - Direct visit: a chrome-less login screen; after sign-in the user is routed
 *   to the screen appropriate for their state (onboarding vs home).
 * - Embedded (?embedded=1): rendered inside the marketing site's login popover
 *   iframe (same-site: nommeal.com embeds nom.nommeal.com, so storage is
 *   unpartitioned). After sign-in — or for an already-signed-in visitor — the
 *   TOP window is navigated into the app, breaking out of the iframe. Internal
 *   links (register / forgot password) also break out to the full app.
 */
@Component({
  selector: 'nom-login-page',
  imports: [LoginPopover],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage implements OnInit, AfterViewInit, OnDestroy {
  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  private el = inject(ElementRef<HTMLElement>);

  readonly embedded =
    new URLSearchParams(window.location.search).get('embedded') === '1' &&
    window.self !== window.top;

  private readonly anchorBreakout = (event: MouseEvent) => {
    if (!this.embedded) return;
    const anchor = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="/"]');
    if (!anchor) return;
    event.preventDefault();
    event.stopPropagation();
    this.navigateTop(anchor.getAttribute('href')!);
  };

  ngOnInit(): void {
    // Already signed in (e.g. popover opened by a logged-in visitor): skip the
    // form entirely and land them on the right screen.
    if (this.authService.isLoggedIn()) {
      this.resolveDestinationAndGo();
    }
  }

  ngAfterViewInit(): void {
    // Capture-phase so RouterLink never handles in-iframe navigation for
    // register / forgot-password links in embedded mode.
    this.el.nativeElement.addEventListener('click', this.anchorBreakout, true);
  }

  ngOnDestroy(): void {
    this.el.nativeElement.removeEventListener('click', this.anchorBreakout, true);
  }

  onLoggedIn(): void {
    this.resolveDestinationAndGo();
  }

  private resolveDestinationAndGo(): void {
    const personId = this.authService.personId();
    if (!personId) {
      this.go('/home');
      return;
    }
    this.personService
      .getOnboardingState(personId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => this.go(state.isComplete ? '/home' : '/onboarding'),
        error: () => this.go('/home'),
      });
  }

  private go(path: string): void {
    if (this.embedded) {
      this.navigateTop(path);
      return;
    }
    this.router.navigateByUrl(path);
  }

  private navigateTop(path: string): void {
    const top = window.top ?? window;
    top.location.href = new URL(path, window.location.origin).toString();
  }
}
