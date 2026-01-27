import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { NotificationService } from '../../../utilities/services/notification.service';

import { AmwButtonComponent, AmwInputComponent, AmwTextareaComponent, AmwCardComponent, AmwIconComponent, AmwInlineLoadingComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { RecipeService } from '../../services/recipe.service';
import { RecipeNoteResponseModel } from '../../models/recipe-note.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
    selector: 'nom-recipe-notes',
    standalone: true,
    imports: [
        ReactiveFormsModule,
        AmwButtonComponent,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwCardComponent,
        AmwIconComponent,
        AmwInlineLoadingComponent,
        AmwValidationTooltipDirective,
    ],
    templateUrl: './recipe-notes.component.html',
    styleUrls: ['./recipe-notes.component.scss']
})
export class RecipeNotesComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);
    private validationService = inject(AmwValidationService);

    validationContext!: ValidationContext;
    notes = signal<RecipeNoteResponseModel[]>([]);
    isLoading = signal(false);
    error = signal<string | null>(null);
    noteForm: FormGroup;
    isAddingNote = signal(false);

    constructor() {
        this.noteForm = this.nonNullableFb.group({
            noteTitle: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]],
            noteText: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(2047)]]
        });
    }

    ngOnInit(): void {
        if (this.recipeId) {
            this.loadNotes();
        }

        // Setup ValidationContext
        this.validationContext = this.validationService.createContext({
            disableOnErrors: true
        });

        // Note title validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteTitle-required',
            message: 'Note title is required',
            severity: 'error',
            field: 'noteTitle',
            control: this.noteForm.get('noteTitle') ?? undefined,
            validator: () => !this.noteForm.get('noteTitle')?.hasError('required')
        });

        // Note title validation - minLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteTitle-minlength',
            message: 'Note title must be at least 2 characters',
            severity: 'error',
            field: 'noteTitle',
            control: this.noteForm.get('noteTitle') ?? undefined,
            validator: () => !this.noteForm.get('noteTitle')?.hasError('minlength')
        });

        // Note title validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteTitle-maxlength',
            message: 'Note title must be 255 characters or less',
            severity: 'error',
            field: 'noteTitle',
            control: this.noteForm.get('noteTitle') ?? undefined,
            validator: () => !this.noteForm.get('noteTitle')?.hasError('maxlength')
        });

        // Note text validation - required
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteText-required',
            message: 'Note content is required',
            severity: 'error',
            field: 'noteText',
            control: this.noteForm.get('noteText') ?? undefined,
            validator: () => !this.noteForm.get('noteText')?.hasError('required')
        });

        // Note text validation - minLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteText-minlength',
            message: 'Note content must be at least 2 characters',
            severity: 'error',
            field: 'noteText',
            control: this.noteForm.get('noteText') ?? undefined,
            validator: () => !this.noteForm.get('noteText')?.hasError('minlength')
        });

        // Note text validation - maxLength
        this.validationService.addViolation(this.validationContext.id, {
            id: 'noteText-maxlength',
            message: 'Note content must be 2047 characters or less',
            severity: 'error',
            field: 'noteText',
            control: this.noteForm.get('noteText') ?? undefined,
            validator: () => !this.noteForm.get('noteText')?.hasError('maxlength')
        });
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();

        if (this.validationContext) {
            this.validationService.destroyContext(this.validationContext.id);
        }
    }

    loadNotes(): void {
        this.isLoading.set(true);
        this.error.set(null);
        this.recipeAdvancedService
            .getRecipeNotes(this.recipeId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (notes) => {
                    this.notes.set(notes.sort((a, b) =>
                        new Date(b.createdDate).getTime() - new Date(a.createdDate).getTime()
                    ));
                    this.isLoading.set(false);
                },
                error: (error) => {
                    console.error("Error loading notes:", error);
                    this.error.set(ERROR_MESSAGES.RECIPE.LOAD_FAILED);
                    this.isLoading.set(false);
                },
            });
    }

    createNote(): void {
        if (this.noteForm.invalid || this.isSubmitting) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;
        const request: RecipeNoteCreateModel = {
            recipeId: this.recipeId,
            noteTitle: this.noteForm.get("noteTitle")!.value,
            noteText: this.noteForm.get("noteText")!.value || undefined,
            isPublic: this.noteForm.get("isPublic")!.value,
        };

        this.recipeAdvancedService
            .createNote(request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (note) => {
                    this.notes.set([note, ...this.notes()]);
                    this.noteForm.reset();
                    this.notificationService.success("Note added successfully");
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating note:", error);
                    this.error.set(ERROR_MESSAGES.RECIPE.SAVE_FAILED);
                    this.isSubmitting = false;
                },
            });
    }

    startEditNote(note: RecipeNoteModel): void {
        this.editingNoteId = note.id;
        this.noteForm.patchValue({
            noteTitle: note.noteTitle,
            noteText: note.noteText || "",
            isPublic: note.isPublic,
        });
    }

    cancelEdit(): void {
        this.editingNoteId = null;
        this.noteForm.reset();
    }

    updateNote(): void {
        if (this.noteForm.invalid || this.isSubmitting || !this.editingNoteId) {
            return;
        }

        this.isSubmitting = true;
        this.error = null;
        const request: RecipeNoteCreateModel = {
            recipeId: this.recipeId,
            noteTitle: this.noteForm.get("noteTitle")!.value,
            noteText: this.noteForm.get("noteText")!.value || undefined,
            isPublic: this.noteForm.get("isPublic")!.value,
        };

        this.recipeAdvancedService
            .updateNote(this.editingNoteId, request)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: (updatedNote) => {
                    const index = this.notes().findIndex(n => n.id === this.editingNoteId);
                    if (index !== -1) {
                        const updatedNotes = [...this.notes()];
                        updatedNotes[index] = updatedNote;
                        this.notes.set(updatedNotes);
                    }
                    this.editingNoteId = null;
                    this.noteForm.reset();
                    this.notificationService.success("Note updated successfully");
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error updating note:", error);
                    this.error.set(ERROR_MESSAGES.RECIPE.SAVE_FAILED);
                    this.isSubmitting = false;
                },
            });
    }

    deleteNote(noteId: number): void {
        this.recipeAdvancedService
            .deleteNote(noteId)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: () => {
                    this.notes.set(this.notes().filter(n => n.id !== noteId));
                    this.notificationService.success("Note deleted successfully");
                },
                error: (error) => {
                    console.error("Error deleting note:", error);
                    this.notificationService.error(ERROR_MESSAGES.RECIPE.DELETE_FAILED);
                },
            });
    }

    isEditing(noteId: number): boolean {
        return this.editingNoteId === noteId;
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
} 