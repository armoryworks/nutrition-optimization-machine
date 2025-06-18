import { AnswerModel } from './answer.model'; // Import AnswerModel

/**
 * Represents the payload structure for submitting a collection of answers to the API.
 * This model is specifically for outgoing data as part of a request.
 */
export class SubmitAnswersRequestModel {
  personId: number;
  answers: AnswerModel[];

  constructor(data: any = {}) {
    this.personId = data.personId;
    // For nested complex objects (like 'answers' which is an array of AnswerModel),
    // we still need to manually map to ensure they are instances of AnswerModel.
    this.answers = data.answers
      ? data.answers.map((ans: any) => new AnswerModel(ans))
      : [];
  }
}
