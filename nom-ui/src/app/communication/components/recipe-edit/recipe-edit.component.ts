// File: nom-ui/src/app/recipe/components/recipe-edit/recipe-edit.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RecipeService } from '../../../recipe/services/recipe.service';

@Component({
  selector: 'app-recipe-edit',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './recipe-edit.component.html',
  styleUrls: ['./recipe-edit.component.scss']
})
export class RecipeEditComponent implements OnInit {
  recipeForm: FormGroup;
  isEditMode = false;
  recipeId: number | null = null;
  isLoading = true;
  pageTitle = 'Create Recipe';

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    public router: Router, // CORRECTED: Must be public to be accessible in the template.
    private recipeService: RecipeService
  ) {
    this.recipeForm = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(511)]],
      description: ['', [Validators.maxLength(2047)]],
      // Add other form controls for prepTime, cookTime, servings, ingredients, steps etc.
    });
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap(params => {
        const id = params.get('id');
        if (id) {
          this.isEditMode = true;
          this.pageTitle = 'Edit Recipe';
          this.recipeId = +id;
          // In a real app, you would fetch the recipe data here
          // return this.recipeService.getRecipeDetails(this.recipeId);
          return of(null); // Placeholder
        }
        this.isLoading = false;
        return of(null);
      })
    ).subscribe(recipeData => {
      if (this.isEditMode && recipeData) {
        // this.recipeForm.patchValue(recipeData);
      }
      this.isLoading = false;
    });
  }

  onSubmit(): void {
    if (this.recipeForm.invalid) {
      return;
    }

    if (this.isEditMode && this.recipeId) {
      // Logic for updating an existing recipe
      // this.recipeService.updateRecipe(this.recipeId, this.recipeForm.value).subscribe(...);
    } else {
      // Logic for creating a new recipe
      // this.recipeService.createRecipe(this.recipeForm.value).subscribe(...);
    }
  }
}