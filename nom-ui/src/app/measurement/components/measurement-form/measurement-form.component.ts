import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MeasurementService } from '../../services/measurement.service';
import { MeasurementModel } from '../../models/measurement.model';

@Component({
    selector: 'app-measurement-form',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatSelectModule,
        MatCardModule,
        MatIconModule
    ],
    templateUrl: './measurement-form.component.html',
    styleUrl: './measurement-form.component.scss'
})
export class MeasurementFormComponent implements OnInit {
    private fb = inject(FormBuilder);
    private measurementService = inject(MeasurementService);

    measurementForm: FormGroup;
    isSubmitting = false;
    error: string | null = null;

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
            this.isSubmitting = true;
            this.error = null;

            const measurementData = this.measurementForm.value;

            // TODO: Implement create/update logic
            console.log('Measurement data:', measurementData);

            this.isSubmitting = false;
        }
    }

    onReset(): void {
        this.measurementForm.reset({
            conversionFactor: 1
        });
        this.error = null;
    }

    onCancel(): void {
        // TODO: Implement cancel logic
        console.log('Cancel clicked');
    }
}

