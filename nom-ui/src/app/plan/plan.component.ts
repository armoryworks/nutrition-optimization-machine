import { Component, inject, input, output, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';
import { toLocalDateString } from '../core/utils/local-date';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { PlanService } from '../core/services/plan.service';
import { MealPlanService } from '../core/services/meal-plan.service';
import { HouseholdStore } from '../core/services/household-store';
import { LoadingService } from '../core/services/loading.service';
import { PlanModel } from '../core/models/plan.model';
import { HouseholdResponseModel } from '../core/models/household-response.model';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';
import { MealPlanWeekResponse } from '../core/models/meal-plan-week-response.model';
import { MealPlanDay } from '../core/models/meal-plan-day.model';
import { MealPlanCell } from '../core/models/meal-plan-cell.model';
import { MealPlanEntry } from '../core/models/meal-plan-entry.model';
import { MealPlanExclusion } from '../core/models/meal-plan-exclusion.model';
import { RestrictionRequest } from '../core/models/restriction-request.model';
import { RecipeSearchDialog, RecipeSearchDialogData, RecipeSearchDialogResult } from './recipe-search-dialog/recipe-search-dialog.component';
import { ShuffleFlowService } from './shuffle-flow.service';

export interface PlanFormData {
  planName: string | null;
  planDescription: string | null;
  startDate: string | null;
  endDate: string | null;
  applyRestrictions: boolean;
  invitationCode: string | null;
}

@Component({
  selector: 'nom-plan',
  imports: [
    DecimalPipe,
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule, ErrorBanner],
  templateUrl: './plan.component.html',
  styleUrl: './plan.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Plan implements OnInit {
  mode = input<'standalone' | 'wizard'>('standalone');
  initialRestrictions = input<RestrictionRequest[]>([]);

  stepComplete = output<PlanFormData>();
  skipped = output<void>();
  saved = output<PlanFormData>();

  private fb = inject(FormBuilder);
  private planService = inject(PlanService);
  private mealPlanService = inject(MealPlanService);
  private shuffleFlow = inject(ShuffleFlowService);
  private householdStore = inject(HouseholdStore);
  private loadingService = inject(LoadingService);
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

  // Wizard mode state
  plans = signal<PlanModel[]>([]);
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  activeTab = signal<'create' | 'join'>('create');

  // Calendar mode state
  households = signal<HouseholdResponseModel[]>([]);
  activeHouseholdId = signal<number | null>(null);
  weekData = signal<MealPlanWeekResponse | null>(null);
  currentWeekStart = signal<string>(Plan.getMonday(new Date()));
  members = signal<HouseholdMemberResponseModel[]>([]);
  shuffling = signal(false);

  // Computed
  isStandalone = computed(() => this.mode() !== 'wizard');
  hasRestrictions = computed(() => this.initialRestrictions().length > 0);
  hasHousehold = computed(() => this.households().length > 0);

  weekLabel = computed(() => {
    const data = this.weekData();
    if (!data) return '';
    const start = new Date(data.weekStart + 'T00:00:00');
    const end = new Date(data.weekEnd + 'T00:00:00');
    const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
    return `${start.toLocaleDateString(undefined, opts)} – ${end.toLocaleDateString(undefined, opts)}, ${end.getFullYear()}`;
  });

  isCurrentWeek = computed(() => {
    return this.currentWeekStart() === Plan.getMonday(new Date());
  });

  // Wizard mode forms
  createForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(255)]],
    description: [''],
    startDate: [new Date()],
    endDate: [null as Date | null],
    applyRestrictions: [true],
  });

  joinForm = this.fb.group({
    invitationCode: ['', Validators.required],
  });

  ngOnInit(): void {
    if (this.isStandalone()) {
      this.loadHouseholds();
    }
  }

  // --- Print ---

  printPlan(): void {
    window.print();
  }

  // --- Calendar navigation ---

  navigateWeek(direction: -1 | 1): void {
    const current = new Date(this.currentWeekStart() + 'T00:00:00');
    current.setDate(current.getDate() + direction * 7);
    this.currentWeekStart.set(Plan.toDateString(current));
    this.loadWeek();
  }

  goToToday(): void {
    this.currentWeekStart.set(Plan.getMonday(new Date()));
    this.loadWeek();
  }

  onHouseholdChange(householdId: number): void {
    this.activeHouseholdId.set(householdId);
    const household = this.households().find(h => h.id === householdId);
    this.members.set(household?.members ?? []);
    this.loadWeek();
  }

  onCellClick(day: MealPlanDay, cell: MealPlanCell): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    const dialogRef = this.dialog.open(RecipeSearchDialog, {
      width: '560px',
      data: {
        householdId,
        date: day.date,
        mealTypeId: cell.mealTypeId,
        mealType: cell.mealType,
        entries: cell.entries,
      } as RecipeSearchDialogData,
    });

    dialogRef.afterClosed().subscribe((result: RecipeSearchDialogResult) => {
      if (result?.changed) this.loadWeek();
    });
  }

  shuffleEmptySlots(): void {
    const householdId = this.activeHouseholdId();
    const data = this.weekData();
    if (!householdId || !data) return;

    const today = Plan.toDateString(new Date());
    const futureDays = data.days.filter(d => d.date >= today);
    if (futureDays.length === 0) return;

    this.shuffleFlow.run({
      householdId,
      days: futureDays,
      startDate: futureDays[0].date,
      endDate: futureDays[futureDays.length - 1].date,
      onShuffleStart: () => this.shuffling.set(true),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        this.weekData.set(response.week);
        this.shuffling.set(false);
      },
      error: () => { this.shuffling.set(false); },
    });
  }

  formatDayHeader(dateStr: string): string {
    const date = new Date(dateStr + 'T00:00:00');
    return date.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' });
  }

  isToday(dateStr: string): boolean {
    return dateStr === Plan.toDateString(new Date());
  }

  // --- Exclusion controls ---

  isExcluded(day: MealPlanDay, member: HouseholdMemberResponseModel): boolean {
    return day.exclusions.some(e => e.personId === member.personId && e.mealTypeId === null);
  }

  getExclusionForMember(day: MealPlanDay, member: HouseholdMemberResponseModel): MealPlanExclusion | undefined {
    return day.exclusions.find(e => e.personId === member.personId && e.mealTypeId === null);
  }

  toggleExclusion(day: MealPlanDay, member: HouseholdMemberResponseModel): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    const existing = this.getExclusionForMember(day, member);
    if (existing) {
      this.mealPlanService.deleteExclusion(existing.id).pipe(
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(() => this.loadWeek());
    } else {
      this.mealPlanService.createExclusion({
        householdId,
        personId: member.personId,
        date: day.date,
        mealTypeId: null,
      }).pipe(
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(() => this.loadWeek());
    }
  }

  getMemberInitial(member: HouseholdMemberResponseModel): string {
    return member.personName.charAt(0).toUpperCase();
  }

  // --- Nutrition helpers ---

  getDayCalories(day: MealPlanDay): number {
    return day.cells.reduce((sum, c) => sum + (c.totalCalories ?? 0), 0);
  }

  getDayProtein(day: MealPlanDay): number {
    return day.cells.reduce((sum, c) => sum + (c.totalProteinGrams ?? 0), 0);
  }

  getDayCarbs(day: MealPlanDay): number {
    return day.cells.reduce((sum, c) => sum + (c.totalCarbGrams ?? 0), 0);
  }

  getDayFat(day: MealPlanDay): number {
    return day.cells.reduce((sum, c) => sum + (c.totalFatGrams ?? 0), 0);
  }

  hasDayNutrition(day: MealPlanDay): boolean {
    return day.cells.some(c => c.totalCalories != null);
  }

  // --- Wizard mode methods (unchanged) ---

  onCreateSubmit(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }
    const form = this.createForm.getRawValue();

    const data: PlanFormData = {
      planName: form.name,
      planDescription: form.description || null,
      startDate: form.startDate ? Plan.toDateString(form.startDate) : null,
      endDate: form.endDate ? Plan.toDateString(form.endDate) : null,
      applyRestrictions: form.applyRestrictions ?? false,
      invitationCode: null,
    };

    if (this.isStandalone()) {
      this.createPlan(data);
    } else {
      this.stepComplete.emit(data);
    }
  }

  onJoinSubmit(): void {
    if (this.joinForm.invalid) {
      this.joinForm.markAllAsTouched();
      return;
    }
    const code = this.joinForm.getRawValue().invitationCode!;

    const data: PlanFormData = {
      planName: null,
      planDescription: null,
      startDate: null,
      endDate: null,
      applyRestrictions: false,
      invitationCode: code,
    };

    if (this.isStandalone()) {
      this.successMessage.set('Plan invitation code saved.');
      this.saved.emit(data);
    } else {
      this.stepComplete.emit(data);
    }
  }

  onSkip(): void {
    this.skipped.emit();
  }

  // --- Private methods ---

  private loadHouseholds(): void {
    this.householdStore.getHouseholds().pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (list) => {
        this.households.set(list);
        if (list.length > 0) {
          this.activeHouseholdId.set(list[0].id);
          this.members.set(list[0].members ?? []);
          this.loadWeek();
        }
      },
      error: () => {},
    });
  }

  private loadWeek(): void {
    const householdId = this.activeHouseholdId();
    if (!householdId) return;

    this.loading.set(true);
    this.mealPlanService.getWeek(householdId, this.currentWeekStart()).pipe(
      this.loadingService.loading('Loading meal plan...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (data) => {
        this.weekData.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to load meal plan.');
      },
    });
  }

  private createPlan(data: PlanFormData): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.planService.createPlan({
      name: data.planName!,
      description: data.planDescription,
      startDate: data.startDate!,
      endDate: data.endDate,
      goals: [],
      meals: [],
      restrictions: data.applyRestrictions
        ? this.initialRestrictions().map(r => ({
            id: 0,
            name: r.name,
            description: r.description,
            restrictionType: null,
            ingredientName: null,
            nutrientName: null,
          }))
        : [],
    }).pipe(
      this.loadingService.loading('Creating plan...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.successMessage.set('Plan created successfully.');
        this.createForm.reset({ startDate: new Date(), applyRestrictions: true });
        this.saved.emit(data);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to create plan. Please try again.');
      },
    });
  }

  // --- Meal completion ---

  completeMeal(event: Event, entry: MealPlanEntry): void {
    event.stopPropagation(); // Prevent cell click from opening recipe dialog

    this.mealPlanService.completeMealPlan(entry.id).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        // Update the entry in-place to show completed state
        entry.completedDate = Plan.toDateString(new Date());
      },
      error: () => {
        this.errorMessage.set('Failed to mark meal as cooked.');
      },
    });
  }

  // --- Static helpers ---

  static getMonday(date: Date): string {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return Plan.toDateString(d);
  }

  static toDateString(date: Date): string {
    return toLocalDateString(date);
  }
}
