/**
 * Interface for base common properties shared across many domain models.
 * Filename is prefixed with '_' to denote it as an interface meant for implementation/extension.
 */
export interface BaseCommonModel {
  id: number;
  // You can add other common properties here if applicable, e.g.,
  // createdDate?: string; // Using string for Date representation from API
  // createdByPersonId?: number;
}
