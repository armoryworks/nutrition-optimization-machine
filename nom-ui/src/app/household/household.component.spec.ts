import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { Household } from './household.component';
import { HouseholdService } from '../core/services/household.service';
import { HouseholdStore } from '../core/services/household-store';
import { AuthService } from '../core/services/auth.service';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';

describe('Household member changes', () => {
  let householdStore: { getHouseholds: ReturnType<typeof vi.fn>; invalidate: ReturnType<typeof vi.fn> };
  let dialogResult: number | undefined;

  function create() {
    householdStore = {
      getHouseholds: vi.fn(() => of([])),
      invalidate: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [Household],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        {
          provide: HouseholdService,
          useValue: {
            removeMember: vi.fn(() => of(undefined)),
            getHousehold: vi.fn(() => of({ id: 3, members: [] })),
          },
        },
        { provide: HouseholdStore, useValue: householdStore },
        {
          provide: AuthService,
          useValue: { personId: signal(7), username: signal('liz@example.com'), displayName: signal('Liz') },
        },
      ],
    });
    TestBed.overrideProvider(MatDialog, { useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) } });
    const component = TestBed.createComponent(Household).componentInstance;
    component.activeHouseholdId.set(3);
    return component;
  }

  it('drops the cached household list after removing a member', () => {
    const component = create();
    component.removeMember({ id: 11, personId: 12 } as HouseholdMemberResponseModel);
    expect(householdStore.invalidate).toHaveBeenCalledTimes(1);
  });

  it('drops the cached household list after adding a member', () => {
    dialogResult = 12;
    const component = create();
    component.openAddMemberDialog();
    expect(householdStore.invalidate).toHaveBeenCalledTimes(1);
  });

  it('keeps the cached household list when adding a member is cancelled', () => {
    dialogResult = undefined;
    const component = create();
    component.openAddMemberDialog();
    expect(householdStore.invalidate).not.toHaveBeenCalled();
  });
});
