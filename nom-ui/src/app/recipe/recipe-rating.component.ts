import { Component, inject, input, signal, computed, OnInit } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { RecipeRatingResponseModel } from '../core/models/rating.model';

@Component({
  selector: 'nom-recipe-rating',
  imports: [MatIconModule, MatButtonModule],
  templateUrl: './recipe-rating.component.html',
  styleUrl: './recipe-rating.component.scss'
})
export class RecipeRating implements OnInit {
  private http = inject(HttpClient);

  recipeId = input.required<number>();
  currentPersonId = input<number | null>(null);

  ratings = signal<RecipeRatingResponseModel[]>([]);
  loading = signal(false);
  hoverStar = signal(0);

  stars = [1, 2, 3, 4, 5];

  averageRating = computed(() => {
    const r = this.ratings();
    if (r.length === 0) return 0;
    const sum = r.reduce((acc, cur) => acc + cur.rating, 0);
    return sum / r.length;
  });

  userRating = computed(() => {
    const personId = this.currentPersonId();
    if (personId == null) return null;
    return this.ratings().find(r => r.raterId === personId) ?? null;
  });

  userRatingValue = computed(() => {
    return this.userRating()?.rating ?? 0;
  });

  roundRating(): number {
    return Math.round(this.averageRating());
  }

  ngOnInit(): void {
    this.loadRatings();
  }

  loadRatings(): void {
    this.loading.set(true);
    this.http.get<RecipeRatingResponseModel[]>(
      `${environment.apiUrl}/recipe/${this.recipeId()}/ratings`
    ).subscribe({
      next: (ratings) => {
        this.ratings.set(ratings);
        this.loading.set(false);
      },
      error: () => {
        this.ratings.set([]);
        this.loading.set(false);
      }
    });
  }

  onRate(star: number): void {
    const existing = this.userRating();
    if (existing) {
      // Update existing rating
      this.http.put<RecipeRatingResponseModel>(
        `${environment.apiUrl}/recipe/ratings/${existing.id}`,
        { rating: star }
      ).subscribe({
        next: (updated) => {
          this.ratings.set(
            this.ratings().map(r => r.id === updated.id ? updated : r)
          );
        }
      });
    } else {
      // Create new rating
      this.http.post<RecipeRatingResponseModel>(
        `${environment.apiUrl}/recipe/${this.recipeId()}/ratings`,
        { rating: star }
      ).subscribe({
        next: (created) => {
          this.ratings.set([...this.ratings(), created]);
        }
      });
    }
  }
}
