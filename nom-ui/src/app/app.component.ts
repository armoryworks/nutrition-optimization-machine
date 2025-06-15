import {
  Component,
  OnInit,
  ViewEncapsulation,
  Inject,
  PLATFORM_ID,
  OnDestroy, // Import OnDestroy
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import {
  RouterOutlet,
  RouterLink,
  Router,
  NavigationStart,
} from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { LoginComponent } from './components/auth/login/login.component';
import { NomConfigService } from './utilities/services/nom-config.service';
import { AuthManagerService } from './utilities/services/auth-manager.service';
import { AuthService } from './components/auth/auth.service';
import { NotificationService } from './utilities/services/notification.service';
import { Subscription } from 'rxjs'; // Import Subscription

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    LoginComponent,
    // Material Modules
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
  // Implement OnDestroy
  title = 'NOM - Nutrition Optimization Machine';
  isMenuOpen: boolean = false; // For the main burger menu
  isLoggedIn: boolean = false; // Mock user login status
  isUserMenuOpen: boolean = false; // Controls visibility of user popover/menu
  isDarkTheme: boolean = false; // For theme toggling
  currentYear: number = new Date().getFullYear(); // For copyright year

  private subscriptions: Subscription = new Subscription(); // To manage subscriptions

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private notificationService: NotificationService,
    private configService: NomConfigService,
    private authManagerService: AuthManagerService,
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.configService.loadSettings();
    this.isDarkTheme = localStorage.getItem('theme') === 'dark';
    this.applyThemeClass();
    this.checkLoggedIn();

    // Subscribe to router events for menu closing
    this.subscriptions.add(
      this.router.events.subscribe((event) => {
        if (event instanceof NavigationStart) {
          if (this.isUserMenuOpen) {
            this.toggleUserMenu(); // Close user menu on navigation start
          }
          // Also close main menu on navigation
          if (this.isMenuOpen) {
            this.toggleMenu();
          }
        }
      })
    );

    // Subscribe to the new signal from AuthManagerService to open the user menu
    this.subscriptions.add(
      this.authManagerService.openUserMenuSignal.subscribe(() => {
        if (!this.isUserMenuOpen) {
          // Only open if it's not already open
          this.toggleUserMenu();
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe(); // Unsubscribe from all subscriptions
  }

  checkLoggedIn() {
    this.subscriptions.add(
      // Add to subscriptions for proper cleanup
      this.authManagerService.userLogin.subscribe(() => {
        this.isLoggedIn = !!this.authManagerService.token;
      })
    );
    this.authManagerService.checkUserLoggedInStatus();
  }

  // Toggles the main navigation menu
  toggleMenu(): void {
    this.isMenuOpen = !this.isMenuOpen;
  }

  // Toggles the user profile popover/menu
  toggleUserMenu(): void {
    this.isUserMenuOpen = !this.isUserMenuOpen;
  }

  // Toggles between light and dark themes
  toggleTheme(): void {
    this.isDarkTheme = !this.isDarkTheme;
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem('theme', this.isDarkTheme ? 'dark' : 'light');
    }
    this.applyThemeClass();
  }

  // Applies or removes theme classes from the document body
  private applyThemeClass(): void {
    if (isPlatformBrowser(this.platformId)) {
      document.body.classList.remove('dark-theme', 'light-theme');
      document.body.classList.add(
        this.isDarkTheme ? 'dark-theme' : 'light-theme'
      );
    }
  }

  logout(): void {
    this.isUserMenuOpen = false;
    this.authService.logout().subscribe({
      next: () => {
        this.authManagerService.clearStorageAfterLogout();
        this.isLoggedIn = false;
        this.notificationService.success('Logged Out Successfully');
      },
      error: (error) => {
        console.error('Logout error:', error);
        this.notificationService.error(
          error.message || 'Failed to log out. Please try again.'
        );
      },
    });
  }
}
