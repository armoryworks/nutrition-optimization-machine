// File: nom-ui/src/app/app.component.ts

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { ThemeService } from './services/theme.service';
import { AuthService } from './auth/auth.service';
import { Router } from '@angular/router';
import { LoginComponent } from './auth/login/login.component';

@Component({
  selector: 'nom-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    LoginComponent
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  private router = inject(Router);

  isDarkTheme = false;
  isLoggedIn$ = this.authService.isLoggedIn$;
  showLoginPopover = false;

  constructor() {
  }

  ngOnInit(): void {
    // Ensure dark theme is applied on initialization
    this.themeService.isDarkTheme$.subscribe(isDark => {
      this.isDarkTheme = isDark;
      // Force apply the theme class to body
      document.body.classList.remove('light-theme', 'dark-theme');
      document.body.classList.add(isDark ? 'dark-theme' : 'light-theme');
      document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    });

    // Force dark theme on first load if no theme is set
    const savedTheme = localStorage.getItem('theme');
    if (!savedTheme) {
      this.themeService.toggleTheme(); // This will set dark theme as default
    }
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  openLoginPopover(): void {
    this.showLoginPopover = true;
  }

  closeLoginPopover(): void {
    this.showLoginPopover = false;
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