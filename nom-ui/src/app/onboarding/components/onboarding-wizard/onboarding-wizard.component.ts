import {
  Component,
  OnInit,
  Input,
  Output,
  EventEmitter,
  ViewEncapsulation,
} from '@angular/core';
import {
  FormGroup,
  FormControl,
  NonNullableFormBuilder,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { finalize } from 'rxjs/operators';

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { JsonParseCommonPipe } from '../../../common/pipes/json-parse.pipe';
import { QuestionModel } from '../../../question/models/question.model';
import { QuestionService } from '../../../question/services/question.service';
import { AnswerModel } from '../../../question/models/answer.model';

@Component({
  selector: 'app-onboarding-wizard',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    JsonParseCommonPipe, // Use the common pipe
  ],
  templateUrl: './onboarding-wizard.component.html',
  styleUrls: ['./onboarding-wizard.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class OnboardingWizardComponent implements OnInit {
  @Input() personId: number | null = null;
  @Output() completion = new EventEmitter<boolean>();

  questions: QuestionModel[] = [];
  currentQuestionIndex: number = 0;
  currentAnswerForm!: FormGroup;

  isLoading: boolean = true;
  isSubmitting: boolean = false;
  error: string | null = null;
  submitMessage: string | null = null;

  private allCollectedAnswers: { [key: number]: string } = {};

  constructor(
    private nonNullableFb: NonNullableFormBuilder,
    private questionService: QuestionService
  ) {}

  ngOnInit(): void {
    this.fetchQuestions();
  }

  fetchQuestions(): void {
    this.isLoading = true;
    this.error = null;
    this.questionService
      .getOnboardingQuestions()
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (data) => {
          this.questions = data;
          if (this.questions.length > 0) {
            this.initializeFormForCurrentQuestion();
          }
        },
        error: (err) => {
          console.error('Error fetching questions:', err);
          this.error = `Failed to fetch questions: ${
            err.message || 'Unknown error'
          }`;
        },
      });
  }

  private initializeFormForCurrentQuestion(): void {
    const question = this.currentQuestion;
    if (!question) return;

    let initialValue: string = '';
    if (this.allCollectedAnswers[question.id] !== undefined) {
      initialValue = this.allCollectedAnswers[question.id];
    } else if (question.defaultAnswer !== null) {
      initialValue = question.defaultAnswer;
    } else {
      if (question.answerType === 'Yes/No') {
        initialValue = 'false';
      } else if (
        question.answerType === 'MultiSelect' ||
        question.answerType === 'SingleSelect'
      ) {
        initialValue = JSON.stringify([]);
      }
    }

    const validators = question.isRequiredForPlanCreation
      ? [Validators.required]
      : [];
    if (question.validationRegex && question.validationRegex.length > 0) {
      validators.push(Validators.pattern(question.validationRegex));
    }

    this.currentAnswerForm = this.nonNullableFb.group({
      answer: new FormControl(initialValue, { validators: validators }),
    });
  }

  get currentQuestion(): QuestionModel | undefined {
    return this.questions[this.currentQuestionIndex];
  }

  onAnswerChange(value: any): void {
    if (this.currentQuestion) {
      let processedValue = value;
      const question = this.currentQuestion;

      if (question.answerType === 'MultiSelect') {
        const currentSelections: string[] = JSON.parse(
          this.allCollectedAnswers[question.id] || '[]'
        );
        if (currentSelections.includes(value)) {
          processedValue = JSON.stringify(
            currentSelections.filter((item) => item !== value)
          );
        } else {
          processedValue = JSON.stringify([...currentSelections, value]);
        }
      } else if (question.answerType === 'Yes/No') {
        processedValue = String(value);
      } else if (question.answerType === 'SingleSelect') {
        processedValue = JSON.stringify([value]);
      }

      this.allCollectedAnswers[question.id] = processedValue;
    }
  }

  goToNextQuestion(): void {
    this.currentAnswerForm.markAllAsTouched();
    this.currentAnswerForm.updateValueAndValidity();

    if (this.currentAnswerForm.invalid) {
      this.error = 'Please provide a valid answer for the current question.';
      return;
    }

    this.error = null;

    if (this.currentQuestion) {
      this.allCollectedAnswers[this.currentQuestion.id] =
        this.currentAnswerForm.get('answer')?.value;
    }

    if (this.currentQuestionIndex < this.questions.length - 1) {
      this.currentQuestionIndex++;
      this.initializeFormForCurrentQuestion();
    } else {
      this.submitAnswers();
    }
  }

  goToPreviousQuestion(): void {
    if (this.currentQuestionIndex > 0) {
      this.currentQuestionIndex--;
      this.error = null;
      this.initializeFormForCurrentQuestion();
    }
  }

  submitAnswers(): void {
    if (this.personId === null) {
      this.error = 'Person ID is not available for submission.';
      return;
    }

    this.isSubmitting = true;
    this.error = null;
    this.submitMessage = null;

    const answersPayload: AnswerModel[] = Object.keys(
      this.allCollectedAnswers
    ).map((id) => ({
      questionId: parseInt(id, 10),
      submittedAnswer: this.allCollectedAnswers[parseInt(id, 10)],
    }));

    this.questionService
      .submitOnboardingAnswers({
        personId: this.personId!,
        answers: answersPayload,
      })
      .pipe(finalize(() => (this.isSubmitting = false)))
      .subscribe({
        next: (response) => {
          this.submitMessage =
            response.message || 'Answers submitted successfully!';
          this.completion.emit(true);
        },
        error: (err) => {
          console.error('Error submitting answers:', err);
          this.error = `Submission failed: ${err.message || 'Unknown error'}`;
          this.submitMessage = `Submission failed: ${
            err.message || 'Unknown error'
          }`;
          this.completion.emit(false);
        },
      });
  }

  goToDashboard(): void {
    this.completion.emit(true);
  }
}
