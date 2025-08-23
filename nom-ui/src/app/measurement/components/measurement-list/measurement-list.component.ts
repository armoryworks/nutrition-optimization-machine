import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ReactiveFormsModule } from '@angular/forms';
import { MeasurementService } from '../../services/measurement.service';
import { MeasurementModel } from '../../models/measurement.model';

@Component({
    selector: 'app-measurement-list',
    standalone: true,
    imports: [
        CommonModule,
        MatTableModule,
        MatButtonModule,
        MatIconModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        ReactiveFormsModule
    ],
    templateUrl: './measurement-list.component.html',
    styleUrl: './measurement-list.component.scss'
})
export class MeasurementListComponent implements OnInit {
    private measurementService = inject(MeasurementService);

    measurements: MeasurementModel[] = [];
    isLoading = false;
    error: string | null = null;
    displayedColumns: string[] = ['name', 'abbreviation', 'type', 'conversionFactor', 'actions'];

    ngOnInit(): void {
        this.loadMeasurements();
    }

    private loadMeasurements(): void {
        this.isLoading = true;
        this.error = null;

        this.measurementService.getAllMeasurements().subscribe({
            next: (measurements) => {
                this.measurements = measurements;
                this.isLoading = false;
            },
            error: (error) => {
                console.error('Error loading measurements:', error);
                this.error = 'Failed to load measurements';
                this.isLoading = false;
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

