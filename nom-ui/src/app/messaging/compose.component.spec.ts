import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { Compose } from './compose.component';
import { HouseholdStore } from '../core/services/household-store';
import { MessagingService } from '../core/services/messaging.service';
import { AuthService } from '../core/services/auth.service';
import { HouseholdResponseModel } from '../core/models/household-response.model';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';

function member(personId: number, personName: string, hasAccount = true): HouseholdMemberResponseModel {
  return {
    id: personId * 10,
    householdId: 3,
    personId,
    personName,
    personEmail: null,
    role: 'Member',
    joinedDate: '2026-10-01T00:00:00Z',
    isActive: true,
    hasProfile: false,
    hasRestrictions: false,
    hasAccount,
  };
}

function household(members: HouseholdMemberResponseModel[] | null, id = 3): HouseholdResponseModel {
  return {
    id,
    name: 'Rivera',
    description: null,
    householdGroupId: 1,
    createdDate: '2026-10-01T00:00:00Z',
    modifiedDate: null,
    members,
    memberCount: members?.length ?? 0,
    recipeCount: 0,
    planCount: 0,
    shoppingListCount: 0,
  };
}

describe('Compose recipients', () => {
  let createThread: ReturnType<typeof vi.fn>;
  let invalidate: ReturnType<typeof vi.fn>;
  let getHouseholds: ReturnType<typeof vi.fn>;

  function create(households: Observable<HouseholdResponseModel[]>, me: Observable<number | null> = of(7)) {
    createThread = vi.fn(() => of({ threadId: 42 }));
    invalidate = vi.fn();
    getHouseholds = vi.fn(() => households);
    TestBed.configureTestingModule({
      imports: [Compose],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: HouseholdStore, useValue: { getHouseholds, invalidate } },
        { provide: MessagingService, useValue: { createThread } },
        { provide: AuthService, useValue: { ensurePersonId: () => me } },
      ],
    });
    const fixture = TestBed.createComponent(Compose);
    fixture.detectChanges();
    return fixture;
  }

  function query(fixture: ReturnType<typeof create>, testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  it('offers the other members with accounts, never the sender', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam'), member(9, 'Ana')])]));
    expect(fixture.componentInstance.recipients().map(m => m.personName)).toEqual(['Sam', 'Ana']);
    expect(query(fixture, 'compose-form')).not.toBeNull();
    expect(query(fixture, 'compose-empty')).toBeNull();
  });

  it('lists each person once across several households', () => {
    const fixture = create(of([
      household([member(7, 'Liz'), member(8, 'Sam')], 3),
      household([member(7, 'Liz'), member(8, 'Sam'), member(9, 'Ana')], 4),
    ]));
    expect(fixture.componentInstance.recipients().map(m => m.personId)).toEqual([8, 9]);
  });

  it('keeps dependents without an account out of the recipients and explains why', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam'), member(12, 'Diego', false)])]));
    expect(fixture.componentInstance.recipients().map(m => m.personName)).toEqual(['Sam']);
    expect(fixture.componentInstance.unreachable().map(m => m.personName)).toEqual(['Diego']);
    expect(query(fixture, 'compose-recipients-hint')?.textContent).toContain("can't receive messages");
  });

  it('shows an empty state for a household of one', () => {
    const fixture = create(of([household([member(7, 'Liz')])]));
    expect(query(fixture, 'compose-empty')).not.toBeNull();
    expect(query(fixture, 'compose-form')).toBeNull();
    expect(query(fixture, 'compose-empty-household-btn')).not.toBeNull();
  });

  it('names the dependents when nobody else can receive messages', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(12, 'Diego', false)])]));
    expect(query(fixture, 'compose-empty-no-accounts')?.textContent).toContain('Diego');
  });

  it('shows the empty state when the household list carries no members', () => {
    const fixture = create(of([household(null)]));
    expect(query(fixture, 'compose-empty')).not.toBeNull();
  });

  it('waits for the household list before deciding there is no one to message', () => {
    const pending = new Subject<HouseholdResponseModel[]>();
    const fixture = create(pending);
    expect(query(fixture, 'compose-loading')).not.toBeNull();
    expect(query(fixture, 'compose-empty')).toBeNull();
    pending.next([household([member(7, 'Liz'), member(8, 'Sam')])]);
    pending.complete();
    fixture.detectChanges();
    expect(query(fixture, 'compose-form')).not.toBeNull();
  });

  it('sends recipients with the first message and opens the new thread', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam')])]));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const component = fixture.componentInstance;
    component.composeForm.setValue({ recipientIds: [8], message: '  Tacos tonight?  ' });

    component.onSubmit();

    expect(createThread).toHaveBeenCalledWith({ participantIds: [8], initialMessage: 'Tacos tonight?' });
    expect(navigate).toHaveBeenCalledWith(['/messages', 42]);
  });

  it('does not send a blank message', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam')])]));
    const component = fixture.componentInstance;
    component.composeForm.setValue({ recipientIds: [8], message: '   ' });

    component.onSubmit();

    expect(createThread).not.toHaveBeenCalled();
  });

  it('shows the server reason when a recipient is refused', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam')])]));
    createThread.mockReturnValue(throwError(() => ({ status: 400, error: { detail: 'You can only message household members who have their own account.' } })));
    const component = fixture.componentInstance;
    component.composeForm.setValue({ recipientIds: [8], message: 'hi' });

    component.onSubmit();
    fixture.detectChanges();

    expect(query(fixture, 'compose-error')?.textContent).toContain('their own account');
  });

  it('drops the cached household list so members who joined from another device are offered', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam')])]));
    expect(invalidate).toHaveBeenCalledBefore(getHouseholds);
    expect(fixture.componentInstance.recipients().map(m => m.personName)).toEqual(['Sam']);
  });

  it('waits for the sender id before listing anyone, so the sender is never offered', () => {
    const me = new Subject<number | null>();
    const fixture = create(of([household([member(7, 'Liz')])]), me);
    expect(query(fixture, 'compose-loading')).not.toBeNull();
    expect(query(fixture, 'compose-form')).toBeNull();
    me.next(7);
    me.complete();
    fixture.detectChanges();
    expect(query(fixture, 'compose-empty')).not.toBeNull();
  });

  it('says the household could not be loaded and loads again on retry', () => {
    const fixture = create(throwError(() => ({ status: 500 })));
    fixture.detectChanges();
    expect(query(fixture, 'compose-load-error')?.textContent).toContain("Couldn't load your household");
    expect(query(fixture, 'compose-form')).toBeNull();

    getHouseholds.mockReturnValue(of([household([member(7, 'Liz'), member(8, 'Sam')])]));
    (query(fixture, 'error-banner-retry-btn') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(query(fixture, 'compose-load-error')).toBeNull();
    expect(query(fixture, 'compose-form')).not.toBeNull();
  });

  it('treats an unknown sender as a load failure rather than listing them', () => {
    const fixture = create(of([household([member(7, 'Liz'), member(8, 'Sam')])]), of(null));
    fixture.detectChanges();
    expect(query(fixture, 'compose-load-error')).not.toBeNull();
  });
});
