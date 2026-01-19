import { Component, OnInit, inject, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { ReactiveFormsModule } from '@angular/forms';

import { AmwButtonComponent, AmwCardComponent, AmwIconButtonComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { MeasurementService } from '../../services/measurement.service';
import { MeasurementModel } from '../../models/measurement.model';

@Component({
    selector: 'app-measurement-list',
    standalone: true,
    imports: [
        MatTableModule,
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconButtonComponent,
        AmwIconComponent,
        AmwProgressSpinnerComponent,
    ],
    templateUrl: './measurement-list.component.html',
    styleUrl: './measurement-list.component.scss'
})
export class MeasurementListComponent implements OnInit {
    private measurementService = inject(MeasurementService);

    measurements = signal<MeasurementModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    displayedColumns: string[] = ['name', 'abbreviation', 'type', 'conversionFactor', 'actions'];

    ngOnInit(): void {
        this.loadMeasurements();
    }

    private loadMeasurements(): void {
        this.isLoading.set(true);
        this.error.set(null);

        this.measurementService.getAllMeasurements().subscribe({
            next: (measurements) => {
                this.measurements.set(measurements);
                this.isLoading.set(false);
            },
            error: (error) => {
                console.error('Error loading measurements:', error);
                this.error.set('Failed to load measurements');
                this.isLoading.set(false);
            }
        });
    }

    onEdit(measurement: MeasurementModel): void {
        // TODO: Implement edit functionality
        console.log('Edit measurement:', measurement);
    }

    onDelete(measurement: MeasurementModel): void {
        // TODO: Implement delete functionality
        console.log('Delete measurement:', measurement);
    }

    onRefresh(): void {
        this.loadMeasurements();
    }
}

