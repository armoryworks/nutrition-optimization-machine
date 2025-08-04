// File: nom-ui/src/app/shared/services/events/_EventBusStatistics.ts

/**
 * Event bus statistics interface
 */
export interface EventBusStatistics {
    /**
     * Total number of events published
     */
    totalEvents: number;

    /**
     * Total number of subscribers
     */
    totalSubscribers: number;

    /**
     * Event type counts
     */
    eventTypeCounts: Map<string, number>;

    /**
     * Subscriber counts by event type
     */
    subscriberCounts: Map<string, number>;

    /**
     * Number of active event types
     */
    activeEventTypes: number;

    /**
     * Timestamp of the last statistics update
     */
    lastUpdated: Date;
} 