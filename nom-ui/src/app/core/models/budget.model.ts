export interface Budget {
  amount: number;
  currency: string;
  period: 'weekly' | 'monthly';
}

export interface EffectiveBudget extends Budget {
  hasBudget: boolean;
  source: 'person' | 'household' | 'none';
}
