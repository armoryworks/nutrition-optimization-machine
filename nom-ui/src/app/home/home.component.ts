import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'nom-home',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatCardModule
  ],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  private router = inject(Router);

  // Hero card click handlers
  onRecipeCardClick(): void {
    // For now, redirect to home since recipe browsing isn't implemented
    // When recipe browsing is implemented, this should go to /recipes
    this.router.navigate(['/home']);
  }

  onIngredientCardClick(): void {
    // For now, redirect to home since ingredient browsing isn't implemented
    // When ingredient browsing is implemented, this should go to /ingredients
    this.router.navigate(['/home']);
  }

  onNutritionCardClick(): void {
    // For now, redirect to home since nutrition info isn't implemented
    // When nutrition info is implemented, this should go to /nutrition
    this.router.navigate(['/home']);
  }

  // Quick action handlers
  onBrowseRecipesClick(): void {
    // For now, redirect to home since recipe browsing isn't implemented
    this.router.navigate(['/home']);
  }

  onFindIngredientsClick(): void {
    // For now, redirect to home since ingredient browsing isn't implemented
    this.router.navigate(['/home']);
  }
}
