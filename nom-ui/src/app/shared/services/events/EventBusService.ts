// File: nom-ui/src/app/shared/services/events/EventBusService.ts

import { Injectable, OnDestroy } from '@angular/core';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { _EventBusStatistics } from './_EventBusStatistics';

/**
 * Concrete event bus service implementation for frontend pub-sub pattern
 */
@Injectable({
    providedIn: 'root'
})
export class EventBusService implements OnDestroy {
    private readonly destroy$ = new Subject<void>();
    private readonly events$ = new Subject<{ type: string; data: unknown }>();
    private readonly subscribers = new Map<string, Set<(event: unknown) => void>>();
    private readonly statistics = {
        totalEvents: 0,
        totalSubscribers: 0,
        eventTypeCounts: new Map<string, number>(),
        subscriberCounts: new Map<string, number>()
    };

    constructor() {
        this.setupEventProcessing();
    }

    /**
     * Publishes an event
     */
    publish<T>(event: T): void {
        const eventType = this.getEventType(event);

        this.statistics.totalEvents++;
        this.statistics.eventTypeCounts.set(
            eventType,
            (this.statistics.eventTypeCounts.get(eventType) || 0) + 1
        );

        console.log(`[EventBus] Publishing event: ${eventType}`, event);
        this.events$.next({ type: eventType, data: event });
    }

    /**
     * Subscribes to an event
     */
    subscribe<T>(eventType: string, handler: (event: T) => void): void {
        if (!this.subscribers.has(eventType)) {
            this.subscribers.set(eventType, new Set());
        }

        const eventSubscribers = this.subscribers.get(eventType)!;
        eventSubscribers.add(handler as (event: unknown) => void);

        this.statistics.totalSubscribers++;
        this.statistics.subscriberCounts.set(
            eventType,
            (this.statistics.subscriberCounts.get(eventType) || 0) + 1
        );

        console.log(`[EventBus] Subscribed to event: ${eventType}`);
    }

    /**
     * Unsubscribes from an event
     */
    unsubscribe<T>(eventType: string, handler: (event: T) => void): void {
        const eventSubscribers = this.subscribers.get(eventType);
        if (eventSubscribers) {
            eventSubscribers.delete(handler as (event: unknown) => void);

            this.statistics.totalSubscribers--;
            const currentCount = this.statistics.subscriberCounts.get(eventType) || 0;
            this.statistics.subscriberCounts.set(eventType, Math.max(0, currentCount - 1));

            if (eventSubscribers.size === 0) {
                this.subscribers.delete(eventType);
            }

            console.log(`[EventBus] Unsubscribed from event: ${eventType}`);
        }
    }

    /**
     * Gets the number of subscribers for an event type
     */
    getSubscriberCount(eventType: string): number {
        const eventSubscribers = this.subscribers.get(eventType);
        return eventSubscribers ? eventSubscribers.size : 0;
    }

    /**
     * Clears all subscribers for an event type
     */
    clearSubscribers(eventType: string): void {
        const eventSubscribers = this.subscribers.get(eventType);
        if (eventSubscribers) {
            const subscriberCount = eventSubscribers.size;
            eventSubscribers.clear();
            this.subscribers.delete(eventType);

            this.statistics.totalSubscribers -= subscriberCount;
            this.statistics.subscriberCounts.delete(eventType);

            console.log(`[EventBus] Cleared ${subscriberCount} subscribers for event: ${eventType}`);
        }
    }

    /**
     * Clears all subscribers
     */
    clearAllSubscribers(): void {
        const totalSubscribers = this.statistics.totalSubscribers;
        this.subscribers.clear();
        this.statistics.totalSubscribers = 0;
        this.statistics.subscriberCounts.clear();

        console.log(`[EventBus] Cleared all ${totalSubscribers} subscribers`);
    }

    /**
     * Gets event bus statistics
     */
    getStatistics(): _EventBusStatistics {
        return {
            totalEvents: this.statistics.totalEvents,
            totalSubscribers: this.statistics.totalSubscribers,
            eventTypeCounts: new Map(this.statistics.eventTypeCounts),
            subscriberCounts: new Map(this.statistics.subscriberCounts),
            activeEventTypes: this.subscribers.size,
            lastUpdated: new Date()
        };
    }

    /**
     * Gets all active event types
     */
    getActiveEventTypes(): string[] {
        return Array.from(this.subscribers.keys());
    }

    /**
     * Gets all subscribers for an event type
     */
    getSubscribers(eventType: string): ((event: unknown) => void)[] {
        const eventSubscribers = this.subscribers.get(eventType);
        return eventSubscribers ? Array.from(eventSubscribers) : [];
    }

    /**
     * Checks if there are any subscribers for an event type
     */
    hasSubscribers(eventType: string): boolean {
        return this.subscribers.has(eventType) && this.subscribers.get(eventType)!.size > 0;
    }

    /**
     * Gets the total number of event types
     */
    getEventTypeCount(): number {
        return this.subscribers.size;
    }

    /**
     * Gets the total number of subscribers
     */
    getTotalSubscriberCount(): number {
        return this.statistics.totalSubscribers;
    }

    /**
     * Gets the total number of events published
     */
    getTotalEventCount(): number {
        return this.statistics.totalEvents;
    }

    /**
     * Resets all statistics
     */
    resetStatistics(): void {
        this.statistics.totalEvents = 0;
        this.statistics.totalSubscribers = 0;
        this.statistics.eventTypeCounts.clear();
        this.statistics.subscriberCounts.clear();

        console.log('[EventBus] Statistics reset');
    }

    /**
     * Sets up event processing
     */
    private setupEventProcessing(): void {
        this.events$
            .pipe(takeUntil(this.destroy$))
            .subscribe(event => {
                const { type, data } = event;
                const eventSubscribers = this.subscribers.get(type);

                if (eventSubscribers && eventSubscribers.size > 0) {
                    console.log(`[EventBus] Processing event: ${type} with ${eventSubscribers.size} subscribers`);

                    eventSubscribers.forEach(handler => {
                        try {
                            handler(data);
                        } catch (error) {
                            console.error(`[EventBus] Error in event handler for ${type}:`, error);
                        }
                    });
                } else {
                    console.log(`[EventBus] No subscribers for event: ${type}`);
                }
            });
    }

    /**
     * Gets the event type from an event object
     */
    private getEventType(event: unknown): string {
        if (typeof event === 'string') {
            return event;
        }

        if (event && typeof event === 'object') {
            return event.type || event.constructor?.name || 'UnknownEvent';
        }

        return 'UnknownEvent';
    }

    ngOnDestroy(): void {
        this.clearAllSubscribers();
        this.destroy$.next();
        this.destroy$.complete();
        console.log('[EventBus] Destroyed');
    }
} 