using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Nom.Data;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The diet gate's whole-catalog probes, computed once and kept until the data they read
    /// changes (alcohol design A9, staging checkpoint 1 K8). Each probe (the foods a restriction
    /// type's criteria match, the recipe lines whose own text reads as a drink) is a regular
    /// expression over every food name or every recipe line and costs seconds on the production
    /// catalog, while every household set (meal plans, search, "Surprise me") needs it. A value
    /// stays fresh until <see cref="Invalidator"/> sees a write to a food, an alias, a food's
    /// nutrients or a recipe line, or until <see cref="MaxAge"/> passes (writes from another
    /// process, such as Nom.Import). A stale value is served while one refresh runs on its own
    /// context, so a write never makes a household wait; only the first read computes in line. A
    /// stale value is never served once its refresh failed (the failure is logged) or once it is
    /// older than twice <see cref="MaxAge"/>: the reader then computes it in line, so a probe that
    /// keeps failing shows as an error rather than as an old set.
    /// </summary>
    public sealed class DietGateCache
    {
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(15);

        /// <summary>The process-wide cache the API reads through, keyed by database.</summary>
        public static readonly DietGateCache Shared = new();

        private sealed class Entry
        {
            public Entry(object value, long generation, DateTime at)
            {
                Value = value;
                Generation = generation;
                At = at;
            }

            public object Value { get; }
            public long Generation { get; }
            public DateTime At { get; }
            public int Refreshing;
            public int RefreshFailed;
        }

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _clock;
        private long _generation;
        private int _computations;

        public DietGateCache(TimeSpan? maxAge = null, Func<DateTime>? clock = null, bool refreshInBackground = true)
        {
            MaxAge = maxAge ?? DefaultMaxAge;
            _clock = clock ?? (() => DateTime.UtcNow);
            RefreshInBackground = refreshInBackground;
            Invalidator = new DietGateCacheInvalidator(this);
        }

        public TimeSpan MaxAge { get; }

        /// <summary>Whether a stale value is served while a refresh runs on a new context; when false the reader that finds it stale recomputes it in line.</summary>
        public bool RefreshInBackground { get; }

        /// <summary>The interceptor that marks every value stale when a command writes a table the probes read; registered on the API's database context.</summary>
        public DietGateCacheInvalidator Invalidator { get; }

        /// <summary>Where a failed background refresh is logged; the API sets it at start-up.</summary>
        public ILogger? Logger { get; set; }

        public long Generation => Interlocked.Read(ref _generation);

        /// <summary>How many times a probe has been computed, for tests and logs.</summary>
        public int Computations => Volatile.Read(ref _computations);

        /// <summary>Marks every value stale.</summary>
        public void Invalidate() => Interlocked.Increment(ref _generation);

        /// <summary>
        /// The value of the probe <paramref name="key"/> on the context's database: the kept value
        /// while it is fresh; a stale one while a refresh runs; computed in line when there is none,
        /// when it is stale and no refresh can run apart, when its last refresh failed, or when it is
        /// older than twice <see cref="MaxAge"/>.
        /// </summary>
        public async Task<T> GetAsync<T>(ApplicationDbContext db, string key, Func<ApplicationDbContext, Task<T>> compute) where T : class
        {
            var full = DatabaseKey(db) + "|" + key;
            if (_entries.TryGetValue(full, out var entry))
            {
                if (IsFresh(entry)) return (T)entry.Value;
                if (RefreshInBackground && CanServeStale(entry) && Fork(db) is { } fork)
                {
                    StartRefresh(full, key, entry, fork, compute);
                    return (T)entry.Value;
                }
            }

            var gate = _gates.GetOrAdd(full, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_entries.TryGetValue(full, out entry) && IsFresh(entry)) return (T)entry.Value;
                var generation = Generation;
                var value = await compute(db);
                Interlocked.Increment(ref _computations);
                _entries[full] = new Entry(value, generation, _clock());
                return value;
            }
            finally
            {
                gate.Release();
            }
        }

        private bool IsFresh(Entry entry) => entry.Generation == Generation && _clock() - entry.At < MaxAge;

        private bool CanServeStale(Entry entry) => Volatile.Read(ref entry.RefreshFailed) == 0 && _clock() - entry.At < MaxAge + MaxAge;

        private void StartRefresh<T>(string full, string key, Entry entry, Func<ApplicationDbContext> fork, Func<ApplicationDbContext, Task<T>> compute) where T : class
        {
            if (Interlocked.CompareExchange(ref entry.Refreshing, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var db = fork();
                    var generation = Generation;
                    var value = await compute(db);
                    Interlocked.Increment(ref _computations);
                    _entries[full] = new Entry(value, generation, _clock());
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref entry.RefreshFailed, 1);
                    Volatile.Write(ref entry.Refreshing, 0);
                    Logger?.LogWarning(ex, "Diet gate: the refresh of probe {Probe} failed; the value computed at {ComputedAt:u} is no longer served and the next household set computes it in line", key, entry.At);
                }
            });
        }

        private static Func<ApplicationDbContext>? Fork(ApplicationDbContext db)
        {
            try
            {
                return db.GetService<IDbContextOptions>() is DbContextOptions<ApplicationDbContext> options ? () => new ApplicationDbContext(options) : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string DatabaseKey(ApplicationDbContext db)
        {
            if (!db.Database.IsRelational()) return db.Database.ProviderName ?? string.Empty;
            var connection = db.Database.GetConnectionString() ?? string.Empty;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection)))[..16];
        }
    }

    /// <summary>
    /// Marks the diet gate's cached probes stale when a command writes a food, an alias, a food's
    /// nutrients, a recipe line, a recipe's servings or a measurement: at once for a command outside a transaction, at the commit for
    /// one inside, so a refresh never reads the state before an uncommitted write and keeps it.
    /// SaveChanges, bulk updates and raw SQL all pass here.
    /// </summary>
    public sealed class DietGateCacheInvalidator : IDbCommandInterceptor, IDbTransactionInterceptor
    {
        private static readonly Regex Write = new(
            @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE\s+INTO|TRUNCATE(?:\s+TABLE)?|COPY)\s+(?:ONLY\s+)?(?:""?(?:recipe|nutrient)""?\.)?""?(?:RecipeIngredient|Ingredient|IngredientAlias|IngredientNutrient|IngredientContents)""?(?![\w""])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RecipeWrite = new(
            @"\b(?:INSERT\s+INTO|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|COPY)\s+(?:ONLY\s+)?(?:""?recipe""?\.)?""?Recipe""?(?![\w""])|\bUPDATE\s+(?:ONLY\s+)?(?:""?recipe""?\.)?""?Recipe""?(?![\w""])[\s\S]*""(?:Servings|RecipeServings|IsDeleted)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MeasurementWrite = new(
            @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE\s+INTO|TRUNCATE(?:\s+TABLE)?|COPY)\s+(?:ONLY\s+)?(?:""?measurement""?\.)?""?Measurement""?(?![\w"".])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly DietGateCache _cache;
        private readonly ConditionalWeakTable<DbTransaction, object> _pending = new();

        public DietGateCacheInvalidator(DietGateCache cache) => _cache = cache;

        /// <summary>Whether the command text writes a table the diet gate's probes read: a food, an alias, a food's nutrients, a food's ingredient statement, a recipe line (with its alcohol retention), a recipe's servings, which the retained alcohol per serving divides by, or a measurement, by which a drink line is weighed.</summary>
        public static bool Touches(string? commandText) =>
            !string.IsNullOrEmpty(commandText)
            && ((commandText.Contains("Ingredient", StringComparison.OrdinalIgnoreCase) && Write.IsMatch(commandText))
                || (commandText.Contains("Recipe", StringComparison.OrdinalIgnoreCase) && RecipeWrite.IsMatch(commandText))
                || (commandText.Contains("Measurement", StringComparison.OrdinalIgnoreCase) && MeasurementWrite.IsMatch(commandText)));

        /// <summary>Marks the cache stale for a write outside a transaction, or remembers the transaction for its commit.</summary>
        public void Executed(DbCommand command)
        {
            if (!Touches(command.CommandText)) return;
            if (command.Transaction is { } transaction) _pending.AddOrUpdate(transaction, transaction);
            else _cache.Invalidate();
        }

        /// <summary>Marks the cache stale when a transaction that wrote a probed table commits.</summary>
        public void Committed(DbTransaction transaction)
        {
            if (_pending.TryGetValue(transaction, out _))
            {
                _pending.Remove(transaction);
                _cache.Invalidate();
            }
        }

        public int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Executed(command);
            return result;
        }

        public ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Executed(command);
            return ValueTask.FromResult(result);
        }

        public DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Executed(command);
            return result;
        }

        public ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Executed(command);
            return ValueTask.FromResult(result);
        }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(transaction);

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed(transaction);
            return Task.CompletedTask;
        }
    }
}
