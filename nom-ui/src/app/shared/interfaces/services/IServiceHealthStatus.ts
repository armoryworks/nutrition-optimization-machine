/**
 * Service health status interface
 */
export interface IServiceHealthStatus {
    /**
     * Whether the service is healthy
     */
    isHealthy: boolean;

    /**
     * The service name
     */
    serviceName: string;

    /**
     * The timestamp of the health check
     */
    timestamp: Date;

    /**
     * Any error messages
     */
    errors: string[];

    /**
     * Additional health information
     */
    details?: Record<string, unknown>;
} 