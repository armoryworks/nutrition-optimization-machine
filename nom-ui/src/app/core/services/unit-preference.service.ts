import { Injectable, effect, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { PersonService } from './person.service';
import { UnitSystem } from '../utils/mass-display';

export const UNIT_SYSTEM_ATTRIBUTE = 'Unit System';
const STORAGE_KEY = 'nom-unit-system';

function readSaved(): UnitSystem {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'imperial' ? 'imperial' : 'metric';
  } catch {
    return 'metric';
  }
}

/**
 * The signed-in person's measurement system. Metric unless their profile says imperial;
 * the last value is kept locally so the first paint doesn't flip.
 */
@Injectable({ providedIn: 'root' })
export class UnitPreferenceService {
  private auth = inject(AuthService);
  private persons = inject(PersonService);
  private systemSignal = signal<UnitSystem>(readSaved());
  private loadedFor: number | null = null;

  readonly system = this.systemSignal.asReadonly();

  constructor() {
    effect(() => {
      const personId = this.auth.personId();
      if (!personId || personId === this.loadedFor) return;
      this.loadedFor = personId;
      this.persons.getCurrentPerson().subscribe({
        next: (person) => {
          const value = person.attributes?.find((a) => a.attributeTypeName === UNIT_SYSTEM_ATTRIBUTE)?.value;
          this.set(value === 'imperial' ? 'imperial' : 'metric');
        },
        error: () => undefined,
      });
    });
  }

  set(system: UnitSystem): void {
    this.systemSignal.set(system);
    try {
      localStorage.setItem(STORAGE_KEY, system);
    } catch {
      return;
    }
  }
}
