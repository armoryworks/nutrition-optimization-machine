import { Component, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MealPlanService } from '../core/services/meal-plan.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { MealPlanRule } from '../core/models/meal-plan.model';

@Component({
  selector: 'nom-plan-rules',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="nom-form--full">
      <div class="nom-form__header">
        <h1 class="nom-form__title">Meal Plan Rules</h1>
        <p class="nom-form__subtitle">Set constraints for automatic meal plan generation</p>
      </div>

      @if (errorMessage()) {
        <div class="nom-form__error">
          <mat-icon>error_outline</mat-icon>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      <!-- Existing rules -->
      <div class="nom-form__section">
        <h2 class="nom-form__section-title">Active Rules</h2>

        @if (loading()) {
          <div class="nom-plan-rules__loading">
            <mat-spinner diameter="32"></mat-spinner>
          </div>
        } @else if (rules().length === 0) {
          <p class="nom-plan-rules__empty">No rules yet. Add one below to constrain how recipes are assigned.</p>
        } @else {
          @for (rule of rules(); track rule.id) {
            <div class="nom-plan-rules__rule">
              <div class="nom-plan-rules__rule-info">
                <span class="nom-plan-rules__rule-filter">{{ rule.queryFilter || 'Any recipe' }}</span>
                <span class="nom-plan-rules__rule-meta">
                  @if (rule.mealTypeName) { {{ rule.mealTypeName }} }
                  @if (rule.dayOfWeekName) { on {{ rule.dayOfWeekName }} }
                  · Max {{ rule.maxRecipes }} recipes
                </span>
              </div>
              <button mat-icon-button (click)="deleteRule(rule.id)" [attr.aria-label]="'Delete rule'">
                <mat-icon>close</mat-icon>
              </button>
            </div>
          }
        }
      </div>

      <!-- Add rule form -->
      <div class="nom-form__section">
        <h2 class="nom-form__section-title">Add Rule</h2>
        <form [formGroup]="ruleForm" (ngSubmit)="onAddRule()">
          <div class="nom-form__fields">
            <mat-form-field appearance="outline">
              <mat-label>Recipe Filter</mat-label>
              <input matInput formControlName="queryFilter" placeholder="e.g. vegetarian, chicken, low-carb" />
              <mat-hint>Keywords to filter recipes by name or category</mat-hint>
            </mat-form-field>

            <div class="nom-form__field-row">
              <mat-form-field appearance="outline">
                <mat-label>Meal Type</mat-label>
                <mat-select formControlName="mealTypeId">
                  <mat-option [value]="null">Any meal</mat-option>
                  <mat-option [value]="1">Breakfast</mat-option>
                  <mat-option [value]="2">Lunch</mat-option>
                  <mat-option [value]="3">Dinner</mat-option>
                  <mat-option [value]="4">Snack</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Day of Week</mat-label>
                <mat-select formControlName="dayOfWeekId">
                  <mat-option [value]="null">Any day</mat-option>
                  <mat-option [value]="1">Monday</mat-option>
                  <mat-option [value]="2">Tuesday</mat-option>
                  <mat-option [value]="3">Wednesday</mat-option>
                  <mat-option [value]="4">Thursday</mat-option>
                  <mat-option [value]="5">Friday</mat-option>
                  <mat-option [value]="6">Saturday</mat-option>
                  <mat-option [value]="7">Sunday</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Max Recipes</mat-label>
                <input matInput type="number" formControlName="maxRecipes" min="1" max="10" />
              </mat-form-field>
            </div>

            <mat-checkbox formControlName="isActive">Active</mat-checkbox>
          </div>

          <div class="nom-form__actions">
            <a mat-button routerLink="/plan">Back to Plan</a>
            <button mat-flat-button type="submit" [disabled]="ruleForm.invalid || saving()">
              @if (saving()) {
                <mat-spinner diameter="20"></mat-spinner>
              } @else {
                Add Rule
              }
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-plan-rules__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-8;
    }

    .nom-plan-rules__empty {
      color: var(--mat-sys-on-surface-variant);
      font-size: vars.$font-size-sm;
      padding: vars.$spacing-4 0;
    }

    .nom-plan-rules__rule {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: vars.$spacing-3 vars.$spacing-4;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: vars.$nom-border-radius;
      margin-bottom: vars.$spacing-2;
      background: var(--mat-sys-surface-container-low);
    }

    .nom-plan-rules__rule-info {
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-1;
    }

    .nom-plan-rules__rule-filter {
      font-weight: vars.$font-weight-medium;
      color: var(--mat-sys-on-surface);
    }

    .nom-plan-rules__rule-meta {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
    }
  `],
})
export class PlanRules implements OnInit {
  private fb = inject(FormBuilder);
  private mealPlanService = inject(MealPlanService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);

  rules = signal<MealPlanRule[]>([]);
  loading = signal(true);
  saving = signal(false);
  errorMessage = signal('');
  householdId = signal(0);

  ruleForm = this.fb.group({
    queryFilter: ['', [Validators.maxLength(2047)]],
    mealTypeId: [null as number | null],
    dayOfWeekId: [null as number | null],
    maxRecipes: [3, [Validators.required, Validators.min(1), Validators.max(10)]],
    isActive: [true],
  });

  ngOnInit(): void {
    this.householdService.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.householdId.set(list[0].id);
          this.loadRules();
        } else {
          this.loading.set(false);
          this.errorMessage.set('Create a household first to manage rules.');
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load household.');
      },
    });
  }

  private loadRules(): void {
    this.loading.set(true);
    this.mealPlanService.getRules(this.householdId()).pipe(
      this.loadingService.loading('Loading rules...')
    ).subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Failed to load rules.');
      },
    });
  }

  onAddRule(): void {
    if (this.ruleForm.invalid || this.saving()) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const form = this.ruleForm.getRawValue();
    this.mealPlanService.createRule({
      householdId: this.householdId(),
      mealTypeId: form.mealTypeId,
      dayOfWeekId: form.dayOfWeekId,
      queryFilter: form.queryFilter ?? '',
      maxRecipes: form.maxRecipes!,
      isActive: form.isActive ?? true,
    }).subscribe({
      next: (rule) => {
        this.rules.update(r => [...r, rule]);
        this.ruleForm.reset({ maxRecipes: 3, isActive: true });
        this.saving.set(false);
      },
      error: () => {
        this.saving.set(false);
        this.errorMessage.set('Failed to create rule.');
      },
    });
  }

  deleteRule(id: number): void {
    this.mealPlanService.deleteRule(id).subscribe({
      next: () => this.rules.update(r => r.filter(rule => rule.id !== id)),
      error: () => this.errorMessage.set('Failed to delete rule.'),
    });
  }
}
