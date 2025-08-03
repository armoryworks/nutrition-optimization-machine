// File: nom-ui/src/app/shared/interfaces/services/IBaseService.ts

import { Observable } from 'rxjs';
import { IServiceHealthStatus } from './IServiceHealthStatus';

/**
 * Base interface for all services providing common patterns and functionality
 */
export interface IBaseService {
    /**
     * Gets the service name for logging and debugging
     */
    readonly serviceName: string;

    /**
     * Gets whether the service is initialized
     */
    readonly isInitialized: boolean;

    /**
     * Initializes the service
     */
    initialize(): Promise<void>;

    /**
     * Disposes the service and cleans up resources
     */
    dispose(): Promise<void>;

    /**
     * Gets the service health status
     */
    getHealthStatus(): Observable<IServiceHealthStatus>;

    /**
     * Handles errors in a standardized way
     */
    handleError(error: any, context?: string): void;

    /**
     * Logs information in a standardized way
     */
    logInfo(message: string, data?: any): void;

    /**
     * Logs warnings in a standardized way
     */
    logWarning(message: string, data?: any): void;

    /**
     * Logs errors in a standardized way
     */
    logError(message: string, error?: any): void;
} 