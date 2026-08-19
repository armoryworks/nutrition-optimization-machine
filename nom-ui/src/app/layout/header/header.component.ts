import {
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { LoginPopover } from './login-popover/login-popover.component';
import { UserMenu } from './user-menu/user-menu.component';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'nom-header',
  imports: [NgTemplateOutlet, RouterLink, MatIconModule, MatButtonModule, LoginPopover, UserMenu],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown.escape)': 'onEscape()',
  },
})
export class Header {
  private router = inject(Router);
  private authService = inject(AuthService);
  private elementRef = inject<ElementRef<HTMLElement>>(ElementRef);

  isLoggedIn = input(false);
  isDarkTheme = input(true);
  themeToggle = output<void>();
  navToggle = output<void>();
  /** Phones only: opens the context sidebar (replaces the floating PANEL tab). */
  sidebarToggle = output<void>();

  loginPopoverOpen = signal(false);
  userMenuOpen = signal(false);
  mobileSearchOpen = signal(false);

  /** Marketing site fronting this instance (NOM_UI_CONFIG, admin-controlled). */
  marketingSite: string =
    (typeof window !== 'undefined' && (window as unknown as { NOM_UI_CONFIG?: { marketingSite?: string } }).NOM_UI_CONFIG?.marketingSite) || '';

  /** Logged-out visitors follow the brand back to the marketing site;
   *  logged-in users go to their own dashboard. */
  brandExternal = computed(() => !this.isLoggedIn() && this.marketingSite !== '');

  userInitial = computed(() => {
    const name = this.authService.username();
    return name ? name.charAt(0).toUpperCase() : 'U';
  });

  toggleLoginPopover(): void {
    this.loginPopoverOpen.update((v) => !v);
  }

  toggleMobileSearch(): void {
    this.mobileSearchOpen.update((v) => !v);
    if (this.mobileSearchOpen()) {
      // The flyout renders after this tick; focus once it exists.
      queueMicrotask(() =>
        this.elementRef.nativeElement
          .querySelector<HTMLInputElement>('[data-testid="header-mobile-search-input"]')
          ?.focus(),
      );
    }
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
  }

  onEscape(): void {
    if (this.userMenuOpen()) this.closeUserMenu();
    if (this.loginPopoverOpen()) this.loginPopoverOpen.set(false);
    if (this.mobileSearchOpen()) this.mobileSearchOpen.set(false);
  }

  /** Close the user menu and return focus to the avatar trigger. */
  closeUserMenu(): void {
    this.userMenuOpen.set(false);
    this.elementRef.nativeElement
      .querySelector<HTMLElement>('[data-testid="header-avatar-btn"]')
      ?.focus();
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    const query = input.value.trim();
    if (query) {
      this.mobileSearchOpen.set(false);
      this.router.navigate(['/search'], { queryParams: { q: query } });
    }
  }
}
