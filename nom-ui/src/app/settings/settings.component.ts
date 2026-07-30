import { Component, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'nom-settings',
  imports: [RouterLink, MatIconModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Settings {
  private authService = inject(AuthService);
  private destroyRef = inject(DestroyRef);

  isAdmin = this.authService.isAdmin;

  constructor() {
    this.authService.ensureAdminStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}
