import {
  Component,
  OnInit,
  ViewEncapsulation,
  Inject,
  PLATFORM_ID,
} from "@angular/core";
import { CommonModule, isPlatformBrowser } from "@angular/common"; // Import isPlatformBrowser
import { RouterOutlet, RouterLink } from "@angular/router";

// Angular Material Imports
import { MatToolbarModule } from "@angular/material/toolbar";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatCardModule } from "@angular/material/card";
import { MatDividerModule } from "@angular/material/divider";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { LoginComponent } from "./auth/components/login/login.component";

@Component({
  selector: "app-root",
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
  templateUrl: "./app.component.html",
  styleUrls: ["./app.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class AppComponent implements OnInit {
  title = "NOM - Nutrition Optimization Machine";
  isMenuOpen: boolean = false; // For the main burger menu
  isLoggedIn: boolean = false; // Mock user login status
  isUserMenuOpen: boolean = false; // Controls visibility of user popover/menu
  isDarkTheme: boolean = false; // For theme toggling
  currentYear: number = new Date().getFullYear(); // For copyright year

  constructor(@Inject(PLATFORM_ID) private platformId: Object) {}

  ngOnInit(): void {
    if (isPlatformBrowser(this.platformId)) {
      // Initialize login state
      this.isLoggedIn = localStorage.getItem("isLoggedIn") === "true";

      // Initialize theme
      this.isDarkTheme = localStorage.getItem("theme") === "dark";
      this.applyThemeClass();
    }
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
      localStorage.setItem("theme", this.isDarkTheme ? "dark" : "light");
    }
    this.applyThemeClass();
  }

  // Applies or removes theme classes from the document body
  private applyThemeClass(): void {
    if (isPlatformBrowser(this.platformId)) {
      document.body.classList.remove("dark-theme", "light-theme");
      document.body.classList.add(
        this.isDarkTheme ? "dark-theme" : "light-theme"
      );
    }
  }

  // Mock login function for demonstration purposes
  mockLogin(): void {
    this.isLoggedIn = true;
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem("isLoggedIn", "true");
    }
    this.isUserMenuOpen = false; // Close menu after mock login
    console.log("User logged in (mock)");
  }

  // Mock logout function for demonstration purposes
  mockLogout(): void {
    this.isLoggedIn = false;
    if (isPlatformBrowser(this.platformId)) {
      localStorage.removeItem("isLoggedIn");
    }
    this.isUserMenuOpen = false; // Close menu after mock logout
    console.log("User logged out (mock)");
  }
}
