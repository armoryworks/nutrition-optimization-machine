import { Component, OnInit, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';

import { AmwButtonComponent, AmwCardComponent, AmwInputComponent, AmwSelectComponent, AmwTextareaComponent, AmwIconComponent } from 'angular-material-wrap';

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
    ],
    templateUrl: './measurement-form.component.html',
    styleUrl: './measurement-form.component.scss'
})
export class MeasurementFormComponent implements OnInit {
    private fb = inject(FormBuilder);
    private measurementService = inject(MeasurementService);

    measurementForm: FormGroup;
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

