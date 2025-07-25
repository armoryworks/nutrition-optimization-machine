// File: nom-ui/src/app/recipe/components/recipe-detail/recipe-detail.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { Observable, of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RecipeService } from '../../../recipe/services/recipe.service';
import { RecipeModel } from '../../../recipe/models/recipe.model';

@Component({
  selector: 'app-recipe-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './recipe-detail.component.html',
  styleUrls: ['./recipe-detail.component.scss']
})
export class RecipeDetailComponent implements OnInit {
  recipe$: Observable<RecipeModel | null> = of(null);
  
  constructor(
    private route: ActivatedRoute,
    private recipeService: RecipeService
  ) { }

  ngOnInit(): void {
    this.recipe$ = this.route.paramMap.pipe(
      switchMap(params => {
        const id = params.get('id');
        if (id) {
          // return this.recipeService.getRecipeDetails(+id); // Real implementation
          // Placeholder with more fields to match the HTML
          return of({ 
            id: +id, 
            name: `Sample Recipe ${id}`, 
            description: 'This is a detailed description of a delicious dish that is both healthy and easy to make.',
            prepTimeMinutes: 15,
            cookTimeMinutes: 45,
            servings: 4
          } as RecipeModel);
        }
        return of(null);
      })
    );
  }
}