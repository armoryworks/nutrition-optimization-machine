import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { AmwButtonComponent, AmwInputComponent, AmwSelectComponent } from 'angular-material-wrap';

import { MeasurementService } from '../../services/measurement.service';
import { MeasurementCategoryService } from '../../services/measurement-category.service';
import { MeasurementModel, MeasurementCategoryModel } from '../../models/measurement.model';

@Component({
    selector: 'app-measurement-converter',
    standalone: true,
    imports: [
        DecimalPipe,
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwSelectComponent,
    ],
    templateUrl: './measurement-converter.component.html',
    styleUrls: ['./measurement-converter.component.scss']
})
export class MeasurementConverterComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private measurementService = inject(MeasurementService);
    private categoryService = inject(MeasurementCategoryService);

    converterForm: FormGroup;
    categories = signal<MeasurementCategoryModel[]>([]);
    fromMeasurements = signal<MeasurementModel[]>([]);
    toMeasurements = signal<MeasurementModel[]>([]);
    result = signal<number | null>(null);
    isLoading = signal(false);
    error = signal<string | null>(null);

    private destroy$ = new Subject<void>();

    constructor() {
        this.converterForm = this.fb.group({
            fromCategoryId: ['', Validators.required],
            fromMeasurementId: ['', Validators.required],
            toCategoryId: ['', Validators.required],
            toMeasurementId: ['', Validators.required],
            value: ['', [Validators.required, Validators.min(0)]]
        });
    }

    ngOnInit(): void {
        this.loadCategories();
        this.setupFormListeners();
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    private loadCategories(): void {
        this.categoryService.getCategories()
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (categories) => {
                    this.categories.set(categories);
                },
                error: (error) => {
                    this.error.set('Failed to load measurement categories');
                    console.error('Error loading categories:', error);
                }
            });
    }

    private setupFormListeners(): void {
        // When from category changes, load its measurements
        this.converterForm.get('fromCategoryId')?.valueChanges
            .pipe(takeUntil(this.destroy$))
            .subscribe(categoryId => {
                if (categoryId) {
                    this.loadMeasurementsForCategory(categoryId, 'from');
                }
                this.converterForm.patchValue({ fromMeasurementId: '' });
            });

        // When to category changes, load its measurements
        this.converterForm.get('toCategoryId')?.valueChanges
            .pipe(takeUntil(this.destroy$))
            .subscribe(categoryId => {
                if (categoryId) {
                    this.loadMeasurementsForCategory(categoryId, 'to');
                }
                this.converterForm.patchValue({ toMeasurementId: '' });
            });
    }

    private loadMeasurementsForCategory(categoryId: number, type: 'from' | 'to'): void {
        this.measurementService.getMeasurementsByCategory(categoryId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (measurements) => {
                    if (type === 'from') {
                        this.fromMeasurements.set(measurements);
                    } else {
                        this.toMeasurements.set(measurements);
                    }
                },
                error: (error) => {
                    this.error.set(`Failed to load measurements for category`);
                    console.error('Error loading measurements:', error);
                }
            });
    }

    onConvert(): void {
        if (this.converterForm.valid) {
            const { fromMeasurementId, toMeasurementId, value } = this.converterForm.value;

            this.isLoading.set(true);
            this.error.set(null);
            this.result.set(null);

            this.measurementService.convertMeasurement(fromMeasurementId, toMeasurementId, value)
                .pipe(takeUntil(this.destroy$))
                .subscribe({
                    next: (result) => {
                        this.result.set(result);
                        this.isLoading.set(false);
                    },
                    error: (error) => {
                        this.error.set('Failed to convert measurement');
                        this.isLoading.set(false);
                        console.error('Error converting measurement:', error);
                    }
                });
        }
    }

    onSwapMeasurements(): void {
        const { fromCategoryId, fromMeasurementId, toCategoryId, toMeasurementId } = this.converterForm.value;

        this.converterForm.patchValue({
            fromCategoryId: toCategoryId,
            fromMeasurementId: toMeasurementId,
            toCategoryId: fromCategoryId,
            toMeasurementId: fromMeasurementId
        });
    }

    resetForm(): void {
        this.converterForm.reset();
        this.result.set(null);
        this.error.set(null);
        this.fromMeasurements.set([]);
        this.toMeasurements.set([]);
    }

    // Helper methods for AMW select options
    getCategoryOptions(): { value: string | number; label: string }[] {
        return [
            { value: '', label: 'Select Category' },
            ...this.categories().map(cat => ({ value: cat.id, label: cat.name }))
        ];
    }

    getFromMeasurementOptions(): { value: string | number; label: string }[] {
        return [
            { value: '', label: 'Select Unit' },
            ...this.fromMeasurements().map(m => ({ value: m.id, label: `${m.name} (${m.symbol})` }))
        ];
    }

    getToMeasurementOptions(): { value: string | number; label: string }[] {
        return [
            { value: '', label: 'Select Unit' },
            ...this.toMeasurements().map(m => ({ value: m.id, label: `${m.name} (${m.symbol})` }))
        ];
    }
}

