/** Represents a single nutrient line item on the label. */
export interface LabelNutrient {
    name: string;
    amount: number;
    unit: string;
    dailyValue?: number; // Optional: for nutrients without a %DV
    isBold?: boolean;    // For styling 'Total Fat', 'Protein', etc.
    isIndented?: boolean; // For sub-nutrients like 'Saturated Fat'
  }