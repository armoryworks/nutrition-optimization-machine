// File: Nom.Api/_Abstractions/_Core/BaseCacheService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api.Core
{

    /// <summary>
    /// Cache entry
    /// </summary>
    public class CacheEntry
    {
        public string Value { get; set; } = string.Empty;
        public DateTime? ExpirationTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastAccessedTime { get; set; }

        public bool IsExpired => ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
    }
}