import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuestionAnswerSubmission } from '../models/question-answer-submission.model';

@Injectable({
  providedIn: 'root',
})
export class QuestionService {
  private http = inject(HttpClient);

  private baseUrl = '/api/questions';

  getOnboardingQuestions(): Observable<QuestionAnswerSubmission[]> {
    return this.http.get(`${this.baseUrl}/onboarding`);
  }

  submitOnboardingAnswers(
    personId: number,
    submission: QuestionAnswerSubmission
  ): Observable<{ success: boolean; message?: string }> {
    return this.http.post<{ success: boolean; message?: string }>(
      `${this.baseUrl}/onboarding?personId=${personId}`,
      submission
    );
  }
}
