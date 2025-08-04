// File: Nom.Api/_Abstractions/_DI/IDependencyResolver.cs

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Nom.Api.DI
{
    /// <summary>
    /// Statistics for dependency resolution
    /// </summary>
    public class DependencyResolutionStatistics
    {
        /// <summary>
        /// Total number of service resolutions
        /// </summary>
        public int TotalResolutions { get; set; }

        /// <summary>
        /// Number of successful resolutions
        /// </summary>
        public int SuccessfulResolutions { get; set; }

        /// <summary>
        /// Number of failed resolutions
        /// </summary>
        public int FailedResolutions { get; set; }

        /// <summary>
        /// Number of singleton resolutions
        /// </summary>
        public int SingletonResolutions { get; set; }

        /// <summary>
        /// Number of scoped resolutions
        /// </summary>
        public int ScopedResolutions { get; set; }

        /// <summary>
        /// Number of transient resolutions
        /// </summary>
        public int TransientResolutions { get; set; }

        /// <summary>
        /// Average resolution time in milliseconds
        /// </summary>
        public double AverageResolutionTimeMs { get; set; }

        /// <summary>
        /// Total resolution time in milliseconds
        /// </summary>
        public double TotalResolutionTimeMs { get; set; }

        /// <summary>
        /// Number of active scopes
        /// </summary>
        public int ActiveScopes { get; set; }

        /// <summary>
        /// Number of disposed scopes
        /// </summary>
        public int DisposedScopes { get; set; }

        /// <summary>
        /// Service types and their resolution counts
        /// </summary>
        public Dictionary<string, int> ServiceTypeCounts { get; set; } = new();

        /// <summary>
        /// Timestamp of the last statistics update
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}