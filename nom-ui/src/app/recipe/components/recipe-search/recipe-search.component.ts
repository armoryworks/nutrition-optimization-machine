import { Component, OnInit, inject } from '@angular/core';

import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';

import { AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

@Component({
  selector: 'nom-recipe-search',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatIconModule,
    AmwButtonComponent,
    AmwCardComponent
  ],
  templateUrl: './recipe-search.component.html',
  styleUrls: ['./recipe-search.component.scss']
})
export class RecipeSearchComponent implements OnInit {
  private router = inject(Router);

  searchControl = new FormControl('');

  ngOnInit(): void {
    // Initialize component
  }

  // Public user methods
  browsePublicRecipes(): void {
    // Navigate to public recipe browsing
    this.router.navigate(['/recipes/browse']);
  }

  // Navigation methods
  onLogin(): void {
    this.router.navigate(['/login']);
  }

  onRegister(): void {
    this.router.navigate(['/register']);
  }
} 