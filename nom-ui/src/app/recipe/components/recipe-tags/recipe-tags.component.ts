import { Component, OnInit, input, output, inject, signal, ViewEncapsulation } from '@angular/core';
import { FormsModule, ReactiveFormsModule, NonNullableFormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatChipsModule } from '@angular/material/chips';

import { AmwInputComponent, AmwTextareaComponent, AmwButtonComponent, AmwCardComponent, AmwIconButtonComponent, AmwTooltipDirective, AmwIconComponent, AmwProgressBarComponent } from 'angular-material-wrap';

import { NotificationService } from '../../../utilities/services/notification.service';
import { RecipeTagsService } from '../../services/recipe-tags.service';
import { RecipeTagModel } from '../../models/recipe-tag.model';

@Component({
    selector: 'nom-recipe-tags',
    standalone: true,
    imports: [
        FormsModule,
        ReactiveFormsModule,
        MatChipsModule,
        AmwInputComponent,
        AmwTextareaComponent,
        AmwButtonComponent,
        AmwCardComponent,
        AmwIconButtonComponent,
        AmwTooltipDirective,
        AmwIconComponent,
        AmwProgressBarComponent,
    ],
    templateUrl: './recipe-tags.component.html',
    styleUrls: ['./recipe-tags.component.scss'],
    encapsulation: ViewEncapsulation.None,
})
export class RecipeTagsComponent implements OnInit {
    private recipeTagsService = inject(RecipeTagsService);
    private nonNullableFb = inject(NonNullableFormBuilder);
    private notificationService = inject(NotificationService);

    recipeId = input<number>();
    tags = input<RecipeTagModel[]>([]);
    tagsChange = output<RecipeTagModel[]>();

    allTags = signal<RecipeTagModel[]>([]);
    loading = signal(false);
    error = signal('');
    searchTerm = signal('');
    filteredTags = signal<RecipeTagModel[]>([]);

    tagForm: FormGroup;
    isAddingTag = signal(false);

    // Page title and subtitle
    pageTitle = 'Recipe Tags';
    pageSubtitle = 'Manage tags for better recipe organization';

    constructor() {
        this.tagForm = this.nonNullableFb.group({
            name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
            color: ['#1976d2', [Validators.required]],
            description: ['', [Validators.maxLength(200)]]
        });
    }

    ngOnInit(): void {
        this.loadAllTags();
    }

    loadAllTags(): void {
        this.loading.set(true);
        this.error.set('');

        this.recipeTagsService.getAllTags().subscribe({
            next: (tags) => {
                this.allTags.set(tags);
                this.filteredTags.set(tags);
                this.loading.set(false);
            },
            error: (error) => {
                this.error.set('Failed to load tags');
                this.loading.set(false);
                console.error('Error loading tags:', error);
            },
        });
    }

    onRefresh(): void {
        this.loadAllTags();
    }

    onRetry(): void {
        this.loadAllTags();
    }

    addTag(tag: RecipeTagModel): void {
        if (!this.tags().find(t => t.id === tag.id)) {
            const updatedTags = [...this.tags(), tag];
            this.tagsChange.emit(updatedTags);
        }
    }

    removeTag(tag: RecipeTagModel): void {
        const updatedTags = this.tags().filter(t => t.id !== tag.id);
        this.tagsChange.emit(updatedTags);
    }

    createNewTag(): void {
        if (this.tagForm.valid) {
            const newTag: Partial<RecipeTagModel> = {
                name: this.tagForm.get('name')?.value,
                description: this.tagForm.get('description')?.value,
                color: this.tagForm.get('color')?.value
            };

            this.recipeTagsService.createTag(newTag).subscribe({
                next: (createdTag) => {
                    this.allTags.set([...this.allTags(), createdTag]);
                    this.addTag(createdTag);
                    this.tagForm.reset({
                        name: '',
                        description: '',
                        color: '#1976d2'
                    });
                },
                error: (error) => {
                    console.error('Error creating tag:', error);
                },
            });
        }
    }

    deleteTag(tag: RecipeTagModel): void {
        this.recipeTagsService.deleteTag(tag.id!).subscribe({
            next: () => {
                this.allTags.set(this.allTags().filter(t => t.id !== tag.id));
                this.removeTag(tag);
            },
            error: (error) => {
                console.error('Error deleting tag:', error);
            },
        });
    }

    filterTags(): void {
        if (!this.searchTerm().trim()) {
            this.filteredTags.set(this.allTags());
        } else {
            this.filteredTags.set(this.allTags().filter(tag =>
                tag.name.toLowerCase().includes(this.searchTerm().toLowerCase()) ||
                tag.description?.toLowerCase().includes(this.searchTerm().toLowerCase())
            ));
        }
    }

    getTagStyle(tag: RecipeTagModel): Record<string, string> {
        return {
            'background-color': tag.color || '#1976d2',
            'color': this.getContrastColor(tag.color || '#1976d2')
        };
    }

    private getContrastColor(hexColor: string): string {
        // Convert hex to RGB
        const r = parseInt(hexColor.slice(1, 3), 16);
        const g = parseInt(hexColor.slice(3, 5), 16);
        const b = parseInt(hexColor.slice(5, 7), 16);

        // Calculate luminance
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;

        return luminance > 0.5 ? '#000000' : '#ffffff';
    }

    isTagSelected(tag: RecipeTagModel): boolean {
        return this.tags().some(t => t.id === tag.id);
    }
} 