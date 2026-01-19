import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';

import { AmwButtonComponent, AmwInputComponent, AmwTextareaComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { RecipeService } from '../../services/recipe.service';
import { RecipeNoteResponseModel } from '../../models/recipe-note.model';

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
        AmwProgressSpinnerComponent,
    ],
    templateUrl: './recipe-notes.component.html',
    styleUrls: ['./recipe-notes.component.scss']
})
export class RecipeNotesComponent implements OnInit, OnDestroy {
    private recipeService = inject(RecipeService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private snackBar = inject(MatSnackBar);

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
    }

    ngOnDestroy(): void {
        this.destroy$.next();
        this.destroy$.complete();
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
                    this.error.set("Failed to load notes. Please try again.");
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
                    this.snackBar.open("Note added successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error creating note:", error);
                    this.error.set("Failed to create note. Please try again.");
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
                    this.snackBar.open("Note updated successfully", "Close", { duration: 3000 });
                    this.isSubmitting = false;
                },
                error: (error) => {
                    console.error("Error updating note:", error);
                    this.error.set("Failed to update note. Please try again.");
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
                    this.snackBar.open("Note deleted successfully", "Close", { duration: 3000 });
                },
                error: (error) => {
                    console.error("Error deleting note:", error);
                    this.snackBar.open("Failed to delete note", "Close", { duration: 3000 });
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