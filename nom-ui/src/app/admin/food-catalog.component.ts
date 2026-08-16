import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FoodCatalogService } from '../core/services/food-catalog.service';
import { MealPlanService } from '../core/services/meal-plan.service';
import { FoodGroup } from '../core/models/food-group.model';
import {
  CurationStatus,
  FoodCatalogFinding,
  FoodCatalogItem,
  FoodProposal,
} from '../core/models/food-catalog.model';

/**
 * Admin review of the imported food catalog (FDC and authored): browse what was staged,
 * see what the deterministic audit flagged, fix classification inline, and promote reviewed
 * foods to Curated so meal planning can use them. Also lists reviewer proposals awaiting a
 * decision — proposals never change the catalog until an admin approves them.
 */
@Component({
  selector: 'nom-food-catalog',
  imports: [
    FormsModule,
    DecimalPipe,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './food-catalog.component.html',
  styleUrl: './food-catalog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FoodCatalog implements OnInit {
  private catalog = inject(FoodCatalogService);
  private mealPlanService = inject(MealPlanService);
  private destroyRef = inject(DestroyRef);

  readonly CurationStatus = CurationStatus;

  readonly sources = [
    { value: null, label: 'All sources' },
    { value: 'foundation_food', label: 'FDC Foundation' },
    { value: 'branded_food', label: 'FDC Branded' },
    { value: 'authored', label: 'Authored' },
  ];

  readonly statuses = [
    { value: null, label: 'Any status' },
    { value: CurationStatus.PendingCuration, label: 'Pending review' },
    { value: CurationStatus.Curated, label: 'Curated (in use)' },
    { value: CurationStatus.Rejected, label: 'Rejected' },
  ];

  // Filters
  source = signal<string | null>('foundation_food');
  status = signal<number | null>(null);
  foodGroupId = signal<number | null>(null);
  search = signal('');
  page = signal(1);
  readonly pageSize = 50;

  foodGroups = signal<FoodGroup[]>([]);
  items = signal<FoodCatalogItem[]>([]);
  total = signal(0);
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  selected = signal<Set<number>>(new Set());

  // Audit
  auditRunning = signal(false);
  findings = signal<FoodCatalogFinding[]>([]);
  auditExamined = signal(0);

  // Proposals
  proposals = signal<FoodProposal[]>([]);
  proposalsLoading = signal(false);

  totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  selectedCount = computed(() => this.selected().size);

  /** Audit codes keyed by ingredient id, so rows can show why they were flagged. */
  findingsByIngredient = computed(() => {
    const map = new Map<number, FoodCatalogFinding[]>();
    for (const f of this.findings()) {
      const list = map.get(f.ingredientId) ?? [];
      list.push(f);
      map.set(f.ingredientId, list);
    }
    return map;
  });

  ngOnInit(): void {
    this.mealPlanService
      .getFoodGroups()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.foodGroups.set(g) });
    this.load();
    this.loadProposals();
  }

  load(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.catalog
      .getPage({
        source: this.source(),
        status: this.status(),
        foodGroupId: this.foodGroupId(),
        search: this.search().trim() || null,
        page: this.page(),
        pageSize: this.pageSize,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.total.set(res.total);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.errorMessage.set('Unable to load the food catalog.');
        },
      });
  }

  onFilterChange(): void {
    this.page.set(1);
    this.selected.set(new Set());
    this.load();
  }

  goToPage(delta: number): void {
    const next = Math.min(Math.max(1, this.page() + delta), this.totalPages());
    if (next === this.page()) return;
    this.page.set(next);
    this.load();
  }

  toggle(id: number): void {
    const next = new Set(this.selected());
    if (!next.delete(id)) next.add(id);
    this.selected.set(next);
  }

  toggleAll(checked: boolean): void {
    this.selected.set(checked ? new Set(this.items().map((i) => i.id)) : new Set());
  }

  runAudit(): void {
    this.auditRunning.set(true);
    this.catalog
      .audit(this.source())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.findings.set(res.findings);
          this.auditExamined.set(res.examined);
          this.auditRunning.set(false);
        },
        error: () => {
          this.auditRunning.set(false);
          this.errorMessage.set('Audit failed.');
        },
      });
  }

  flagsFor(id: number): FoodCatalogFinding[] {
    return this.findingsByIngredient().get(id) ?? [];
  }

  setGroup(item: FoodCatalogItem, foodGroupId: number | null): void {
    this.catalog
      .update(item.id, { foodGroupId: foodGroupId ?? 0 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => this.replace(updated),
        error: () => this.errorMessage.set('Could not update the food group.'),
      });
  }

  setWholeFood(item: FoodCatalogItem, isWholeFood: boolean): void {
    this.catalog
      .update(item.id, { isWholeFood })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => this.replace(updated),
        error: () => this.errorMessage.set('Could not update the food.'),
      });
  }

  promoteSelected(statusId: number): void {
    const ids = [...this.selected()];
    if (ids.length === 0) return;
    this.catalog
      .setCurationStatus(ids, statusId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.successMessage.set(
            statusId === CurationStatus.Curated
              ? `${res.updated} food(s) approved — meal planning can now use them.`
              : `${res.updated} food(s) updated.`,
          );
          this.selected.set(new Set());
          this.load();
        },
        error: () => this.errorMessage.set('Could not update those foods.'),
      });
  }

  exportUrl(): string {
    return this.catalog.exportUrl(this.source(), this.status());
  }

  loadProposals(): void {
    this.proposalsLoading.set(true);
    this.catalog
      .getProposals()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          this.proposals.set(p);
          this.proposalsLoading.set(false);
        },
        error: () => this.proposalsLoading.set(false),
      });
  }

  applyProposal(p: FoodProposal): void {
    this.catalog
      .applyProposal(p.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.proposals.set(this.proposals().filter((x) => x.id !== p.id));
          this.load();
        },
        error: () => this.errorMessage.set('That proposal could not be applied.'),
      });
  }

  rejectProposal(p: FoodProposal): void {
    this.catalog
      .rejectProposal(p.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.proposals.set(this.proposals().filter((x) => x.id !== p.id)),
        error: () => this.errorMessage.set('That proposal could not be rejected.'),
      });
  }

  private replace(updated: FoodCatalogItem): void {
    this.items.set(this.items().map((i) => (i.id === updated.id ? updated : i)));
  }
}
