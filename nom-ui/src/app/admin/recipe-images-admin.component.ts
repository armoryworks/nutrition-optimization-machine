import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RecipeImageService, summarizeImageChecks } from '../core/services/recipe-image.service';
import { RecipeImageCandidate, RecipeImageCandidateStatus, RecipeImageCheckSummary, RecipeImageCredit } from '../core/models/recipe-image.model';
import { ImageCredit } from '../shared/components/image-credit/image-credit.component';

/**
 * Review queue for openly licensed recipe photos proposed by ops/recipe-image-finder.py.
 * Photos that passed every check were attached automatically and are listed under
 * "attached" (removable); borderline ones wait here for approve / reject.
 */
@Component({
  selector: 'nom-recipe-images-admin',
  imports: [RouterLink, DecimalPipe, MatButtonModule, MatIconModule, ImageCredit],
  templateUrl: './recipe-images-admin.component.html',
  styleUrl: './recipe-images-admin.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipeImagesAdmin implements OnInit {
  private imageService = inject(RecipeImageService);
  private snackBar = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  readonly statuses: RecipeImageCandidateStatus[] = ['pending', 'attached', 'approved', 'rejected', 'removed'];

  candidates = signal<RecipeImageCandidate[]>([]);
  previews = signal<Record<number, string>>({});
  filter = signal<RecipeImageCandidateStatus>('pending');
  loading = signal(false);
  busyId = signal<number | null>(null);
  errorMessage = signal('');

  private objectUrls: string[] = [];

  constructor() {
    this.destroyRef.onDestroy(() => this.revokePreviews());
  }

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.imageService.getCandidates(this.filter()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => {
        this.candidates.set(list);
        this.loading.set(false);
        this.loadPreviews(list);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to load image candidates.');
      },
    });
  }

  setFilter(status: RecipeImageCandidateStatus): void {
    this.filter.set(status);
    this.refresh();
  }

  previewFor(c: RecipeImageCandidate): string | null {
    return c.previewUrl ?? this.previews()[c.id] ?? null;
  }

  creditFor(c: RecipeImageCandidate): RecipeImageCredit {
    return {
      author: c.author,
      authorUrl: c.authorUrl,
      sourceName: c.sourceName,
      sourceUrl: c.landingUrl,
      license: c.license,
      licenseUrl: c.licenseUrl,
      noDerivatives: false,
    };
  }

  checksFor(c: RecipeImageCandidate): RecipeImageCheckSummary[] {
    return summarizeImageChecks(c.checksJson);
  }

  approve(c: RecipeImageCandidate): void {
    this.act(c, this.imageService.approve(c.id), `Photo attached to “${c.recipeName}”.`, (status) =>
      status === 409 ? `“${c.recipeName}” already has an image.` : `Couldn't approve the photo for “${c.recipeName}”.`,
    );
  }

  reject(c: RecipeImageCandidate): void {
    this.act(c, this.imageService.reject(c.id), 'Photo rejected — it will not be proposed again.', () => `Couldn't reject the photo.`);
  }

  remove(c: RecipeImageCandidate): void {
    this.act(c, this.imageService.remove(c.id), `Photo removed from “${c.recipeName}”.`, () => `Couldn't remove the photo.`);
  }

  private act(
    c: RecipeImageCandidate,
    request: ReturnType<RecipeImageService['approve']>,
    success: string,
    failure: (status: number) => string,
  ): void {
    this.busyId.set(c.id);
    this.errorMessage.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busyId.set(null);
        this.snackBar.open(success, 'OK', { duration: 4000 });
        this.refresh();
      },
      error: (err) => {
        this.busyId.set(null);
        this.errorMessage.set(failure(err?.status ?? 0));
      },
    });
  }

  private loadPreviews(list: RecipeImageCandidate[]): void {
    this.revokePreviews();
    this.previews.set({});
    for (const c of list.filter((x) => !x.previewUrl && (x.status === 'pending' || x.status === 'attached' || x.status === 'approved'))) {
      this.imageService.getCandidateImage(c.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          this.objectUrls.push(url);
          this.previews.update((p) => ({ ...p, [c.id]: url }));
        },
        error: () => undefined,
      });
    }
  }

  private revokePreviews(): void {
    this.objectUrls.forEach((u) => URL.revokeObjectURL(u));
    this.objectUrls = [];
  }
}
