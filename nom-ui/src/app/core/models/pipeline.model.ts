import { SourceAdmissionStatus, SourceRightsStatus } from './scraping-source.model';

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
  proseWithheld: number;
  proseWithheldApproved: number;
  proseRepair: number;
  proseRepairApproved: number;
  proseUnsure: number;
  proseUnsureApproved: number;
  proseGaveUp: number;
  proseGaveUpByClass: Record<string, number>;
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
  heldByRule?: number;
  heldByReason?: PipelineReasonCount[];
  awaitingSecondSource?: number;
  pendingWithoutReason?: number;
  rejected?: number;
  branded?: PipelineBrandedCounts;
  preparedContents?: PipelinePreparedContents;
}

export interface PipelinePreparedContents {
  meals: number;
  read: number;
  readUnresolved: number;
  unread: number;
  none: number;
  fromFdc: number;
  fromLabels: number;
}

export interface PipelineReasonCount {
  reason: string;
  foods: number;
}

export interface PipelineSourceCount {
  sources: string;
  foods: number;
}

export interface PipelineBrandedCounts {
  curatedByAgreeingSources: PipelineSourceCount[];
  curatedWithoutCount: number;
  curatedWithDisagreement: number;
  awaiting: number;
  heldByDisagreement: number;
  heldOther: number;
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

export type LaneMode = 'shadow' | 'live' | 'paused';

export interface LaneFigures {
  evaluatedAt: string;
  signature: string | null;
  decisions: number;
  written: number;
  audited: number;
  errors: number;
  flagged: number;
  observedError: number;
  correctedError: number;
  precisionLower: number;
  precisionUpper: number;
  qualifyBar: number;
  neededAudited: number | null;
  windowAudited: number;
  windowErrors: number;
  windowError: number;
  windowErrorLower: number;
  windowErrorUpper: number;
  pauseBar: number;
  window: number;
  auditorSource: string | null;
  auditorSensitivity: number | null;
  auditorFalseFlagRate: number | null;
  auditorKnownBad: number | null;
  auditorKnownGood: number | null;
  auditorExact: boolean;
  auditorProblem: string | null;
  goldenProblem: string | null;
  pauseReasons: string[];
}

export interface LaneFrameLane {
  lane: string;
  tier: string;
  mode: LaneMode;
  reason: string | null;
  since: string;
  signatureHash: string | null;
  evaluatedAt: string | null;
  lastCleanAt: string | null;
  figures: LaneFigures | null;
  decisionsLast24h: number;
  writtenLast24h: number;
  canUndo: boolean;
}

export interface PipelineRoster {
  readerA: string;
  readerAFamily: string;
  readerB: string;
  readerBFamily: string;
  auditor: string;
  auditorFamily: string;
}

export interface PipelineFrame {
  lanes: LaneFrameLane[];
  roster: PipelineRoster;
}

export interface ReprocessReasonCount {
  code: string;
  label: string;
  recipes: number;
  waiting: number;
}

export interface ReprocessLaneBatch {
  tier: string;
  mode: string;
  stamp: string;
  taken: number;
  rebuilt: number;
  held: number;
  unchanged: number;
  failed: number;
  linesChecked: number;
  linesFailing: number;
  faults: string[];
}

export interface ReprocessLaneStatus {
  enabled: boolean;
  ruleVersion: string;
  read: number;
  readWithCurrentRules: number;
  waiting: number;
  waitingByTier: Record<string, number>;
  byReason: ReprocessReasonCount[];
  byOutcome: Record<string, number>;
  heldByReason: Record<string, number>;
  lastDetectedAt: string | null;
  judgeModel: string;
  judgeProblem: string | null;
  lastBatches: ReprocessLaneBatch[];
}

export interface LinkRecheckRun {
  set: string;
  stamp: string;
  lane: boolean;
  mode: string | null;
  signature: string;
  undone: boolean;
  complete: boolean;
  running: boolean;
  groups: number;
  kept: number;
  held: number;
  linesMoved: number;
  linesUnlinked: number;
  aliasesChanged: number;
  withdrawn: number;
  renamed: number;
}

export interface LinkRecheckStatus {
  enabled: boolean;
  version: string;
  signature: string;
  sets: string[];
  provenClasses?: string[];
  candidateClasses?: string[];
  provenProblem?: string | null;
  lastRuns: LinkRecheckRun[];
  lastWithdraw: LinkRecheckRun | null;
  waitingPicks: number;
}

export interface ContainerSizeKey {
  rowId: number;
  ingredientId: number;
  ingredient: string;
  kind: string;
  era: string;
  sizeWord: string;
  netGrams: number;
  drainedGrams: number | null;
  sizeText: string;
  sourceRef: string;
  class: string;
  status: string;
  reason: string | null;
  lines: number;
  agree: number;
}

export interface ContainerSizeStatus {
  lane: string;
  laneMode: string;
  rows: number;
  byStatus: Record<string, number>;
  bySource: Record<string, number>;
  lastScoredAt: string | null;
  holdoutLines: number;
  holdoutAgree: number;
  keys: ContainerSizeKey[];
}

export interface LaneUndoReport {
  lane: string;
  tier: string;
  sinceUtc: string;
  applied: boolean;
  result: { undone: number; skipped: number; remaining: number; note: string | null };
}

export interface PipelineStatus {
  status: PipelineCounts;
  gate: PipelineGate;
  frame?: PipelineFrame | null;
  reprocess?: ReprocessLaneStatus | null;
  linkRecheck?: LinkRecheckStatus | null;
  containerSizes?: ContainerSizeStatus | null;
  sources?: SourceAdmissionStatus | null;
  sourceRights?: SourceRightsStatus | null;
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
