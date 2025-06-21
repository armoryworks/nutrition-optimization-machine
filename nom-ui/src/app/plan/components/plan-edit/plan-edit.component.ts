import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { RestrictionEditComponent } from '../../../restriction/components/restriction-edit/restriction-edit.component';
import { RestrictionModel } from '../../../restriction/models/restriction.model';
import { PersonModel } from '../../../person/models/person.model';

@Component({
  selector: 'app-plan-edit',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    RestrictionEditComponent, // Import RestrictionEditComponent for reuse
  ],
  templateUrl: './plan-edit.component.html',
  styleUrls: ['./plan-edit.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class PlanEditComponent implements OnInit {
  @Input() currentPlanId: number | null = null;
  @Input() existingRestrictions: RestrictionModel[] = []; // Input restrictions for the plan
  @Input() allPersonsInPlan: PersonModel[] = []; // All persons for restriction allocation

  @Output() formSubmitted = new EventEmitter<RestrictionModel[]>();
  @Output() skipStep = new EventEmitter<void>();

  constructor() {}

  ngOnInit(): void {
    // Initialization logic for plan details if this component expands beyond just hosting restrictions
  }

  /**
   * Handles the submission from the nested RestrictionEditComponent.
   * Emits the restrictions up to the parent workflow.
   */
  onRestrictionsSubmitted(restrictions: RestrictionModel[]): void {
    this.formSubmitted.emit(restrictions);
  }

  onSkip(): void {
    this.skipStep.emit();
  }
}
