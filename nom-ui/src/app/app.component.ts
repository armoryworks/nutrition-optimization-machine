import {
  Component,
  OnInit,
  ViewEncapsulation,
  Inject,
  PLATFORM_ID,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common'; // Import isPlatformBrowser
import { RouterOutlet, RouterLink } from '@angular/router';

// Angular Material Imports
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
import { MatSnackBar } from '@angular/material/snack-bar';

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
export class AppComponent implements OnInit {
  title = 'NOM - Nutrition Optimization Machine';
  isMenuOpen: boolean = false; // For the main burger menu
  isLoggedIn: boolean = false; // Mock user login status
  isUserMenuOpen: boolean = false; // Controls visibility of user popover/menu
  isDarkTheme: boolean = false; // For theme toggling
  currentYear: number = new Date().getFullYear(); // For copyright year

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private snackBar: MatSnackBar,
    private configService: NomConfigService,
    private authManagerService: AuthManagerService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.configService.getSettings();
    this.isDarkTheme = localStorage.getItem('theme') === 'dark';
    this.applyThemeClass();
    this.checkLoggedIn();
  }

  checkLoggedIn() {
    this.authManagerService.userLogin.subscribe(() => {
      this.isLoggedIn = !!this.authManagerService.token;
    });
    //have to access token or call checkUserLoggedInStatus at least once
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
    this.authService.logout().subscribe(() => {
      this.authManagerService.clearStorageAfterLogout();
      this.isLoggedIn = false;
      this.snackBar.open('Logged Out Successfully', undefined, {
        duration: 2000,
      });
    });
  }
}
