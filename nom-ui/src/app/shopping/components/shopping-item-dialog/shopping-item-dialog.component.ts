import { Component, OnInit, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { Subject, takeUntil } from 'rxjs';
import { ShoppingReferenceService } from '../../services/shopping-reference.service';
import { ConfigurationService } from '../../../common/services/configuration.service';
import { ReferenceSelectorComponent } from '../../../common/components/reference-selector/reference-selector.component';
import { REFERENCE_IDS } from '../../../common/constants/reference-ids';

export interface ShoppingItemDialogData {
    mode: 'add' | 'edit';
    item?: any;
}

export interface ShoppingItemFormData {
    name: string;
    description: string;
    quantity: number;
    unit: string;
    category: string;
    priority: string;
    isCompleted: boolean;
}

@Component({
    selector: 'nom-shopping-item-dialog',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatDialogModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatSelectModule,
        MatCheckboxModule,
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
    isSubmitting = false;

    // Reference data loaded dynamically
    categories: any[] = [];
    units: string[] = [];
    priorities: any[] = [];

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
                this.categories = categories;
                this.priorities = priorities;
            });

        // Load measurement units from configuration service
        this.loadMeasurementUnits();
    }

    private loadMeasurementUnits(): void {
        // Use configuration service for standard units
        this.units = this.configurationService.getShoppingUnits();
    }

    onSubmit(): void {
        if (this.itemForm.valid) {
            this.isSubmitting = true;
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
} 