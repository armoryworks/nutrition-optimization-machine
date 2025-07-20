// File: nom-ui/src/app/person/models/person-create-response.model.ts

/**
 * Defines the structure of the response received from the backend
 * after a person record has been successfully created or updated.
 */
export interface PersonCreateResponseModel {
  id: number;
  name: string;
  userId: string | null;
}
