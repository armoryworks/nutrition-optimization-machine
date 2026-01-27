export interface IFieldError {
  fieldName: string;
  fieldLabel: string;
  errors: { key: string; message: string }[];
}
