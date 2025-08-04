// File: nom-ui/src/app/shared/services/events/_EventBusOptions.ts

/**
 * Event bus options
 */
export interface EventBusOptions {
    /**
     * Whether to enable logging
     */
    enableLogging?: boolean;

    /**
     * Whether to enable statistics
     */
    enableStatistics?: boolean;

    /**
     * Maximum number of subscribers per event type
     */
    maxSubscribersPerEvent?: number;

    /**
     * Whether to enable error handling
     */
    enableErrorHandling?: boolean;

    /**
     * Whether to enable performance monitoring
     */
    enablePerformanceMonitoring?: boolean;
} 