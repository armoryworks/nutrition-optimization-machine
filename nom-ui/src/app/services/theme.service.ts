import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

@Injectable({
    providedIn: 'root'
})
export class ThemeService {
    private isDarkThemeSubject = new BehaviorSubject<boolean>(false);
    isDarkTheme$ = this.isDarkThemeSubject.asObservable();

    constructor() {
        // Initialize theme from localStorage, default to dark theme
        const savedTheme = localStorage.getItem('theme');
        const isDark = savedTheme ? savedTheme === 'dark' : true; // Default to dark theme
        this.isDarkThemeSubject.next(isDark);
        
        // Apply theme class to body on initialization
        this.applyTheme(isDark);
    }

    toggleTheme(): void {
        const currentTheme = this.isDarkThemeSubject.value;
        const newTheme = !currentTheme;

        this.isDarkThemeSubject.next(newTheme);
        localStorage.setItem('theme', newTheme ? 'dark' : 'light');

        // Apply theme class to body
        this.applyTheme(newTheme);
    }

    private applyTheme(isDark: boolean): void {
        // Remove existing theme classes
        document.body.classList.remove('dark-theme', 'light-theme');
        
        // Add new theme class
        document.body.classList.add(isDark ? 'dark-theme' : 'light-theme');
        
        // Also set data attribute for additional CSS targeting
        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
        
        console.log('Theme applied:', isDark ? 'dark' : 'light');
    }
}

