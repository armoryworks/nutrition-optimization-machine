import { Component, computed, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { ThemeService } from './core/services/theme.service';
import { AuthService } from './core/services/auth.service';
import { LoadingService } from './core/services/loading.service';
import { Header } from './layout/header/header.component';
import { Footer } from './layout/footer/footer.component';
import { Sidebar } from './layout/sidebar/sidebar.component';
import { LoadingOverlay } from './shared/components/loading-overlay/loading-overlay.component';

@Component({
  selector: 'nom-root',
  imports: [RouterOutlet, MatIconModule, Header, Footer, Sidebar, LoadingOverlay],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class App {
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  private loadingService = inject(LoadingService);

  isDarkTheme = computed(() => this.themeService.isDark());
  isLoggedIn = computed(() => this.authService.isLoggedIn());
  isLoading = computed(() => this.loadingService.isLoading());
  sidebarOpen = signal(false);

  toggleTheme(): void {
    this.themeService.toggle();
  }

  toggleSidebar(): void {
    this.sidebarOpen.update(v => !v);
  }

  logout(): void {
    this.authService.logout().subscribe();
  }
}
