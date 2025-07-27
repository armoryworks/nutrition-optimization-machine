// File: nom-ui/src/app/user/components/recipe-author-dashboard/recipe-author-dashboard.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';

@Component({
  selector: 'app-recipe-author-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './recipe-author-dashboard.component.html',
  styleUrls: ['./recipe-author-dashboard.component.scss']
})
export class RecipeAuthorDashboardComponent implements OnInit {
  recipes$!: Observable<RecipeDashboardItemModel[]>;
  // Placeholder for ingredients - this would follow the same pattern
  ingredients: any[] = [];
  error: string | null = null;

  constructor(private recipeService: RecipeService) { }

  ngOnInit(): void {
    this.recipes$ = this.recipeService.getMyRecipes().pipe(
      catchError(err => {
        console.error('Error fetching recipes:', err);
        this.error = 'Could not load your recipes. Please try again later.';
        return of([]); // Return an empty array to prevent breaking the async pipe
      })
    );
  }
}