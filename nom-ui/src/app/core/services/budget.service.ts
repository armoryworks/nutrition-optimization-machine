import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Budget, EffectiveBudget } from '../models/budget.model';

@Injectable({ providedIn: 'root' })
export class BudgetService {
  private http = inject(HttpClient);
  private readonly personUrl = `${environment.apiUrl}/Person`;
  private readonly householdUrl = `${environment.apiUrl}/Household`;

  getPersonBudget(personId: number): Observable<Budget> {
    return this.http.get<Budget>(`${this.personUrl}/${personId}/budget`);
  }

  savePersonBudget(personId: number, budget: Budget): Observable<Budget> {
    return this.http.put<Budget>(`${this.personUrl}/${personId}/budget`, budget);
  }

  getEffectiveForPerson(personId: number): Observable<EffectiveBudget> {
    return this.http.get<EffectiveBudget>(`${this.personUrl}/${personId}/budget/effective`);
  }

  getHouseholdBudget(householdId: number): Observable<Budget> {
    return this.http.get<Budget>(`${this.householdUrl}/${householdId}/budget`);
  }

  saveHouseholdBudget(householdId: number, budget: Budget): Observable<Budget> {
    return this.http.put<Budget>(`${this.householdUrl}/${householdId}/budget`, budget);
  }
}
