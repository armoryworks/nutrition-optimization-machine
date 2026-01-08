// File: nom-ui/src/app/app.component.ts

import { Component, OnInit, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { ThemeService } from 'angular-material-wrap';
import { AuthService } from './auth/auth.service';
import { AuthManagerService } from './utilities/services/auth-manager.service';
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
  authManagerService = inject(AuthManagerService);
  private router = inject(Router);

  isDarkTheme = false;
  isLoggedIn$ = this.authService.isLoggedIn$;
  showLoginPopover = false;

  constructor() {
  }

  ngOnInit(): void {
    // Ensure theme is applied on initialization
    this.themeService.themeChanges$.subscribe(theme => {
      this.isDarkTheme = theme === 'dark';
      // Force apply the theme class to body
      document.body.classList.remove('light-theme', 'dark-theme');
      document.body.classList.add(theme === 'dark' ? 'dark-theme' : 'light-theme');
      document.documentElement.setAttribute('data-theme', theme);
    });

    // Force dark theme on first load if no theme is set
    const savedTheme = localStorage.getItem('theme');
    if (!savedTheme) {
      this.themeService.setTheme('dark'); // Set dark theme as default
    }

    // Force check user logged in status to load claims
    this.authManagerService.checkUserLoggedInStatus();
  }

  toggleTheme(): void {
    const currentTheme = this.themeService.getCurrentTheme();
    this.themeService.setTheme(currentTheme === 'dark' ? 'light' : 'dark');
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