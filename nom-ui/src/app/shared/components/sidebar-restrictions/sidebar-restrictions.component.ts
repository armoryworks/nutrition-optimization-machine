import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AmwIconComponent } from 'angular-material-wrap';
import { PersonService } from '../../../person/services/person.service';

@Component({
  selector: 'nom-sidebar-restrictions',
  standalone: true,
  imports: [CommonModule, RouterModule, AmwIconComponent],
  templateUrl: './sidebar-restrictions.component.html',
  styleUrls: ['./sidebar-restrictions.component.scss'],
})
export class SidebarRestrictionsComponent implements OnInit {
  private personService = inject(PersonService);

  personName = signal<string>('');
  restrictions = signal<string[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.loadRestrictions();
  }

  private loadRestrictions(): void {
    this.personService.getCurrentPerson().subscribe({
      next: (person) => {
        if (person) {
          this.personName.set(person.name);
          this.restrictions.set(
            (person.attributes || []).map((a) => a.value)
          );
        }
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
