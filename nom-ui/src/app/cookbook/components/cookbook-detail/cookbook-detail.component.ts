import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { CookbookService } from '../../services/cookbook.service';
import { CookbookResponseModel } from '../../models/cookbook-response.model';
import { RecipeDashboardItemModel } from '../../../recipe/models/recipe-dashboard-item.model';

@Component({
    selector: 'nom-cookbook-detail',
    standalone: true,
    imports: [
        AmwButtonComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
    ],
    templateUrl: './cookbook-detail.component.html',
    styleUrls: ['./cookbook-detail.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class CookbookDetailComponent implements OnInit {
    private cookbookService = inject(CookbookService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);

    cookbook = signal<CookbookResponseModel | null>(null);
    recipes = signal<RecipeDashboardItemModel[]>([]);
    loading = signal(false);
    error = signal('');

    ngOnInit(): void {
        const id = Number(this.route.snapshot.paramMap.get('id'));
        if (id) this.loadCookbook(id);
    }

    loadCookbook(id: number): void {
        this.loading.set(true);
        this.error.set('');

        this.cookbookService.getCookbook(id).subscribe({
            next: (cookbook) => {
                this.cookbook.set(cookbook);
                this.loadRecipes(id);
            },
            error: () => {
                this.error.set('Failed to load cookbook.');
                this.loading.set(false);
            },
        });
    }

    loadRecipes(cookbookId: number): void {
        this.cookbookService.getCookbookRecipes(cookbookId).subscribe({
            next: (recipes) => {
                this.recipes.set(recipes);
                this.loading.set(false);
            },
            error: () => {
                this.loading.set(false);
            },
        });
    }

    editCookbook(): void {
        const cb = this.cookbook();
        if (cb) this.router.navigate(['/cookbook', cb.id, 'edit']);
    }

    viewRecipe(recipe: RecipeDashboardItemModel): void {
        this.router.navigate(['/recipes', recipe.id]);
    }

    removeRecipe(recipe: RecipeDashboardItemModel): void {
        const cb = this.cookbook();
        if (!cb || !confirm(`Remove "${recipe.name}" from this cookbook?`)) return;

        this.cookbookService.removeRecipe(cb.id, recipe.id).subscribe({
            next: () => this.loadRecipes(cb.id),
            error: () => this.error.set('Failed to remove recipe.'),
        });
    }

    goBack(): void {
        this.router.navigate(['/cookbook']);
    }
}
