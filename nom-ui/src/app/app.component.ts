import {
  Component,
  computed,
  inject,
  signal,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import {
  Router,
  RouterOutlet,
  NavigationStart,
  NavigationEnd,
  NavigationCancel,
  NavigationError,
} from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { ThemeService } from './core/services/theme.service';
import { AuthService } from './core/services/auth.service';
import { LoadingService } from './core/services/loading.service';
import { Header } from './layout/header/header.component';
import { Footer } from './layout/footer/footer.component';
import { Nav } from './layout/nav/nav.component';
import { Sidebar } from './layout/sidebar/sidebar.component';
import { LoadingOverlay } from './shared/components/loading-overlay/loading-overlay.component';

@Component({
  selector: 'nom-root',
  imports: [RouterOutlet, MatIconModule, Header, Footer, Nav, Sidebar, LoadingOverlay],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown.escape)': 'onEscape()',
  },
})
export class App {
  private router = inject(Router);
  private document = inject(DOCUMENT);
  private firstNavigationDone = false;
  private destroyRef = inject(DestroyRef);
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  private loadingService = inject(LoadingService);

  isDarkTheme = computed(() => this.themeService.isDark());
  isLoggedIn = computed(() => this.authService.isLoggedIn());
  isLoading = computed(() => this.loadingService.isLoading());
  sidebarOpen = signal(false);
  navOpen = signal(false);
  // /login renders without app chrome (it is also iframed by the marketing site).
  chromeless = signal(false);

  private navLoadingKey: string | null = null;

  constructor() {
    this.router.events
      .pipe(
        filter(
          (e) =>
            e instanceof NavigationStart ||
            e instanceof NavigationEnd ||
            e instanceof NavigationCancel ||
            e instanceof NavigationError,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((e) => {
        if (e instanceof NavigationStart) {
          this.navLoadingKey = this.loadingService.add('Loading...');
        } else if (this.navLoadingKey) {
          this.loadingService.remove(this.navLoadingKey);
          this.navLoadingKey = null;
        }

        // Move focus to the content region on navigation so keyboard and
        // screen-reader users land on the new page, not stale chrome.
        if (e instanceof NavigationEnd) {
          this.chromeless.set(e.urlAfterRedirects.startsWith('/login'));
          if (this.firstNavigationDone) {
            this.document.getElementById('main-content')?.focus({ preventScroll: true });
          }
          this.firstNavigationDone = true;
        }
      });
  }

  toggleTheme(): void {
    this.themeService.toggle();
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  toggleNav(): void {
    this.navOpen.update((v) => !v);
  }

  closeNav(): void {
    this.navOpen.set(false);
  }

  onEscape(): void {
    if (this.navOpen()) this.closeNav();
    if (this.sidebarOpen()) this.sidebarOpen.set(false);
  }
}
