import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ReactiveFormsModule, NonNullableFormBuilder, Validators } from "@angular/forms";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatButtonModule } from "@angular/material/button";
import { MatIconModule } from "@angular/material/icon";
import { MatDividerModule } from "@angular/material/divider";
import { MatChipsModule } from "@angular/material/chips";
import { MatSnackBar } from "@angular/material/snack-bar";
import { MatTooltipModule } from "@angular/material/tooltip";
import { MatDatepickerModule } from "@angular/material/datepicker";
import { MatNativeDateModule } from "@angular/material/core";
import { MatSelectModule } from "@angular/material/select";
import { Subject, takeUntil } from "rxjs";

import { RecipeAdvancedService } from "../../services/recipe-advanced.service";
import { RecipeTimelineEventModel, RecipeTimelineEventCreateModel } from "../../models/recipe-timeline-event.model";
import { BaseDetailComponent, BaseDetailConfig } from "../../../common/components/base-detail/base-detail.component";

@Component({
    selector: "nom-recipe-timeline-events",
    standalone: true,
    imports: [
        CommonModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatIconModule,
        MatDividerModule,
        MatChipsModule,
        MatTooltipModule,
        MatDatepickerModule,
        MatNativeDateModule,
        MatSelectModule,
        BaseDetailComponent,
    ],
    templateUrl: "./recipe-timeline-events.component.html",
    styleUrls: ["./recipe-timeline-events.component.scss"],
})
export class RecipeTimelineEventsComponent implements OnInit, OnDestroy {
    @Input() recipeId: number = 0;

    timelineEvents: RecipeTimelineEventModel[] = [];
    timelineEventForm = this.fb.group({
        eventTypeId: [0, [Validators.required, Validators.min(1)]],
        eventTitle: ["", [Validators.required, Validators.maxLength(511)]],
        eventDescription: ["", [Validators.maxLength(2047)]],
        eventDate: [null as Date | null],
    });

    // Mock event types - in a real app, these would come from a service
    eventTypes = [
        { id: 1, name: "First Made" },
        { id: 2, name: "Modified" },
        { id: 3, name: "Shared" },
        { id: 4, name: "Rated" },
        { id: 5, name: "Added to Meal Plan" },
        { id: 6, name: "Added to Shopping List" },
        { id: 7, name: "Custom Event" },
    ];

    isLoading = false;
    isSubmitting = false;
    error: string | null = null;
    private destroy$ = new Subject<void>();

    detailConfig: BaseDetailConfig = {
        title: 'Recipe Timeline',
        subtitle: 'Track important events and milestones for this recipe',
        showBackButton: false,
        maxWidth: '800px'
    };

    constructor(
        private fb: NonNullableFormBuilder,
        private recipeAdvancedService: RecipeAdvancedService,
        private snackBar: MatSnackBar
    ) { }

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
        this.isLoading = true;
        this.error = null;
        this.recipeAdvancedService
            .getRecipeTimelineEvents(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (timelineEvents) => {
                    this.timelineEvents = timelineEvents.sort((a, b) =>
                        new Date(b.eventDate || b.createdDate).getTime() -
                        new Date(a.eventDate || a.createdDate).getTime()
                    );
                    this.isLoading = false;
                },
                error: (error) => {
                    console.error("Error loading timeline events:", error);
                    this.error = "Failed to load timeline events. Please try again.";
                    this.isLoading = false;
                },
            });
    }

    createTimelineEvent(): void {
        if (this.timelineEventForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;
        const request: RecipeTimelineEventCreateModel = {
            recipeId: this.recipeId,
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
                    this.timelineEvents.unshift(timelineEvent);
                    this.timelineEventForm.reset();
                    this.snackBar.open("Timeline event added successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating timeline event:", error);
                    this.error = "Failed to create timeline event. Please try again.";
                    this.isSubmitting = false;
                },
            });
    }

    deleteTimelineEvent(eventId: number): void {
        this.recipeAdvancedService
            .deleteTimelineEvent(eventId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.timelineEvents = this.timelineEvents.filter(e => e.id !== eventId);
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