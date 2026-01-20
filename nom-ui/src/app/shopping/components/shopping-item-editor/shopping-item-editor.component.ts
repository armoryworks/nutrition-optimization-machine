import { Component, OnInit, inject, OnDestroy, signal, effect } from '@angular/core';

import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { AmwInputComponent, AmwSelectComponent, AmwCheckboxComponent, AmwButtonComponent } from 'angular-material-wrap';

import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { ReferenceSelectorComponent } from '../../../common/components/reference-selector/reference-selector.component';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

import { ShoppingItemFormData } from './shopping-item-form-data.interface';

@Component({
    selector: 'nom-shopping-item-editor',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwInputComponent,
        AmwSelectComponent,
        AmwCheckboxComponent,
        AmwButtonComponent,
        ReferenceSelectorComponent
    ],
    templateUrl: './shopping-item-editor.component.html',
    styleUrls: ['./shopping-item-editor.component.scss']
})
export class ShoppingItemEditorComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private shoppingReferenceService = inject(ShoppingReferenceService);
    private configurationService = inject(ConfigurationService);

    // Input signals for data (set by parent via instance)
    mode = signal<'add' | 'edit'>('add');
    item = signal<any>(null);

    // Signal-based outputs for container communication
    confirmed = signal<ShoppingItemFormData | null>(null);
    cancelled = signal(false);

    itemForm: FormGroup;
    isSubmitting = signal(false);

    // Reference data loaded dynamically
    categories = signal<any[]>([]);
    units = signal<string[]>([]);
    priorities = signal<any[]>([]);

    // Make constants available in template
    readonly REFERENCE_IDS = REFERENCE_IDS;

    private destroy$ = new Subject<void>();

    constructor() {
        this.itemForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(255)]],
            description: ['', [Validators.maxLength(1000)]],
            quantity: [1, [Validators.required, Validators.min(0.01)]],
            unit: ['pieces'],
            categoryId: [null, Validators.required],
            priorityId: [null, Validators.required],
            isCompleted: [false]
        });
    }

    ngOnInit(): void {
        this.loadReferenceData();

        // Populate form if editing existing item
        if (this.mode() === 'edit' && this.item()) {
            this.itemForm.patchValue(this.item());
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    private loadReferenceData(): void {
        // Load shopping categories and priorities in bulk
        this.shoppingReferenceService.getShoppingReferencesBulk()
            .pipe(takeUntil(this.destroy$))
            .subscribe(({ categories, priorities }) => {
                this.categories.set(categories);
                this.priorities.set(priorities);
            });

        // Load measurement units from configuration service
        this.loadMeasurementUnits();
    }

    private loadMeasurementUnits(): void {
        // Use configuration service for standard units
        this.units.set(this.configurationService.getShoppingUnits());
    }

    onSubmit(): void {
        if (this.itemForm.valid) {
            this.isSubmitting.set(true);
            const formData: ShoppingItemFormData = this.itemForm.value;
            this.confirmed.set(formData);
        }
    }

    onCancel(): void {
        this.cancelled.set(true);
    }

    getTitle(): string {
        return this.mode() === 'add' ? 'Add Item' : 'Edit Item';
    }

    getSubmitText(): string {
        return this.mode() === 'add' ? 'Add' : 'Update';
    }

    getUnitOptions(): Array<{value: string, label: string}> {
        return this.units().map(u => ({ value: u, label: u }));
    }
}
