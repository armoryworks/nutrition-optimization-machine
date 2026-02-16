import { Component, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwIconComponent, AmwMenuComponent, AmwMenuItemComponent, AmwButtonComponent } from 'angular-material-wrap';
import { ThemeService } from '../../../services/theme.service';
import { AuthService } from '../../../auth/auth.service';
import { AuthManagerService } from '../../../utilities/services/auth-manager.service';

@Component({
  selector: 'nom-context-sidebar',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    AmwIconComponent,
    AmwMenuComponent,
    AmwMenuItemComponent,
    AmwButtonComponent,
  ],
  templateUrl: './context-sidebar.component.html',
  styleUrls: ['./context-sidebar.component.scss'],
})
export class ContextSidebarComponent {
  private themeService = inject(ThemeService);
  private authService = inject(AuthService);
  authManagerService = inject(AuthManagerService);

  isDarkTheme = computed(() => this.themeService.getCurrentTheme() === 'dark');
  isLoggedIn$ = this.authService.isLoggedIn$;

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  logout(): void {
    this.authService.logout().subscribe();
  }
}
