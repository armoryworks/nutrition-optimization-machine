import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent } from 'angular-material-wrap';

import { LabelService } from '../../services/label.service';
import { LabelResponseModel, LabelCreateRequestModel } from '../../models/label.models';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-label-dashboard',
    standalone: true,
    imports: [FormsModule, AmwButtonComponent, AmwIconComponent, AmwInlineLoadingComponent],
    templateUrl: './label-dashboard.component.html',
    styleUrls: ['./label-dashboard.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class LabelDashboardComponent implements OnInit {
    private labelService = inject(LabelService);

    labels = signal<LabelResponseModel[]>([]);
    loading = signal(false);
    error = signal('');
    showCreateForm = signal(false);
    editingLabel = signal<LabelResponseModel | null>(null);

    newLabel: LabelCreateRequestModel = { name: '' };

    pageTitle = 'Labels';
    pageSubtitle = 'Organize content with custom labels';

    ngOnInit(): void {
        this.loadLabels();
    }

    loadLabels(): void {
        this.loading.set(true);
        this.error.set('');

        this.labelService.getLabels().subscribe({
            next: (labels) => { this.labels.set(labels); this.loading.set(false); },
            error: () => { this.error.set(ERROR_MESSAGES.LABEL.LOAD_FAILED); this.loading.set(false); },
        });
    }

    toggleCreateForm(): void {
        this.showCreateForm.set(!this.showCreateForm());
        this.editingLabel.set(null);
        this.newLabel = { name: '' };
    }

    startEdit(label: LabelResponseModel): void {
        this.editingLabel.set(label);
        this.newLabel = { name: label.name, color: label.color, groupName: label.groupName };
        this.showCreateForm.set(true);
    }

    saveLabel(): void {
        if (!this.newLabel.name.trim()) return;

        const editing = this.editingLabel();
        const onSuccess = () => { this.showCreateForm.set(false); this.editingLabel.set(null); this.loadLabels(); };
        const onError = () => this.error.set(ERROR_MESSAGES.LABEL.SAVE_FAILED);

        if (editing) {
            this.labelService.updateLabel(editing.id, this.newLabel).subscribe({ next: onSuccess, error: onError });
        } else {
            this.labelService.createLabel(this.newLabel).subscribe({ next: onSuccess, error: onError });
        }
    }

    deleteLabel(label: LabelResponseModel): void {
        if (!confirm(`Delete label "${label.name}"?`)) return;
        this.labelService.deleteLabel(label.id).subscribe({
            next: () => this.loadLabels(),
            error: () => this.error.set(ERROR_MESSAGES.LABEL.DELETE_FAILED),
        });
    }
}
