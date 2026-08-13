import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  OnDestroy,
  ViewEncapsulation,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LoginPopover } from '../layout/header/login-popover/login-popover.component';
import { AuthService } from '../core/services/auth.service';

/**
 * Absolute origin of the NOM app (e.g. https://nom.nommeal.com). Provided by
 * the element bundle's bootstrap, derived from where the bundle was loaded
 * from; defaults to the current origin for in-app use.
 */
export const APP_ORIGIN = new InjectionToken<string>('APP_ORIGIN', {
  factory: () => window.location.origin,
});

/**
 * Embeddable sign-in popover, published as the <nom-login-embed> custom
 * element (see elements-main.ts). The marketing site mounts it directly in its
 * static page — no iframe — so the page body stays indexable static HTML.
 *
 * Because the element runs on the marketing origin, a successful login lands
 * tokens in the wrong origin's storage. The embed therefore trades its session
 * for a one-time handoff code and redirects to the app origin's /auth/handoff,
 * which redeems the code for the app's own tokens. Internal links (register /
 * forgot password) navigate to the full app.
 */
@Component({
  selector: 'nom-login-embed',
  imports: [LoginPopover],
  template: `
    <div class="nom-login-embed__card">
      <nom-login-popover [deferNavigation]="true" (loggedIn)="onLoggedIn()" (closed)="onClosed()" />
    </div>
  `,
  styleUrl: './login-embed.component.scss',
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginEmbed implements AfterViewInit, OnDestroy {
  private authService = inject(AuthService);
  private appOrigin = inject(APP_ORIGIN);
  private el = inject(ElementRef<HTMLElement>);
  private destroyRef = inject(DestroyRef);

  private readonly anchorBreakout = (event: MouseEvent) => {
    const anchor = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="/"]');
    if (!anchor) return;
    event.preventDefault();
    event.stopPropagation();
    window.location.href = new URL(anchor.getAttribute('href')!, this.appOrigin).toString();
  };

  ngAfterViewInit(): void {
    // Capture-phase so RouterLink never handles register / forgot-password
    // clicks — those must leave the static page for the full app.
    this.el.nativeElement.addEventListener('click', this.anchorBreakout, true);
  }

  ngOnDestroy(): void {
    this.el.nativeElement.removeEventListener('click', this.anchorBreakout, true);
  }

  onLoggedIn(): void {
    this.authService
      .requestHandoffCode()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ code }) => {
          // The marketing origin's token copies are scratch — drop them before
          // leaving. (Best-effort server call; session is cleared synchronously.)
          this.authService.logout().subscribe();
          window.location.href = `${this.appOrigin}/auth/handoff#code=${code}`;
        },
        // Handoff unavailable (e.g. stale bundle against an older API): fall
        // back to the app's login page rather than stranding the user.
        error: () => {
          window.location.href = `${this.appOrigin}/login`;
        },
      });
  }

  onClosed(): void {
    this.el.nativeElement.dispatchEvent(
      new CustomEvent('nom-login-close', { bubbles: true, composed: true }),
    );
  }
}
