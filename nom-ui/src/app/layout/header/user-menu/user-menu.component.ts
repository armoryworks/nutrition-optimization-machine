import {
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  output,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'nom-user-menu',
  imports: [RouterLink, RouterLinkActive, MatIconModule],
  templateUrl: './user-menu.component.html',
  styleUrl: './user-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenu {
  private authService = inject(AuthService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  closed = output<void>();

  constructor() {
    const elementRef = inject<ElementRef<HTMLElement>>(ElementRef);
    // Move focus into the menu when it opens; the header restores it on close.
    afterNextRender(() => {
      elementRef.nativeElement.querySelector<HTMLElement>('a, button')?.focus();
    });
  }

  email = computed(() => this.authService.username());

  initial = computed(() => {
    const name = this.authService.username();
    return name ? name.charAt(0).toUpperCase() : 'U';
  });

  onLogout(): void {
    this.authService
      .logout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.router.navigate(['/home']);
      });
    this.closed.emit();
  }
}
