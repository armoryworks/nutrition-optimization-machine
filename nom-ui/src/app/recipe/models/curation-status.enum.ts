export enum CurationStatus {
    NonCurated = 9000,
    PendingCuration = 9001,
    RequiresRevision = 9002,
    Curated = 9003,
    Rejected = 9004
}

// Helper function to check if a status allows submission for curation
export function canSubmitForCuration(status: CurationStatus | string): boolean {
    if (typeof status === 'string') {
        // Handle string status values that might come from the backend
        return status === 'NonCurated' ||
            status === 'NON-CURATED' ||
            status === 'Non-Curated' ||
            status === 'Draft';
    }
    // Handle numeric enum values
    return status === CurationStatus.NonCurated;
}

// Helper function to check if a status is pending curation
export function isPendingCuration(status: CurationStatus | string): boolean {
    if (typeof status === 'string') {
        return status === 'PendingCuration' ||
            status === 'Pending Curation';
    }
    return status === CurationStatus.PendingCuration;
} 