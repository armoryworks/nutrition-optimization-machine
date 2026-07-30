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
import { Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { LoginPopover } from './login-popover/login-popover.component';
import { UserMenu } from './user-menu/user-menu.component';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'nom-header',
  imports: [RouterLink, MatIconModule, MatButtonModule, LoginPopover, UserMenu],
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

  loginPopoverOpen = signal(false);
  userMenuOpen = signal(false);

  userInitial = computed(() => {
    const name = this.authService.username();
    return name ? name.charAt(0).toUpperCase() : 'U';
  });

  toggleLoginPopover(): void {
    this.loginPopoverOpen.update((v) => !v);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
  }

  onEscape(): void {
    if (this.userMenuOpen()) this.closeUserMenu();
    if (this.loginPopoverOpen()) this.loginPopoverOpen.set(false);
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
      this.router.navigate(['/search'], { queryParams: { q: query } });
    }
  }
}
