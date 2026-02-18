import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwIconComponent } from 'angular-material-wrap';
import { HouseholdService } from '../../../household/services/household.service';

interface HouseholdSummary {
  id: number;
  name: string;
  memberCount: number;
  memberNames: string[];
  isOwner: boolean;
}

@Component({
  selector: 'nom-sidebar-household-context',
  standalone: true,
  imports: [CommonModule, RouterModule, AmwIconComponent],
  templateUrl: './sidebar-household-context.component.html',
  styleUrls: ['./sidebar-household-context.component.scss'],
})
export class SidebarHouseholdContextComponent implements OnInit {
  private householdService = inject(HouseholdService);

  households = signal<HouseholdSummary[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.loadHouseholds();
  }

  private loadHouseholds(): void {
    this.householdService.getHouseholds().subscribe({
      next: (households) => {
        this.households.set(
          households.map((h) => ({
            id: h.id,
            name: h.name,
            memberCount: h.memberCount || 0,
            memberNames: (h.members || []).map((m) => m.personName || m.name || 'Member'),
            isOwner: h.isOwner,
          }))
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
