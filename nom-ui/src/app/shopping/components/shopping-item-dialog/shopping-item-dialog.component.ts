import { Component, OnInit, inject, OnDestroy, signal } from '@angular/core';

import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Subject, takeUntil } from 'rxjs';

import { AmwInputComponent, AmwSelectComponent, AmwCheckboxComponent, AmwButtonComponent } from 'angular-material-wrap';

import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { ReferenceSelectorComponent } from '../../../common/components/reference-selector/reference-selector.component';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

import { ShoppingItemDialogData } from './shopping-item-dialog-data.interface';
import { ShoppingItemFormData } from './shopping-item-form-data.interface';

@Component({
    selector: 'nom-shopping-item-dialog',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        MatDialogModule,
        AmwInputComponent,
        AmwSelectComponent,
        AmwCheckboxComponent,
        AmwButtonComponent,
        ReferenceSelectorComponent
    ],
    templateUrl: './shopping-item-dialog.component.html',
    styleUrls: ['./shopping-item-dialog.component.scss']
})
export class ShoppingItemDialogComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private dialogRef = inject<MatDialogRef<ShoppingItemDialogComponent>>(MatDialogRef);
    private shoppingReferenceService = inject(ShoppingReferenceService);
    private configurationService = inject(ConfigurationService);
    data = inject<ShoppingItemDialogData>(MAT_DIALOG_DATA);

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

        if (this.data.mode === 'edit' && this.data.item) {
            this.itemForm.patchValue(this.data.item);
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

            this.dialogRef.close(formData);
        }
    }

    onCancel(): void {
        this.dialogRef.close();
    }

    getTitle(): string {
        return this.data.mode === 'add' ? 'Add Item' : 'Edit Item';
    }

    getSubmitText(): string {
        return this.data.mode === 'add' ? 'Add' : 'Update';
    }

    getUnitOptions(): Array<{value: string, label: string}> {
        return this.units().map(u => ({ value: u, label: u }));
    }
} 