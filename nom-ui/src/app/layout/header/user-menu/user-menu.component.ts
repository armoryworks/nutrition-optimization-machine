import { Component, computed, inject, output, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'nom-user-menu',
  imports: [RouterLink, MatIconModule],
  templateUrl: './user-menu.component.html',
  styleUrl: './user-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenu {
  private authService = inject(AuthService);
  private router = inject(Router);

  closed = output<void>();

  email = computed(() => this.authService.username());

  initial = computed(() => {
    const name = this.authService.username();
    return name ? name.charAt(0).toUpperCase() : 'U';
  });

  onLogout(): void {
    this.authService.logout().subscribe(() => {
      this.router.navigate(['/home']);
    });
    this.closed.emit();
  }
}
