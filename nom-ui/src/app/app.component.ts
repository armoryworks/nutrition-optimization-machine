// File: nom-ui/src/app/app.component.ts

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ThemeService } from './services/theme.service';
import { AuthService } from './auth/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'nom-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ReactiveFormsModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatCheckboxModule
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  isDarkTheme = false;
  isLoggedIn$ = this.authService.isLoggedIn$;
  showLoginPopover = false;
  isLoggingIn = false;
  loginForm: FormGroup;

  constructor() {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      rememberMe: [false]
    });
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
    this.loginForm.reset();
  }

  onLoginSubmit(): void {
    if (this.loginForm.valid) {
      this.isLoggingIn = true;
      const { email, password, rememberMe } = this.loginForm.value;

      this.authService.login({
        email,
        password,
        twoFactorCode: '',
        toFactorRecoveryCode: '',
        rememberMe: rememberMe
      }).subscribe({
        next: (response) => {
          this.isLoggingIn = false;
          this.closeLoginPopover();
          // Navigate to dashboard or home after successful login
          this.router.navigate(['/home']);
        },
        error: (error) => {
          this.isLoggingIn = false;
          console.error('Login failed:', error);
          // You could add error handling here (e.g., show error message)
        }
      });
    }
  }

  logout(): void {
    this.authService.logout().subscribe({
      next: () => {
        this.router.navigate(['/home']);
      },
      error: (error) => {
        console.error('Logout failed:', error);
        // Still navigate to home even if logout API call fails
        this.router.navigate(['/home']);
      }
    });
  }
}