// File: nom-ui/src/app/shared/services/base/_BaseService.ts

import { Injectable, OnDestroy, inject } from '@angular/core';
import { BehaviorSubject, Observable, Subject, timer } from 'rxjs';
import { takeUntil, tap, catchError } from 'rxjs/operators';
import { _ServiceHealthStatus } from './_ServiceHealthStatus';
import { _ServiceConfig } from './_ServiceConfig';

/**
 * Abstract base service that should be extended by concrete service implementations
 */
@Injectable()
export abstract class BaseService implements OnDestroy {
    protected readonly config = inject(_ServiceConfig) ?? {};

    protected readonly destroy$ = new Subject<void>();
    protected readonly healthStatus$ = new BehaviorSubject<_ServiceHealthStatus>({
        isHealthy: true,
        serviceName: this.getServiceName(),
        timestamp: new Date(),
        errors: []
    });

    public readonly isInitialized = new BehaviorSubject<boolean>(false);

    constructor() {
        this.initialize();
    }

    /**
     * Gets the service name for logging and debugging
     */
    abstract getServiceName(): string;

    /**
     * Gets whether the service is initialized
     */
    get isInitialized(): boolean {
        return this.isInitialized.value;
    }

    /**
     * Initializes the service
     */
    async initialize(): Promise<void> {
        try {
            this.logInfo('Initializing service');

            // Perform any initialization logic
            await this.performInitialization();

            this.isInitialized.next(true);
            this.updateHealthStatus(true);

            this.logInfo('Service initialized successfully');
        } catch (error) {
            this.logError('Failed to initialize service', error);
            this.updateHealthStatus(false, [error?.message || 'Initialization failed']);
            throw error;
        }
    }

    /**
     * Disposes the service and cleans up resources
     */
    async dispose(): Promise<void> {
        try {
            this.logInfo('Disposing service');

            // Perform cleanup logic
            await this.performCleanup();

            this.isInitialized.next(false);
            this.updateHealthStatus(false);

            this.logInfo('Service disposed successfully');
        } catch (error) {
            this.logError('Error disposing service', error);
            throw error;
        } finally {
            this.destroy$.next();
            this.destroy$.complete();
        }
    }

    /**
     * Gets the service health status
     */
    getHealthStatus(): Observable<_ServiceHealthStatus> {
        return this.healthStatus$.asObservable();
    }

    /**
     * Handles errors in a standardized way
     */
    handleError(error: Error | string | unknown, context?: string): void {
        const errorMessage = context ? `${context}: ${error?.message || error}` : error?.message || error;
        this.logError(errorMessage, error);
        this.updateHealthStatus(false, [errorMessage]);
    }

    /**
     * Logs information in a standardized way
     */
    logInfo(message: string, data?: unknown): void {
        if (this.config.enableLogging !== false) {
            console.log(`[${this.getServiceName()}] INFO: ${message}`, data || '');
        }
    }

    /**
     * Logs warnings in a standardized way
     */
    logWarning(message: string, data?: unknown): void {
        if (this.config.enableLogging !== false) {
            console.warn(`[${this.getServiceName()}] WARNING: ${message}`, data || '');
        }
    }

    /**
     * Logs errors in a standardized way
     */
    logError(message: string, error?: Error | string | unknown): void {
        if (this.config.enableLogging !== false) {
            console.error(`[${this.getServiceName()}] ERROR: ${message}`, error || '');
        }
    }

    /**
     * Performs service-specific initialization - to be implemented by concrete classes
     */
    protected abstract performInitialization(): Promise<void>;

    /**
     * Performs service-specific cleanup - to be implemented by concrete classes
     */
    protected abstract performCleanup(): Promise<void>;

    /**
     * Updates the health status
     */
    protected updateHealthStatus(isHealthy: boolean, errors: string[] = []): void {
        this.healthStatus$.next({
            isHealthy,
            serviceName: this.getServiceName(),
            timestamp: new Date(),
            errors
        });
    }

    /**
     * Creates a retry observable with exponential backoff
     */
    protected createRetryObservable<T>(
        source: Observable<T>,
        maxRetries: number = this.config.maxRetryAttempts || 3,
        delay: number = this.config.retryDelay || 1000
    ): Observable<T> {
        return source.pipe(
            catchError((error, caught) => {
                if (maxRetries > 0) {
                    this.logWarning(`Retrying operation, ${maxRetries} attempts remaining`);
                    return timer(delay).pipe(
                        tap(() => this.logInfo('Retrying operation')),
                        takeUntil(this.destroy$),
                        catchError(() => caught)
                    );
                }
                throw error;
            })
        );
    }

    /**
     * Performs a health check - to be implemented by concrete classes
     */
    protected abstract performHealthCheck(): Promise<boolean>;

    /**
     * Starts periodic health checks
     */
    protected startHealthChecks(): void {
        if (this.config.enableHealthChecks && this.config.healthCheckInterval) {
            timer(0, this.config.healthCheckInterval)
                .pipe(takeUntil(this.destroy$))
                .subscribe(async () => {
                    const isHealthy = await this.performHealthCheck();
                    this.updateHealthStatus(isHealthy);
                });
        }
    }

    ngOnDestroy(): void {
        this.dispose();
    }
} 