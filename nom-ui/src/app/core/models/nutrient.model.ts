/** A nutrient definition (API: GET /Nutrient/all). */
export interface NutrientModel {
  id: number;
  name: string;
  description?: string | null;
  defaultMeasurementId: number;
  defaultMeasurementName: string;
  defaultMeasurementSymbol: string;
  rank?: number | null;
}
