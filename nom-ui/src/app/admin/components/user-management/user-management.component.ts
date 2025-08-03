// File: nom-ui/src/app/admin/components/user-management/user-management.component.ts

import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'nom-user-management',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './user-management.component.html',
  styleUrls: ['./user-management.component.scss']
})
export class UserManagementComponent {
  // Logic for User Role Managers to view users and toggle their admin claims.
}