import { Component, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { LoginPopover } from './login-popover/login-popover.component';

@Component({
  selector: 'nom-header',
  imports: [MatIconModule, MatButtonModule, LoginPopover],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class Header {
  private router = inject(Router);

  isLoggedIn = input(false);
  isDarkTheme = input(true);
  themeToggle = output<void>();
  logoutClick = output<void>();

  loginPopoverOpen = signal(false);

  toggleLoginPopover(): void {
    this.loginPopoverOpen.update(v => !v);
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    const query = input.value.trim();
    if (query) {
      this.router.navigate(['/search'], { queryParams: { q: query } });
    }
  }
}
