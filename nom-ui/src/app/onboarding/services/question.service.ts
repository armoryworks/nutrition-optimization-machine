import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuestionAnswerSubmission } from '../models/question-answer-submission.model';

@Injectable({
  providedIn: 'root',
})
export class QuestionService {
  private baseUrl = '/api/questions';

  constructor(private http: HttpClient) {}

  getOnboardingQuestions(): Observable<any> {
    return this.http.get(`${this.baseUrl}/onboarding`);
  }

  submitOnboardingAnswers(
    personId: number,
    submission: QuestionAnswerSubmission
  ): Observable<any> {
    return this.http.post(
      `${this.baseUrl}/onboarding?personId=${personId}`,
      submission
    );
  }
}
