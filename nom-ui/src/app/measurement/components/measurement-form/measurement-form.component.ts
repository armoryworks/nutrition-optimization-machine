import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';

import { AmwButtonComponent, AmwCardComponent, AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwIconComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { MeasurementService } from '../../services/measurement.service';
import { MeasurementModel } from '../../models/measurement.model';

@Component({
    selector: 'app-measurement-form',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwCardComponent,
        AmwInputComponent,
        AmwSelectComponent,
        AmwTextareaComponent,
        AmwIconComponent,
        AmwValidationTooltipDirective,
    ],
    templateUrl: './measurement-form.component.html',
    styleUrl: './measurement-form.component.scss'
})
export class MeasurementFormComponent implements OnInit, OnDestroy {
    private fb = inject(FormBuilder);
    private measurementService = inject(MeasurementService);
    private validationService = inject(AmwValidationService);

    measurementForm: FormGroup;
    validationContext!: ValidationContext;
    isSubmitting = signal(false);
    error = signal<string | null>(null);

    measurementTypes = [
        { value: 'Mass', label: 'Mass' },
        { value: 'Volume', label: 'Volume' },
        { value: 'Length', label: 'Length' },
        { value: 'Time', label: 'Time' },
        { value: 'Temperature', label: 'Temperature' }
    ];

    constructor() {
        this.measurementForm = this.fb.group({
            name: ['', [Validators.required, Validators.maxLength(50)]],
            abbreviation: ['', [Validators.required, Validators.maxLength(10)]],
            measurementType: ['', Validators.required],
            conversionFactor: [1, [Validators.required, Validators.min(0)]],
            description: ['', [Validators.maxLength(200)]]
        });
    }

    ngOnInit(): void {
        // TODO: Load measurement data if editing

        // Setup ValidationContext
        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Name validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-required',
            message: 'Name is required',
            severity: 'error',
            field: 'name',
            control: this.measurementForm.get('name') ?? undefined,
            validator: () => !this.measurementForm.get('name')?.hasError('required')
        });

        // Name validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'name-maxlength',
            message: 'Name must be 50 characters or less',
            severity: 'error',
            field: 'name',
            control: this.measurementForm.get('name') ?? undefined,
            validator: () => !this.measurementForm.get('name')?.hasError('maxlength')
        });

        // Abbreviation validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'abbreviation-required',
            message: 'Abbreviation is required',
            severity: 'error',
            field: 'abbreviation',
            control: this.measurementForm.get('abbreviation') ?? undefined,
            validator: () => !this.measurementForm.get('abbreviation')?.hasError('required')
        });

        // Abbreviation validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'abbreviation-maxlength',
            message: 'Abbreviation must be 10 characters or less',
            severity: 'error',
            field: 'abbreviation',
            control: this.measurementForm.get('abbreviation') ?? undefined,
            validator: () => !this.measurementForm.get('abbreviation')?.hasError('maxlength')
        });

        // Measurement type validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'measurementType-required',
            message: 'Measurement type is required',
            severity: 'error',
            field: 'measurementType',
            control: this.measurementForm.get('measurementType') ?? undefined,
            validator: () => !this.measurementForm.get('measurementType')?.hasError('required')
        });

        // Conversion factor validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'conversionFactor-required',
            message: 'Conversion factor is required',
            severity: 'error',
            field: 'conversionFactor',
            control: this.measurementForm.get('conversionFactor') ?? undefined,
            validator: () => !this.measurementForm.get('conversionFactor')?.hasError('required')
        });

        // Conversion factor validation - min
        this.validationService.addViolation(this.validationContext.id, {
            id: 'conversionFactor-min',
            message: 'Conversion factor must be at least 0',
            severity: 'error',
            field: 'conversionFactor',
            control: this.measurementForm.get('conversionFactor') ?? undefined,
            validator: () => !this.measurementForm.get('conversionFactor')?.hasError('min')
        });

        // Description validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'description-maxlength',
            message: 'Description must be 200 characters or less',
            severity: 'error',
            field: 'description',
            control: this.measurementForm.get('description') ?? undefined,
            validator: () => !this.measurementForm.get('description')?.hasError('maxlength')
        });
    }

    ngOnDestroy(): void {
        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    onSubmit(): void {
        if (this.measurementForm.valid) {
            this.isSubmitting.set(true);
            this.error.set(null);

            const measurementData = this.measurementForm.value;

            // TODO: Implement create/update logic
            console.log('Measurement data:', measurementData);

            this.isSubmitting.set(false);
        }
    }

    onReset(): void {
        this.measurementForm.reset({
            conversionFactor: 1
        });
        this.error.set(null);
    }

    onCancel(): void {
        // TODO: Implement cancel logic
        console.log('Cancel clicked');
    }
}

