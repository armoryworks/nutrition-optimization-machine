export interface RecipeImageCredit {
  sourceName?: string | null;
  sourceUrl?: string | null;
  author?: string | null;
  authorUrl?: string | null;
  license?: string | null;
  licenseUrl?: string | null;
  /** No-derivatives licence: show the photo uncropped. */
  noDerivatives: boolean;
}

export type RecipeImageCandidateStatus = 'pending' | 'attached' | 'approved' | 'rejected' | 'superseded' | 'removed';

export interface RecipeImageCandidate {
  id: number;
  recipeId: number;
  recipeName: string;
  status: RecipeImageCandidateStatus;
  sourceKey: string;
  sourceName: string;
  title?: string | null;
  author?: string | null;
  authorUrl?: string | null;
  license: string;
  licenseUrl?: string | null;
  landingUrl?: string | null;
  hotlink: boolean;
  /** Remote URL for hotlinked sources; null means fetch the rehosted preview from the API. */
  previewUrl?: string | null;
  score?: number | null;
  checksJson?: string | null;
  createdDate: string;
  reviewedAt?: string | null;
}

/** One verification check as the finder recorded it. */
export interface RecipeImageCheckSummary {
  name: string;
  passed: boolean;
  reasons: string[];
}
