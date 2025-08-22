import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { MeasurementRoutingModule } from './measurement-routing.module';
import { MeasurementListComponent } from './components/measurement-list/measurement-list.component';
import { MeasurementFormComponent } from './components/measurement-form/measurement-form.component';
import { MeasurementConverterComponent } from './components/measurement-converter/measurement-converter.component';
import { MeasurementCategoryListComponent } from './components/measurement-category-list/measurement-category-list.component';
import { MeasurementCategoryFormComponent } from './components/measurement-category-form/measurement-category-form.component';

@NgModule({
    declarations: [
        MeasurementListComponent,
        MeasurementFormComponent,
        MeasurementConverterComponent,
        MeasurementCategoryListComponent,
        MeasurementCategoryFormComponent
    ],
    imports: [
        CommonModule,
        FormsModule,
        ReactiveFormsModule,
        RouterModule,
        MeasurementRoutingModule
    ],
    exports: [
        MeasurementConverterComponent
    ]
})
export class MeasurementModule { }

