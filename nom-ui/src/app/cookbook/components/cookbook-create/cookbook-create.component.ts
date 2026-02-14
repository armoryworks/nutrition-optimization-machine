import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwIconComponent } from 'angular-material-wrap';

import { CookbookService } from '../../services/cookbook.service';
import { CookbookCreateRequestModel } from '../../models/cookbook-create-request.model';

@Component({
    selector: 'nom-cookbook-create',
    standalone: true,
    imports: [
        FormsModule,
        AmwButtonComponent,
        AmwIconComponent,
    ],
    templateUrl: './cookbook-create.component.html',
    styleUrls: ['./cookbook-create.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class CookbookCreateComponent implements OnInit {
    private cookbookService = inject(CookbookService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);

    model = new CookbookCreateRequestModel();
    saving = signal(false);
    error = signal('');

    ngOnInit(): void {
        const householdId = Number(this.route.snapshot.queryParamMap.get('householdId'));
        if (householdId) this.model.householdId = householdId;
    }

    save(): void {
        if (!this.model.name.trim()) {
            this.error.set('Name is required.');
            return;
        }

        this.saving.set(true);
        this.error.set('');

        this.cookbookService.createCookbook(this.model).subscribe({
            next: (id) => {
                this.saving.set(false);
                this.router.navigate(['/cookbook', id]);
            },
            error: () => {
                this.error.set('Failed to create cookbook. Please try again.');
                this.saving.set(false);
            },
        });
    }

    cancel(): void {
        this.router.navigate(['/cookbook']);
    }
}
