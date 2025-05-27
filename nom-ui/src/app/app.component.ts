import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  isMenuOpen: boolean = false;

  toggleMenu(): void {
    this.isMenuOpen = !this.isMenuOpen;
  }
  closeMenuOnBodyClick(): void {
    if (this.isMenuOpen) {
      this.isMenuOpen = false;
    }
  }
  stopPropagation(event: Event): void {
    event.stopPropagation();
  }
}
