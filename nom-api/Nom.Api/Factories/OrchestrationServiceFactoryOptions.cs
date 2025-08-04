// File: Nom.Api/_Abstractions/_Factories/OrchestrationServiceFactory.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Api.Core;
using Nom.Api.Factories.Interfaces;

namespace Nom.Api.Factories
{
    /// <summary>
    /// Configuration options for the orchestration service factory
    /// </summary>
    public class OrchestrationServiceFactoryOptions
    {
        /// <summary>
        /// Whether to enable automatic service discovery
        /// </summary>
        public bool EnableAutoDiscovery { get; set; } = true;

        /// <summary>
        /// Whether to enable service caching
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration for services (in minutes)
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 30;

        /// <summary>
        /// Whether to enable service validation
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// Whether to enable service logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;
    }
}