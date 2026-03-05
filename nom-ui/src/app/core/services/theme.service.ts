import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private darkMode = signal(true);
  readonly isDark = this.darkMode.asReadonly();

  constructor() {
    const saved = localStorage.getItem('nom-theme');
    if (saved === 'light') {
      this.darkMode.set(false);
      document.body.classList.replace('dark-theme', 'light-theme');
    }
  }

  toggle(): void {
    const next = !this.darkMode();
    this.darkMode.set(next);
    document.body.classList.replace(
      next ? 'light-theme' : 'dark-theme',
      next ? 'dark-theme' : 'light-theme'
    );
    localStorage.setItem('nom-theme', next ? 'dark' : 'light');
  }
}
