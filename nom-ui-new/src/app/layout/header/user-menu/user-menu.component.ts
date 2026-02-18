import { Component, computed, inject, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'nom-user-menu',
  imports: [RouterLink, MatIconModule],
  templateUrl: './user-menu.component.html',
  styleUrl: './user-menu.component.scss',
})
export class UserMenu {
  private authService = inject(AuthService);

  closed = output<void>();

  email = computed(() => this.authService.username());

  initial = computed(() => {
    const name = this.authService.username();
    return name ? name.charAt(0).toUpperCase() : 'U';
  });

  onLogout(): void {
    this.authService.logout().subscribe();
    this.closed.emit();
  }
}
