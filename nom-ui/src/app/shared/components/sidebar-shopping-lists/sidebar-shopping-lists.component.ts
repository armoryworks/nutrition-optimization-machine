import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwIconComponent } from 'angular-material-wrap';
import { ShoppingListService } from '../../../shopping/services/shopping-list.service';

interface ShoppingListSummary {
  id: number;
  name: string;
  itemCount: number;
  completedItemCount: number;
  progressPercent: number;
}

@Component({
  selector: 'nom-sidebar-shopping-lists',
  standalone: true,
  imports: [CommonModule, RouterModule, AmwIconComponent],
  templateUrl: './sidebar-shopping-lists.component.html',
  styleUrls: ['./sidebar-shopping-lists.component.scss'],
})
export class SidebarShoppingListsComponent implements OnInit {
  private shoppingListService = inject(ShoppingListService);

  lists = signal<ShoppingListSummary[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.loadActiveLists();
  }

  private loadActiveLists(): void {
    this.shoppingListService.getActiveShoppingLists().subscribe({
      next: (lists) => {
        this.lists.set(
          lists.map((l) => ({
            id: l.id,
            name: l.name,
            itemCount: l.itemCount || 0,
            completedItemCount: l.completedItemCount || 0,
            progressPercent: l.itemCount > 0
              ? Math.round((l.completedItemCount / l.itemCount) * 100)
              : 0,
          }))
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
