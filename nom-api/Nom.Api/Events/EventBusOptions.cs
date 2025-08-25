namespace Nom.Api.Events
{
    /// <summary>
    /// Configuration options for the event bus
    /// </summary>
    public class EventBusOptions
    {
        /// <summary>
        /// Whether to enable the event bus
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether to enable event logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable event validation
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent event handlers
        /// </summary>
        public int MaxConcurrentHandlers { get; set; } = 10;

        /// <summary>
        /// Event handler timeout in seconds
        /// </summary>
        public int HandlerTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether to enable event correlation
        /// </summary>
        public bool EnableCorrelation { get; set; } = true;

        /// <summary>
        /// Whether to enable event retry on failure
        /// </summary>
        public bool EnableRetry { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;
    }
}



