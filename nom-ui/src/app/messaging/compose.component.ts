import { Component, inject, signal, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MessagingService } from '../core/services/messaging.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';
import { HouseholdMemberResponseModel } from '../core/models/household.model';

@Component({
  selector: 'nom-compose',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="nom-form--full">
      <div class="nom-form__header">
        <h1 class="nom-form__title">New Message</h1>
        <p class="nom-form__subtitle">Send a message to household members</p>
      </div>

      @if (errorMessage()) {
        <div class="nom-form__error">
          <mat-icon>error_outline</mat-icon>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      <form [formGroup]="composeForm" (ngSubmit)="onSubmit()">
        <div class="nom-form__section">
          <div class="nom-form__fields">
            <mat-form-field appearance="outline">
              <mat-label>To</mat-label>
              <mat-select formControlName="recipientIds" multiple>
                @for (m of members(); track m.personId) {
                  <mat-option [value]="m.personId">{{ m.personName }}</mat-option>
                }
              </mat-select>
              @if (composeForm.get('recipientIds')?.hasError('required')) {
                <mat-error>Select at least one recipient</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Subject</mat-label>
              <input matInput formControlName="subject" maxlength="255" />
              @if (composeForm.get('subject')?.hasError('required')) {
                <mat-error>Subject is required</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Message</mat-label>
              <textarea matInput formControlName="message" rows="6"></textarea>
              @if (composeForm.get('message')?.hasError('required')) {
                <mat-error>Message is required</mat-error>
              }
            </mat-form-field>
          </div>
        </div>

        <div class="nom-form__actions">
          <a mat-button routerLink="/messages">Cancel</a>
          <button mat-flat-button type="submit" [disabled]="composeForm.invalid || sending()">
            @if (sending()) {
              <mat-spinner diameter="20"></mat-spinner>
            } @else {
              <ng-container>
                <mat-icon>send</mat-icon>
                Send
              </ng-container>
            }
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [],
})
export class Compose implements OnInit {
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private messagingService = inject(MessagingService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);

  members = signal<HouseholdMemberResponseModel[]>([]);
  sending = signal(false);
  errorMessage = signal('');

  composeForm = this.fb.group({
    recipientIds: [[] as number[], Validators.required],
    subject: ['', [Validators.required, Validators.maxLength(255)]],
    message: ['', Validators.required],
  });

  ngOnInit(): void {
    this.householdService.getHouseholds().subscribe({
      next: (list) => {
        if (list.length > 0 && list[0].members) {
          this.members.set(list[0].members);
        }
      },
    });
  }

  onSubmit(): void {
    if (this.composeForm.invalid || this.sending()) return;
    this.sending.set(true);
    this.errorMessage.set('');

    const form = this.composeForm.getRawValue();
    this.messagingService.createThread({
      participantPersonIds: form.recipientIds!,
      subject: form.subject!,
      initialMessage: form.message!,
    }).pipe(
      this.loadingService.loading('Sending message...')
    ).subscribe({
      next: (result) => {
        this.sending.set(false);
        this.router.navigate(['/messages', result.id]);
      },
      error: () => {
        this.sending.set(false);
        this.errorMessage.set('Failed to send message. Please try again.');
      },
    });
  }
}
