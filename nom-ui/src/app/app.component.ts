// File: nom-ui/src/app/app.component.ts

import { Component, OnInit, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwButtonComponent, AmwIconButtonComponent, AmwIconComponent, AmwMenuComponent, AmwMenuItemComponent, AmwMenuTriggerForDirective, AmwFullScreenLoadingComponent, AmwValidationTooltipOverlayComponent } from 'angular-material-wrap';
import { ThemeService } from './services/theme.service';
import { AuthService } from './auth/auth.service';
import { AuthManagerService } from './utilities/services/auth-manager.service';
import { Router } from '@angular/router';
import { LoginComponent } from './auth/login/login.component';
import { ValidationTooltipOverlayComponent } from './shared/components/validation-tooltip-overlay/validation-tooltip-overlay.component';

@Component({
  selector: 'nom-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    AmwButtonComponent,
    AmwIconButtonComponent,
    AmwIconComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwMenuTriggerForDirective,
    AmwFullScreenLoadingComponent,
    AmwValidationTooltipOverlayComponent,
    ValidationTooltipOverlayComponent,
    LoginComponent
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  authManagerService = inject(AuthManagerService);
  private router = inject(Router);

  isDarkTheme = computed(() => this.themeService.getCurrentTheme() === 'dark');
  isLoggedIn$ = this.authService.isLoggedIn$;
  showLoginPopover = signal(false);

  constructor() {
  }

  ngOnInit(): void {
    // Force check user logged in status to load claims
    this.authManagerService.checkUserLoggedInStatus();

    // Ensure theme is set to dark by default if not already saved
    const savedTheme = localStorage.getItem('theme');
    if (!savedTheme) {
      this.themeService.setTheme('dark');
    }
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  openLoginPopover(): void {
    this.showLoginPopover.set(true);
  }

  closeLoginPopover(): void {
    this.showLoginPopover.set(false);
  }

  logout(): void {
    this.authService.logout().subscribe({
      next: () => {
        this.closeLoginPopover();
        this.router.navigate(['/home']);
      },
      error: (error) => {
        console.error('Logout failed:', error);
      }
    });
  }
}