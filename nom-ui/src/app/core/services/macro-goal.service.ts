import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MacroGoal, EffectiveMacroGoal } from '../models/macro-goal.model';

@Injectable({ providedIn: 'root' })
export class MacroGoalService {
  private http = inject(HttpClient);
  private readonly personUrl = `${environment.apiUrl}/Person`;
  private readonly householdUrl = `${environment.apiUrl}/Household`;

  getPersonGoal(personId: number): Observable<MacroGoal> {
    return this.http.get<MacroGoal>(`${this.personUrl}/${personId}/macro-goals`);
  }

  savePersonGoal(personId: number, goal: MacroGoal): Observable<MacroGoal> {
    return this.http.put<MacroGoal>(`${this.personUrl}/${personId}/macro-goals`, goal);
  }

  getEffectiveForPerson(personId: number): Observable<EffectiveMacroGoal> {
    return this.http.get<EffectiveMacroGoal>(`${this.personUrl}/${personId}/macro-goals/effective`);
  }

  getHouseholdGoal(householdId: number): Observable<MacroGoal> {
    return this.http.get<MacroGoal>(`${this.householdUrl}/${householdId}/macro-goals`);
  }

  saveHouseholdGoal(householdId: number, goal: MacroGoal): Observable<MacroGoal> {
    return this.http.put<MacroGoal>(`${this.householdUrl}/${householdId}/macro-goals`, goal);
  }
}
