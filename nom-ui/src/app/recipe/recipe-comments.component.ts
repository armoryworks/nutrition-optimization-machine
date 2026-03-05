import { Component, inject, input, signal, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { RecipeCommentResponseModel } from '../core/models/comment.model';

@Component({
  selector: 'nom-recipe-comments',
  imports: [DatePipe, FormsModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './recipe-comments.component.html',
  styleUrl: './recipe-comments.component.scss'
})
export class RecipeComments implements OnInit {
  private http = inject(HttpClient);

  recipeId = input.required<number>();
  currentPersonId = input<number | null>(null);

  comments = signal<RecipeCommentResponseModel[]>([]);
  loading = signal(false);
  submitting = signal(false);
  newCommentText = '';

  ngOnInit(): void {
    this.loadComments();
  }

  loadComments(): void {
    this.loading.set(true);
    this.http.get<RecipeCommentResponseModel[]>(
      `${environment.apiUrl}/recipe/${this.recipeId()}/comments`
    ).subscribe({
      next: (comments) => {
        // Newest first
        this.comments.set(comments.sort((a, b) =>
          new Date(b.createdDate).getTime() - new Date(a.createdDate).getTime()
        ));
        this.loading.set(false);
      },
      error: () => {
        this.comments.set([]);
        this.loading.set(false);
      }
    });
  }

  onSubmitComment(): void {
    const text = this.newCommentText.trim();
    if (!text) return;

    this.submitting.set(true);
    this.http.post<RecipeCommentResponseModel>(
      `${environment.apiUrl}/recipe/${this.recipeId()}/comments`,
      { comment: text }
    ).subscribe({
      next: (comment) => {
        this.comments.set([comment, ...this.comments()]);
        this.newCommentText = '';
        this.submitting.set(false);
      },
      error: () => {
        this.submitting.set(false);
      }
    });
  }

  onDeleteComment(commentId: number): void {
    this.http.delete(
      `${environment.apiUrl}/recipe/comments/${commentId}`
    ).subscribe({
      next: () => {
        this.comments.set(this.comments().filter(c => c.id !== commentId));
      }
    });
  }
}
