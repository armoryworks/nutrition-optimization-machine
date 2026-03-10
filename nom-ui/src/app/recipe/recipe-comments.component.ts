import { Component, inject, input, signal, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RecipeService } from '../core/services/recipe.service';
import { RecipeCommentResponseModel } from '../core/models/recipe-comment-response.model';

@Component({
  selector: 'nom-recipe-comments',
  imports: [DatePipe, ReactiveFormsModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './recipe-comments.component.html',
  styleUrl: './recipe-comments.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeComments implements OnInit {
  private recipeService = inject(RecipeService);
  private destroyRef = inject(DestroyRef);

  recipeId = input.required<number>();
  currentPersonId = input<number | null>(null);

  comments = signal<RecipeCommentResponseModel[]>([]);
  loading = signal(false);
  submitting = signal(false);
  newCommentText = new FormControl('');

  ngOnInit(): void {
    this.loadComments();
  }

  loadComments(): void {
    this.loading.set(true);
    this.recipeService.getComments(this.recipeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
    const text = (this.newCommentText.value ?? '').trim();
    if (!text) return;

    this.submitting.set(true);
    this.recipeService.addComment(this.recipeId(), text).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (comment) => {
        this.comments.set([comment, ...this.comments()]);
        this.newCommentText.setValue('');
        this.submitting.set(false);
      },
      error: () => {
        this.submitting.set(false);
      }
    });
  }

  onDeleteComment(commentId: number): void {
    this.recipeService.deleteComment(commentId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.comments.set(this.comments().filter(c => c.id !== commentId));
      }
    });
  }
}
