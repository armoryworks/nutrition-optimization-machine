// File: nom-ui/src/app/app.component.ts

import {
  Component,
  OnInit,
  ViewEncapsulation,
  Inject,
  PLATFORM_ID,
  OnDestroy,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import {
  RouterOutlet,
  RouterLink,
  Router,
  NavigationStart,
} from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { LoginComponent } from './auth/login/login.component';
import { NomConfigService } from './utilities/services/nom-config.service';
import { AuthManagerService } from './utilities/services/auth-manager.service';
import { AuthService } from './auth/auth.service';
import { NotificationService } from './utilities/services/notification.service';
import { UserInfoService } from './utilities/services/user-info.service';
import { Subscription, Observable } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

@Component({
  selector: 'nom-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    FormsModule,
    LoginComponent,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'NOM - Nutrition Optimization Machine';
  isMenuOpen: boolean = false;
  isLoggedIn: boolean = false;
  isUserMenuOpen: boolean = false;
  isDarkTheme: boolean = false;
  currentYear: number = new Date().getFullYear();
  searchQuery: string = '';

  // Observables for reactive UI updates
  isLoggedIn$: Observable<boolean>;
  canManageCuration$: Observable<boolean>;
  canManageUserRoles$: Observable<boolean>;

  private subscriptions: Subscription = new Subscription();

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private notificationService: NotificationService,
    private configService: NomConfigService,
    private authManagerService: AuthManagerService,
    private authService: AuthService,
    private userInfoService: UserInfoService,
    private router: Router
  ) {
    // Initialize observables from the AuthManagerService
    this.isLoggedIn$ = this.authManagerService.userLogin;
    this.canManageCuration$ = this.authManagerService.canManageCuration$;
    this.canManageUserRoles$ = this.authManagerService.canManageUserRoles$;
  }

  ngOnInit(): void {
    this.configService.loadSettings();
    this.isDarkTheme = localStorage.getItem('theme') === 'dark';
    this.applyThemeClass();
    this.checkLoggedIn();
    // Removed loadUserInfo() call since AuthManagerService will handle it

    // Add debugging for claims observables
    this.subscriptions.add(
      this.canManageCuration$.subscribe(hasCuration => {
        console.log('CanManageCuration changed:', hasCuration);
      })
    );

    this.subscriptions.add(
      this.canManageUserRoles$.subscribe(hasUserRoles => {
        console.log('CanManageUserRoles changed:', hasUserRoles);
      })
    );

    this.subscriptions.add(
      this.router.events.subscribe((event) => {
        if (event instanceof NavigationStart) {
          if (this.isUserMenuOpen) { this.toggleUserMenu(); }
          if (this.isMenuOpen) { this.toggleMenu(); }
        }
      })
    );

    this.subscriptions.add(
      this.authManagerService.openUserMenuSignal.subscribe(() => {
        if (!this.isUserMenuOpen) { this.toggleUserMenu(); }
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  checkLoggedIn() {
    this.subscriptions.add(
      this.isLoggedIn$.subscribe((status) => {
        this.isLoggedIn = status;
        // Don't load user info here since AuthManagerService will handle it after login
      })
    );
    this.authManagerService.checkUserLoggedInStatus();
  }

  loadUserInfo() {
    // Only load user info if user is logged in
    if (this.authManagerService.isLoggedIn()) {
      this.authService.loadUserInfo().subscribe({
        next: (userInfo) => {
          console.log('User info loaded:', userInfo);
        },
        error: (error) => {
          console.error('Error loading user info:', error);
        }
      });
    }
  }

  toggleMenu(): void { this.isMenuOpen = !this.isMenuOpen; }
  toggleUserMenu(): void { this.isUserMenuOpen = !this.isUserMenuOpen; }

  toggleTheme(): void {
    this.isDarkTheme = !this.isDarkTheme;
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem('theme', this.isDarkTheme ? 'dark' : 'light');
    }
    this.applyThemeClass();
  }

  onSearchInput(event: any): void {
    // Handle search input with debouncing
    const query = event.target.value;
    if (query.length >= 2) {
      // Implement debounced search here if needed
      console.log('Search query:', query);
    }
  }

  performSearch(): void {
    if (this.searchQuery.trim()) {
      // Navigate to recipe search with query only
      this.router.navigate(['/recipe-search'], {
        queryParams: {
          q: this.searchQuery.trim()
        }
      });
    }
  }

  private applyThemeClass(): void {
    if (isPlatformBrowser(this.platformId)) {
      document.body.classList.remove('dark-theme', 'light-theme');
      document.body.classList.add(this.isDarkTheme ? 'dark-theme' : 'light-theme');
    }
  }

  logout(): void {
    this.isUserMenuOpen = false;
    this.authService.logout().subscribe({
      next: () => {
        this.authManagerService.logout();
        this.notificationService.success('Logged Out Successfully');
      },
      error: (error: any) => {
        console.error('Logout error:', error);
        this.notificationService.error(error.message || 'Failed to log out. Please try again.');
      },
    });
  }

  onOnboardingComplete(success: boolean): void {
    console.log('Onboarding Workflow completed:', success ? 'Successfully!' : 'With errors.');
    if (success) {
      alert('Onboarding complete! Redirecting to dashboard (simulated).');
    } else {
      alert('There was an issue completing your onboarding. Please try again.');
    }
  }
}