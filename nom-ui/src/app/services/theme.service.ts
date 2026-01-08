// File: nom-ui/src/app/services/theme.service.ts

import { Injectable, signal, effect } from '@angular/core';

export type ThemeConfig = 'light' | 'dark';

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  private readonly THEME_STORAGE_KEY = 'theme';
  private readonly theme = signal<ThemeConfig>('dark');

  constructor() {
    // Load theme from localStorage on initialization
    const savedTheme = localStorage.getItem(this.THEME_STORAGE_KEY) as ThemeConfig;
    if (savedTheme === 'light' || savedTheme === 'dark') {
      this.theme.set(savedTheme);
    }

    // Effect to persist theme changes to localStorage
    effect(() => {
      const currentTheme = this.theme();
      localStorage.setItem(this.THEME_STORAGE_KEY, currentTheme);
      this.applyTheme(currentTheme);
    });
  }

  /**
   * Get the current theme as a signal
   */
  getTheme() {
    return this.theme.asReadonly();
  }

  /**
   * Get the current theme value
   */
  getCurrentTheme(): ThemeConfig {
    return this.theme();
  }

  /**
   * Set the theme
   */
  setTheme(theme: ThemeConfig): void {
    this.theme.set(theme);
  }

  /**
   * Toggle between light and dark themes
   */
  toggleTheme(): void {
    this.theme.set(this.theme() === 'dark' ? 'light' : 'dark');
  }

  /**
   * Apply theme classes to document
   */
  private applyTheme(theme: ThemeConfig): void {
    document.body.classList.remove('light-theme', 'dark-theme');
    document.body.classList.add(`${theme}-theme`);
    document.documentElement.setAttribute('data-theme', theme);
  }
}
