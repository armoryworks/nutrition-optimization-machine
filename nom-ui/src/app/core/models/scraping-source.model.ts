export type ScrapingSourceStatus = 'Pending' | 'Approved' | 'Rejected';

export interface SourceReason {
  code: string;
  outcome: 'pass' | 'fail' | 'unknown' | 'info' | string;
  detail?: string | null;
}

export interface SourceStatement {
  kind: string;
  text?: string | null;
  foundAt?: string | null;
  where?: string | null;
  licenceUrl?: string | null;
}

export interface SourceFacts {
  licenceKind?: string | null;
  licenceBasis?: string | null;
  licenceStatements: SourceStatement[];
  sampleLicences: Record<string, number>;
  termsPages: { url: string; outcome?: string | null }[];
  prohibitions: { category?: string | null; text?: string | null; foundAt?: string | null }[];
  robotsUrl?: string | null;
  robotsStatus?: string | null;
  generalAllowsHomepage?: boolean | null;
  generalSamplePathsDisallowed: number;
  nomBotAllowsHomepage?: boolean | null;
  nomBotSamplePathsDisallowed: number;
  aiCrawlersBlocked: string[];
  aiCrawlersAllowed: string[];
  crawlDelaySeconds?: number | null;
  recipeUrlCount: number;
  sampleRequested: number;
  sampleFetched: number;
  sampleWithRecipe: number;
  sampleComplete: number;
  sampleEnglish: number;
  ingredientLines: number;
  linesWithQuantity: number;
  homepageOutcome?: string | null;
  homepageLinks?: number | null;
  noAddress?: boolean;
  refusedPages?: string[];
  samplesRefused?: number;
  termsOnly?: boolean;
}

export interface SourceCheck {
  id: number;
  domain: string;
  purpose: string;
  mode?: 'terms-only' | 'full' | string;
  newerRuleVersion?: boolean;
  status: string;
  error?: string | null;
  startedUtc: string;
  finishedUtc?: string | null;
  verdict?: 'clear' | 'not-clear' | null;
  conclusive: boolean;
  ruleVersion?: string | null;
  policyVersion?: string | null;
  reasons: SourceReason[];
  facts?: SourceFacts | null;
}

export interface ScrapingSourceModel {
  id: number;
  domain: string;
  status: ScrapingSourceStatus;
  kind?: 'web' | 'hand' | string;
  title?: string | null;
  sampleUrl?: string;
  requestedByName?: string;
  createdDate: string;
  reviewedByName?: string;
  reviewedDate?: string;
  notes?: string;
  licenceKind?: string | null;
  licenceStatement?: string | null;
  licenceFoundAt?: string | null;
  checkVerdict?: 'clear' | 'not-clear' | null;
  checkedAtUtc?: string | null;
  check?: SourceCheck | null;
  lastDiscoveredUtc?: string | null;
  probationUntilUtc?: string | null;
  probationRecipeLimit?: number | null;
  probationResult?: string | null;
  harvestPausedAt?: string | null;
  harvestPausedReason?: string | null;
  recipesWithheldAt?: string | null;
  recipesWithheldReason?: string | null;
  rightsClass?: RightsClass | null;
  effectiveRightsClass?: RightsClass | null;
  rightsRuleVersion?: string | null;
  rightsQuote?: string | null;
  rightsQuoteUrl?: string | null;
  rightsDerivedAtUtc?: string | null;
  rightsRecordId?: number | null;
  rightsRecord?: SourceRightsRecord | null;
  aiReserved?: boolean;
  aiReservedQuote?: string | null;
  aiReservedQuoteUrl?: string | null;
  permissionGrantedBy?: string | null;
  permissionGrantedOn?: string | null;
  permissionText?: string | null;
  permissionRecordedAtUtc?: string | null;
  factsOnlyApprovedAt?: string | null;
  mayFetch?: boolean;
  fetchRefusal?: string | null;
  recheckSkipped?: string | null;
  termsCheck?: SourceCheck | null;
  prose?: SourceProseFigures | null;
}

export interface SourceProseFigures {
  sourceId: number;
  domain: string;
  rightsClass?: RightsClass | null;
  classCheckId?: number | null;
  classFromTermsOnly: boolean;
  recipes: number;
  approved: number;
  withheld: number;
  withheldApproved: number;
  wouldWithhold: number;
  wouldWithholdApproved: number;
}

export type RightsClass = 'clear' | 'facts-only' | 'permission-needed' | 'forbidden';

export interface SourceRightsChange {
  sourceId: number;
  domain: string;
  checkId?: number | null;
  oldClass?: RightsClass | null;
  newClass?: RightsClass | null;
  oldAiReserved: boolean;
  newAiReserved: boolean;
  reason?: string | null;
  quote?: string | null;
  quoteUrl?: string | null;
  aiQuote?: string | null;
  aiQuoteUrl?: string | null;
  conclusive: boolean;
  changed: boolean;
  recipesRestamped: number;
  recipesWithheld: number;
  recipesReleased: number;
  termsOnly?: boolean;
  wouldWithhold?: number;
  wouldWithholdApproved?: number;
}

export interface SourceRightsRun {
  applied: boolean;
  ruleVersion: string;
  stamp: string;
  sources: number;
  changed: number;
  inconclusive: number;
  results: SourceRightsChange[];
}

export interface SourcePermissionRequest {
  grantedBy: string;
  grantedOn: string;
  text: string;
}

export type RightsStep = 'titles' | 'credits' | 'retention';

export interface SourceRecipeChange {
  recipeId: number;
  domain?: string | null;
  old?: string | null;
  new?: string | null;
  reason?: string | null;
}

export interface SourceRecipeStep {
  step: string;
  applied: boolean;
  stamp: string;
  ruleVersion?: string | null;
  examined: number;
  changed: number;
  skipped: number;
  alreadyUndone?: number;
  refused?: string | null;
  skippedByReason: Record<string, number>;
  changes: SourceRecipeChange[];
}

export interface SourceTaken {
  sourceId: number;
  domain: string;
  rightsClass?: RightsClass | null;
  rightsQuote?: string | null;
  rightsQuoteUrl?: string | null;
  recipes: number;
  recipesShown: number;
  recipesWithheld: boolean;
  removable?: boolean;
  retired?: number;
  purged?: number;
}

export interface RecipeRemovalSource {
  sourceId: number;
  domain: string;
  rightsClass?: string | null;
  rightsQuote?: string | null;
  recipes: number;
  alreadyDeleted: number;
  approved: number;
  approvedByPerson: number;
  approvedUnrecorded: number;
  editedByPerson: number;
  inMealPlans: number;
  favourited: number;
  inCookbooks: number;
  inShoppingLists: number;
}

export interface RecipeRemovalTarget {
  table: string;
  columns: string;
  action: string;
  rows: number;
}

export interface RecipeRemovalReport {
  stage: string;
  applied: boolean;
  stamp: string;
  sourceId?: number | null;
  runId?: number | null;
  resumed: boolean;
  refused?: string | null;
  recipes: number;
  skipped: number;
  alreadyDone: number;
  skippedByReason: Record<string, number>;
  sources: RecipeRemovalSource[];
  targets: RecipeRemovalTarget[];
  warning?: string | null;
  recipeIds: number[];
}

export interface RecipeRemovalRun {
  id: number;
  kind: string;
  stamp: string;
  sourceId?: number | null;
  undoesStamp?: string | null;
  personId: number;
  reason?: string | null;
  planned: number;
  done: number;
  skipped: number;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  undoable: number;
}

export interface RecipeRemovalFigures {
  sourceId: number;
  domain: string;
  rightsClass?: string | null;
  removable: boolean;
  taken: number;
  retired: number;
  purged: number;
}

export interface RecipeRemovalStatus {
  retired: number;
  purged: number;
  bySource: RecipeRemovalFigures[];
  runs: RecipeRemovalRun[];
  unfinished?: RecipeRemovalRun | null;
}

export interface SourceRightsStatus {
  ruleVersion: string;
  titleRuleVersion: string;
  enforced: boolean;
  sourcesByClass: Record<string, number>;
  sourcesUnclassed: number;
  sourcesStale: number;
  aiReserved: number;
  permissions: number;
  factsOnlyApproved: number;
  fetchable: number;
  recipesByClass: Record<string, number>;
  recipesAiReserved: number;
  factsOnlyWithheld: number;
  factsOnlyOwnMethod: number;
  retentionDue: number;
  retentionHeld: number;
  recipesWithCredit: number;
  takenFromBarredSources: number;
  recipesRetired?: number;
  recipesPurged?: number;
  removalBySource?: RecipeRemovalFigures[];
  lastChangeUtc?: string | null;
  changesLast7d: number;
  proseBySource?: SourceProseFigures[];
}

export interface SourceCandidate {
  id: number;
  domain: string;
  evidenceUrl?: string | null;
  foundVia?: string | null;
  signal?: string | null;
  firstSeenUtc: string;
  lastSeenUtc: string;
  timesSeen: number;
  state: string;
  check?: SourceCheck | null;
  canApprove: boolean;
  approveBlockedReason?: string | null;
}

export interface SourceDiscoveryRun {
  id: number;
  startedUtc: string;
  finishedUtc?: string | null;
  status: 'running' | 'completed' | 'partial' | 'failed' | 'skipped' | string;
  stopReason?: string | null;
  error?: string | null;
  seeds: string;
  candidates: number;
  leads: number;
  seedsVisited?: string | null;
  seedsNotVisited?: string | null;
  seedsRefused?: string | null;
  pagesFetched: number;
  excludeSent?: number;
  excludeLeftOut?: number;
  seedsExcluded?: string | null;
  candidatesExcluded?: number;
  candidatesExcludedHosts?: SourceExcludedHost[];
  excludeNote?: string | null;
  refusingHosts?: number;
  refusingHostsList?: SourceRefusingHostReport[];
}

export interface SourceRefusingHostReport {
  domain: string;
  url?: string | null;
  httpStatus?: number | null;
  botChallenge: boolean;
}

export interface SourceRefusingHost {
  id: number;
  host: string;
  firstRefusedUtc: string;
  lastRefusedUtc: string;
  timesRefused: number;
  lastUrl?: string | null;
  lastHttpStatus?: number | null;
  lastBotChallenge: boolean;
  lastRunId?: number | null;
  held: boolean;
  releasedAtUtc?: string | null;
  releasedByPersonId?: number | null;
  releasedNote?: string | null;
}

export interface SourceRightsRecord {
  id: number;
  host: string;
  rightsClass: RightsClass;
  quote: string;
  quoteUrl: string;
  readOn: string;
  recordedByPersonId: number;
  recordedAtUtc: string;
  withdrawnAtUtc?: string | null;
  journalEntryId?: number | null;
  sources: string[];
  candidate?: string | null;
}

export interface SourceRightsByHandRequest {
  host: string;
  rightsClass: RightsClass;
  quote: string;
  quoteUrl: string;
  readOn: string;
}

export interface SourceRightsByHandSource {
  sourceId: number;
  domain: string;
  oldClass?: RightsClass | null;
  newClass?: RightsClass | null;
  recipes: number;
}

export interface SourceRightsByHandResult {
  applied: boolean;
  host?: string | null;
  rightsClass?: RightsClass | null;
  refused?: string | null;
  alreadyRecorded: boolean;
  sources: SourceRightsByHandSource[];
  candidate?: string | null;
  candidateClass?: RightsClass | null;
  excluded: boolean;
  record?: SourceRightsRecord | null;
}

export interface SourceExcludedHost {
  domain: string;
  discoveredVia?: string | null;
  excludedBy?: string | null;
}

export interface SourceJournalEntry {
  id: number;
  sourceId: number;
  recordId?: number | null;
  domain?: string | null;
  changeType: string;
  field?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  timestamp: string;
  changedByPersonId: number;
  canUndo: boolean;
}

export interface SourceReview {
  enabled: boolean;
  policyVersion: string;
  scraperRuleVersion: string;
  queue: SourceCandidate[];
  notClear: SourceCandidate[];
  waiting: SourceCandidate[];
  leads: number;
  approved: ScrapingSourceModel[];
  hand: ScrapingSourceModel[];
  rejected: ScrapingSourceModel[];
  runs: SourceDiscoveryRun[];
  failedChecks: SourceCheck[];
  journal: SourceJournalEntry[];
  rightsRecords?: SourceRightsRecord[];
  refusingHosts?: SourceRefusingHost[];
}

export interface SourceWithholdReport {
  applied: boolean;
  recipes: number;
  source?: ScrapingSourceModel | null;
}

export interface HandSourceRequest {
  domain: string;
  title: string;
  licenceKind: string;
  licenceStatement: string;
  licenceFoundAt: string;
  notes?: string;
}

export interface SourceAdmissionStatus {
  enabled: boolean;
  policyVersion: string;
  scraperRuleVersion: string;
  runs: SourceDiscoveryRun[];
  runsFailedInRow: number;
  lastGoodRunUtc?: string | null;
  checksLast24h: number;
  maxChecksPerDay: number;
  checksFailedLast7d: number;
  queue: number;
  notClear: number;
  waiting: number;
  leads: number;
  approved: number;
  approvedClear: number;
  approvedNotClear: number;
  approvedUnjudged: number;
  harvestPaused: number;
  withheld: number;
  inProbation: number;
  harvestable: number;
  hand: number;
  recheckSkipped?: number;
  termsChecksWaiting?: number;
  lastExclusionRun?: SourceDiscoveryRun | null;
  leadsHeld?: number;
  leadsTermsChecked?: number;
}
