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
  answerType: 'Yes/No' | 'TextInput' | 'MultiSelect' | 'SingleSelect';
  displayOrder: number;
  isActive: boolean;
  isRequiredForPlanCreation: boolean;
  defaultAnswer: string | null; // This will be a JSON string for options in Multi/Single-Select
  validationRegex: string | null;
  options?: string[]; // Derived from defaultAnswer for select types in the frontend

  constructor(data: any = {}) {
    this.id = data.id;
    this.text = data.text;
    this.hint = data.hint;
    this.questionCategoryId = data.questionCategoryId;
    this.answerType = data.answerType;
    this.displayOrder = data.displayOrder;
    this.isActive = data.isActive;
    this.isRequiredForPlanCreation = data.isRequiredForPlanCreation;
    this.defaultAnswer = data.defaultAnswer;
    this.validationRegex = data.validationRegex;
    this.options = data.options; // This property is set during mapping in the service
  }
}
