import { Component, OnInit, OnDestroy, inject, signal, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { Subject, takeUntil } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeTimelineEventResponseModel, RecipeTimelineEventCreateModel } from '../../models/recipe-timeline-event.model';
import { RecipeAdvancedService } from '../../services/recipe-advanced.service';

@Component({
    selector: 'nom-recipe-timeline-events',
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatChipsModule,
        MatDividerModule,
        MatSelectModule,
        MatDatepickerModule,
        MatNativeDateModule,
        MatDialogModule,
        MatListModule,
        MatMenuModule,
    ],
    templateUrl: './recipe-timeline-events.component.html',
    styleUrls: ['./recipe-timeline-events.component.scss']
})
export class RecipeTimelineEventsComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private recipeAdvancedService = inject(RecipeAdvancedService);
    private router = inject(Router);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);
    private dialog = inject(MatDialog);
    private destroy$ = new Subject<void>();

    recipeId = input.required<number>();

    timelineEvents = signal<RecipeTimelineEventResponseModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    timelineEventForm: FormGroup;
    isSubmitting = signal(false);

    eventTypes = [
        { id: 1, name: "First Made" },
        { id: 2, name: "Modified" },
        { id: 3, name: "Shared" },
        { id: 4, name: "Rated" },
        { id: 5, name: "Added to Meal Plan" },
        { id: 6, name: "Added to Shopping List" },
        { id: 7, name: "Custom Event" }
    ];

    constructor() {
        this.timelineEventForm = this.nonNullableFb.group({
            eventTypeId: ['', [Validators.required]],
            eventTitle: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
            eventDescription: ['', [Validators.maxLength(2047)]],
            eventDate: [new Date(), [Validators.required]]
        });
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadTimelineEvents();
        }
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
    }

    loadTimelineEvents(): void {
        this.isLoading.set(true);
        this.error.set(null);
        this.recipeAdvancedService
            .getRecipeTimelineEvents(this.recipeId())
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (timelineEvents) => {
                    this.timelineEvents.set(timelineEvents.sort((a, b) =>
                        new Date(b.eventDate || b.createdDate).getTime() -
                        new Date(a.eventDate || a.createdDate).getTime()
                    ));
                    this.isLoading.set(false);
                },
                error: (error) => {
                    console.error("Error loading timeline events:", error);
                    this.error.set("Failed to load timeline events. Please try again.");
                    this.isLoading.set(false);
                },
            });
    }

    createTimelineEvent(): void {
        if (this.timelineEventForm.invalid || this.isSubmitting()) {
            return;
        }

        this.isSubmitting.set(true);
        this.error.set(null);
        const request: RecipeTimelineEventCreateModel = {
            recipeId: this.recipeId(),
            eventTypeId: this.timelineEventForm.get("eventTypeId")!.value,
            eventTitle: this.timelineEventForm.get("eventTitle")!.value,
            eventDescription: this.timelineEventForm.get("eventDescription")!.value || undefined,
            eventDate: this.timelineEventForm.get("eventDate")!.value || undefined,
        };

        this.recipeAdvancedService
            .createTimelineEvent(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (timelineEvent) => {
                    this.timelineEvents.set([timelineEvent, ...this.timelineEvents()]);
                    this.timelineEventForm.reset();
                    this.snackBar.open("Timeline event added successfully", "Close", { duration: 3000 });
                    this.isSubmitting.set(false);
                },
                error: (error) => {
                    console.error("Error creating timeline event:", error);
                    this.error.set("Failed to create timeline event. Please try again.");
                    this.isSubmitting.set(false);
                },
            });
    }

    deleteTimelineEvent(eventId: number): void {
        this.recipeAdvancedService
            .deleteTimelineEvent(eventId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.timelineEvents.set(this.timelineEvents().filter(e => e.id !== eventId));
                    this.snackBar.open("Timeline event deleted successfully", "Close", { duration: 3000 });
                },
                error: (error) => {
                    console.error("Error deleting timeline event:", error);
                    this.snackBar.open("Failed to delete timeline event", "Close", { duration: 3000 });
                },
            });
    }

    getEventTypeName(eventTypeId: number): string {
        const eventType = this.eventTypes.find(et => et.id === eventTypeId);
        return eventType ? eventType.name : "Unknown Event";
    }

    getEventIcon(eventTypeId: number): string {
        switch (eventTypeId) {
            case 1: return "restaurant"; // First Made
            case 2: return "edit"; // Modified
            case 3: return "share"; // Shared
            case 4: return "star"; // Rated
            case 5: return "calendar_today"; // Added to Meal Plan
            case 6: return "shopping_cart"; // Added to Shopping List
            case 7: return "event"; // Custom Event
            default: return "event";
        }
    }

    formatDate(dateString: string): string {
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    formatEventDate(dateString: string | undefined): string {
        if (!dateString) return "No date specified";
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    }
} 