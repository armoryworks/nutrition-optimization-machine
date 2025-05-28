import { Component, Inject, PLATFORM_ID, OnInit } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  imports: [
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule]
})
export class AppComponent implements OnInit {
  title = 'NOM - Nutrition Optimization Machine';
  isDarkTheme: boolean = false;
  currentYear: number = new Date().getFullYear();

  constructor(@Inject(PLATFORM_ID) private platformId: Object) {}

  ngOnInit(): void {
    if (isPlatformBrowser(this.platformId)) {
      this.isDarkTheme = localStorage.getItem('theme') === 'dark';
      this.applyThemeClass();
    }
  }

  toggleTheme(): void {
    this.isDarkTheme = !this.isDarkTheme;
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem('theme', this.isDarkTheme ? 'dark' : 'light');
    }
    this.applyThemeClass();
  }

  private applyThemeClass(): void {
    if (isPlatformBrowser(this.platformId)) {
      document.body.classList.remove('dark-theme', 'light-theme');
      document.body.classList.add(this.isDarkTheme ? 'dark-theme' : 'light-theme');
    }
  }

  openNavigation(): void {
    console.log('Navigation icon clicked!');
  }

  openUserProfile(): void {
    console.log('User profile icon clicked!');
  }
}