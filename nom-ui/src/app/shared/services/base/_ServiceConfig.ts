// File: nom-ui/src/app/shared/services/base/_ServiceConfig.ts

/**
 * Service configuration interface
 */
export interface ServiceConfig {
    /**
     * The service name
     */
    name?: string;

    /**
     * Whether to enable caching
     */
    enableCaching?: boolean;

    /**
     * Cache duration in milliseconds
     */
    cacheDuration?: number;

    /**
     * Whether to enable logging
     */
    enableLogging?: boolean;

    /**
     * Whether to enable error handling
     */
    enableErrorHandling?: boolean;

    /**
     * Whether to enable retry logic
     */
    enableRetry?: boolean;

    /**
     * Maximum number of retry attempts
     */
    maxRetryAttempts?: number;

    /**
     * Retry delay in milliseconds
     */
    retryDelay?: number;

    /**
     * Whether to enable health checks
     */
    enableHealthChecks?: boolean;

    /**
     * Health check interval in milliseconds
     */
    healthCheckInterval?: number;
} 