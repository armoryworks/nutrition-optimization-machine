// File: nom-ui/src/app/recipe/components/ingredient-details/ingredient-details.component.ts

import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { IngredientModel } from '../../models/ingredient.model';

@Component({
  selector: 'app-ingredient-details',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatListModule, MatDividerModule],
  templateUrl: './ingredient-details.component.html',
  styleUrls: ['./ingredient-details.component.scss'],
})
export class IngredientDetailsComponent {
  @Input() ingredient: IngredientModel | null = null;
}
