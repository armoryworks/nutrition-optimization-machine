import { BaseCommonModel } from '../../common/models/_base-common.model';

/**
 * Represents a question entity, inheriting common properties.
 * This model is used for fetching question details from the backend.
 */
export class QuestionModel implements BaseCommonModel {
  id: number;
  text: string;
  hint: string | null;
  questionCategoryId: number;
  answerType: 'YesNo' | 'TextInput' | 'MultiSelect' | 'SingleSelect';
  displayOrder: number;
  isActive: boolean;
  defaultAnswer: string | null; // Default value for the answer. For select types, this can be a pre-selected value.
  validationRegex: string | null;
  options: string; // Available choices for 'MultiSelect' or 'SingleSelect' types.

  constructor(data: any = {}) {
    this.id = data.id;
    this.text = data.text;
    this.hint = data.hint;
    this.questionCategoryId = data.questionCategoryId;
    this.answerType = data.answerType;
    this.displayOrder = data.displayOrder;
    this.isActive = data.isActive;
    this.defaultAnswer = data.defaultAnswer;
    this.validationRegex = data.validationRegex;
    this.options = data.options; // This property is set during mapping in the service
  }
}
