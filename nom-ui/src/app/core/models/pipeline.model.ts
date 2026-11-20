export interface PipelineBlockedBy {
  none: number;
  one: number;
  two: number;
  threeOrMore: number;
}

export interface PipelineRecipeCounts {
  approved: number;
  waiting: number;
  needsRevision: number;
  pendingReview: number;
  rejected: number;
  waitingBlockedBy: PipelineBlockedBy;
  waitingPublicDomain: number;
  waitingScrapedWithoutOwnImage: number;
  waitingWithSourceProse: number;
  revisionMissingQuantities: number;
  revisionSteps: number;
  approvedWithUncountedLine: number;
  approvedWithoutImage: number;
  withoutNutritionLabel: number;
  lastAutoApprovedAt: string | null;
  autoApprovedLast24h: number;
  lastScrapedAt: string | null;
  createdLast7d: number;
}

export interface PipelineLineFunnel {
  total: number;
  notAnIngredient: number;
  ingredient: number;
  onCuratedFood: number;
  unresolved: number;
  notMeasured: number;
  noData: number;
  noConversion: number;
  counted: number;
  countedByServingFallback: number;
  countWithoutItemWeight: number;
  volumeWithoutDensity: number;
  countableStrict: number;
}

export interface PipelineLines {
  all: PipelineLineFunnel;
  approved: PipelineLineFunnel;
  folded: number;
  foldedOnApproved: number;
}

export interface PipelineWordingBucket {
  bucket: string;
  wordings: number;
  lines: number;
}

export interface PipelineLinkStateCount {
  state: 'pendingPick' | 'noCandidate' | 'rejectedPick' | 'neverProposed';
  foods: number;
  lines: number;
}

export interface PipelineFoodCounts {
  curated: number;
  pendingReview: number;
  notCurated: number;
  curatedWithoutNutrition: number;
  curatedWithoutUsdaLink: number;
  curatedWithoutFoodGroup: number;
  foodsMissingItemWeight: number;
  foodsMissingDensity: number;
  wordingsInUse: number;
  unresolvedWordings: number;
  wordingsByUse: PipelineWordingBucket[];
  unresolvedByLinkState: PipelineLinkStateCount[];
}

export interface PipelineStuckFood {
  ingredientId: number;
  name: string;
  lines: number;
}

export interface PipelineStuck {
  unresolvedWordings: PipelineStuckFood[];
  countWithoutItemWeight: PipelineStuckFood[];
  volumeWithoutDensity: PipelineStuckFood[];
}

export interface PipelineProposalCounts {
  pending: number;
  pendingModelPicks: number;
  pendingModelPicksVerified: number;
  pendingExactMatches: number;
  pendingOther: number;
  linkQueue: number;
  verifiedAppliedLast24h: number;
  verifiedDailyCap: number;
  verifiedPerFoodCap: number;
  foodsAtCap: number;
  nameMatchAppliedLast24h: number;
  nameMatchDailyCap: number;
  nameMatchPerFoodCap: number;
  nameMatchFoodsAtCap: number;
  lastProposedAt: string | null;
  lastAppliedBySystemAt: string | null;
}

export interface PipelineImageCounts {
  approvedWithoutImage: number;
  neverSearched: number;
  lastSearchedAt: string | null;
  candidates: { pending: number; attached: number; approved: number; rejected: number; superseded: number; removed: number };
}

export interface PipelineLaneSwitch {
  key: string;
  label: string;
  on: boolean;
}

export interface PipelineLaneCap {
  label: string;
  limit: number;
  used: number | null;
}

export interface PipelineLane {
  key: string;
  name: string;
  gateLane: string | null;
  external: boolean;
  enabled: boolean;
  usesModel: boolean;
  modelConfigured: boolean | null;
  model: string | null;
  switches: PipelineLaneSwitch[];
  caps: PipelineLaneCap[];
  queue: number | null;
  queueLabel: string | null;
  lastActivityAt: string | null;
  activityLast24h: number | null;
}

export interface PipelineJournalRow {
  entityType: string;
  changeType: string;
  last24h: number;
  last7d: number;
  total: number;
  lastAt: string | null;
}

export interface PipelineCounts {
  generatedAt: string;
  computeMs: number;
  fromCache: boolean;
  recipes: PipelineRecipeCounts;
  lines: PipelineLines;
  foods: PipelineFoodCounts;
  stuck: PipelineStuck;
  proposals: PipelineProposalCounts;
  images: PipelineImageCounts;
  lanes: PipelineLane[];
  ai: { quietHours: string; quietTimeZone: string; quietMinutesLeft: number | null };
  journal: PipelineJournalRow[];
}

export type AiLaneStateName =
  | 'not-started'
  | 'disabled'
  | 'running'
  | 'idle'
  | 'paused'
  | 'quiet-hours'
  | 'host-down'
  | 'at-cap'
  | 'failing';

export interface AiLaneStatus {
  lane: string;
  state: AiLaneStateName;
  stateSince: string | null;
  stateReason: string | null;
  model: string | null;
  lastPass: string | null;
  lastPassItems: number;
  lastError: string | null;
  lastErrorAt: string | null;
  itemsToday: number;
  itemsTodayFromLedger: boolean;
  dailyCap: number | null;
  modelCalls: number;
  modelCallErrors: number;
  lastCall: string | null;
  lastSuccess: string | null;
  latencyMedianMs: number | null;
  latencyP95Ms: number | null;
  countingSince: string;
}

export interface AiHostStatus {
  host: string;
  up: boolean;
  downSince: string | null;
  consecutiveFailures: number;
  lastError: string | null;
  lastProbe: string | null;
  modelsAsked: string[];
  modelsInstalled: string[];
  modelsMissing: string[];
}

export interface PipelineGateLane {
  lane: string;
  label: string;
  paused: boolean;
  status: AiLaneStatus;
}

export interface PipelineGate {
  pauseAll: boolean;
  lanes: PipelineGateLane[];
  hosts: AiHostStatus[];
}

export interface PipelineStatus {
  status: PipelineCounts;
  gate: PipelineGate;
}

export interface UnfoldPreview {
  applied: boolean;
  rowsExamined: number;
  folds: number;
  linesAdded: number;
  collapsed: number;
  duplicatesDropped: number;
  quantitiesReassigned: number;
  unmeasuredAdded: number;
  unnamedUnmeasured: number;
  skippedSingleSourceLine: number;
  skippedAmbiguous: number;
  skippedOtherKind: number;
  remaining: number;
}

export interface RereadPreview {
  linesExamined: number;
  changeCount: number;
  touchCount: number;
  stopCounting: number;
  startCounting: number;
  drainedAsContainers: number;
  missingMeasurements: string[];
}

export interface LinkUndoPreview {
  applied: boolean;
  proposals: number;
  proposalsSkipped: number;
  linesRestored: number;
  linesSkipped: number;
  foodsRestored: number;
  statusesRestored: number;
  statusesKept: number;
  remaining: number;
}
