import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { CookbookService } from '../../services/cookbook.service';
import { CookbookResponseModel } from '../../models/cookbook-response.model';
import { CookbookUpdateRequestModel } from '../../models/cookbook-update-request.model';

@Component({
    selector: 'nom-cookbook-edit',
    standalone: true,
    imports: [
        FormsModule,
        AmwButtonComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
    ],
    templateUrl: './cookbook-edit.component.html',
    styleUrls: ['./cookbook-edit.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class CookbookEditComponent implements OnInit {
    private cookbookService = inject(CookbookService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);

    cookbook = signal<CookbookResponseModel | null>(null);
    model = new CookbookUpdateRequestModel();
    loading = signal(false);
    saving = signal(false);
    error = signal('');

    private cookbookId = 0;

    ngOnInit(): void {
        this.cookbookId = Number(this.route.snapshot.paramMap.get('id'));
        if (this.cookbookId) this.loadCookbook();
    }

    loadCookbook(): void {
        this.loading.set(true);
        this.cookbookService.getCookbook(this.cookbookId).subscribe({
            next: (cookbook) => {
                this.cookbook.set(cookbook);
                this.model.name = cookbook.name;
                this.model.description = cookbook.description;
                this.model.isPublic = cookbook.isPublic;
                this.loading.set(false);
            },
            error: () => {
                this.error.set('Failed to load cookbook.');
                this.loading.set(false);
            },
        });
    }

    save(): void {
        if (!this.model.name?.trim()) {
            this.error.set('Name is required.');
            return;
        }

        this.saving.set(true);
        this.error.set('');

        this.cookbookService.updateCookbook(this.cookbookId, this.model).subscribe({
            next: () => {
                this.saving.set(false);
                this.router.navigate(['/cookbook', this.cookbookId]);
            },
            error: () => {
                this.error.set('Failed to update cookbook. Please try again.');
                this.saving.set(false);
            },
        });
    }

    cancel(): void {
        this.router.navigate(['/cookbook', this.cookbookId]);
    }
}
