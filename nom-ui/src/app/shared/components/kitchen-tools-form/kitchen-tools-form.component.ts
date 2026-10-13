import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { KitchenToolsService } from '../../../core/services/kitchen-tools.service';
import { KitchenTool, KitchenToolMode } from '../../../core/models/kitchen-tools.model';

/**
 * Editor for the household's kitchen: which tools it has (common ones start ticked,
 * esoteric ones unticked) and whether recipes needing a missing tool are hidden from
 * planning, shown with a warning, or left alone. Saves require household manage permission.
 */
@Component({
  selector: 'nom-kitchen-tools-form',
  imports: [MatButtonModule, MatButtonToggleModule, MatCheckboxModule],
  templateUrl: './kitchen-tools-form.component.html',
  styleUrl: './kitchen-tools-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KitchenToolsForm implements OnInit {
  householdId = input.required<number>();

  private kitchenService = inject(KitchenToolsService);
  private destroyRef = inject(DestroyRef);

  tools = signal<KitchenTool[]>([]);
  mode = signal<KitchenToolMode>('warn');
  loaded = signal(false);
  saving = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  readonly modes: { value: KitchenToolMode; label: string; testid: string }[] = [
    { value: 'warn', label: 'Show with a warning', testid: 'kitchen-tools-mode-warn' },
    { value: 'hide', label: 'Hide from meal plans', testid: 'kitchen-tools-mode-hide' },
    { value: 'ignore', label: "Don't check", testid: 'kitchen-tools-mode-ignore' },
  ];

  categories = computed(() => {
    const groups = new Map<string, KitchenTool[]>();
    for (const tool of this.tools()) {
      groups.set(tool.category, [...(groups.get(tool.category) ?? []), tool]);
    }
    return [...groups.entries()].map(([name, tools]) => ({ name, tools }));
  });

  ngOnInit(): void {
    this.kitchenService
      .getSettings(this.householdId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          this.tools.set(settings.tools);
          this.mode.set(settings.mode);
          this.loaded.set(true);
        },
        error: () => this.errorMessage.set('Unable to load your kitchen tools.'),
      });
  }

  toggle(tool: KitchenTool, owned: boolean): void {
    this.tools.update((list) => list.map((t) => (t.id === tool.id ? { ...t, owned } : t)));
    this.successMessage.set('');
  }

  setMode(mode: KitchenToolMode): void {
    this.mode.set(mode);
    this.successMessage.set('');
  }

  onSave(): void {
    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const update = {
      mode: this.mode(),
      tools: this.tools().map((t) => ({ toolId: t.id, owned: t.owned === t.ownedByDefault ? null : t.owned })),
    };

    this.kitchenService
      .saveSettings(this.householdId(), update)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          this.tools.set(settings.tools);
          this.mode.set(settings.mode);
          this.saving.set(false);
          this.successMessage.set('Kitchen saved.');
        },
        error: (err) => {
          this.saving.set(false);
          this.errorMessage.set(
            err.status === 403
              ? "You don't have permission to change the household's kitchen."
              : 'Unable to save your kitchen. Please try again.',
          );
        },
      });
  }
}
