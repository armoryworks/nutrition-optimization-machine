import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { MeasurementListComponent } from './components/measurement-list/measurement-list.component';
import { MeasurementFormComponent } from './components/measurement-form/measurement-form.component';
import { MeasurementCategoryListComponent } from './components/measurement-category-list/measurement-category-list.component';
import { MeasurementCategoryFormComponent } from './components/measurement-category-form/measurement-category-form.component';

const routes: Routes = [
    {
        path: '',
        children: [
            { path: '', redirectTo: 'list', pathMatch: 'full' },
            { path: 'list', component: MeasurementListComponent },
            { path: 'new', component: MeasurementFormComponent },
            { path: 'edit/:id', component: MeasurementFormComponent },
            { path: 'categories', component: MeasurementCategoryListComponent },
            { path: 'categories/new', component: MeasurementCategoryFormComponent },
            { path: 'categories/edit/:id', component: MeasurementCategoryFormComponent }
        ]
    }
];

@NgModule({
    imports: [RouterModule.forChild(routes)],
    exports: [RouterModule]
})
export class MeasurementRoutingModule { }

