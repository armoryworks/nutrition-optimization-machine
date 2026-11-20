using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Builds the stock-phrase index from the stored source documents: every five-word phrase of
    /// each document's description and method steps (<see cref="ProsePhrases"/>), counted once per
    /// site, kept when <see cref="ProseStockIndex.StockSites"/> or more sites use it. The same
    /// documents always give the same index. Each write replaces the whole index under the next
    /// version and is journaled.
    /// </summary>
    public class ProseStockService : IProseStockService
    {
        public const string EntityType = "ProseStockPhrase";
        public const string RebuildChangeType = "ProseStockRebuild";

        private const int DocumentsPerRead = 500;
        private const int RowsPerSave = 10000;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public ProseStockService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private struct Seen
        {
            public int Sites;
            public int LastSite;
        }

        public async Task<ProseStockRebuildResult> RebuildAsync(bool apply = false, CancellationToken cancellationToken = default)
        {
            var result = new ProseStockRebuildResult { Applied = apply };
            var documents = await _db.ScrapedDocuments.AsNoTracking()
                .OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.SourceUrl })
                .ToListAsync(cancellationToken);
            result.Documents = documents.Count;

            var bySite = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                if (Site(document.SourceUrl) is not { } site)
                {
                    result.DocumentsWithoutSite++;
                    continue;
                }

                if (!bySite.TryGetValue(site, out var ids)) bySite[site] = ids = new List<long>();
                ids.Add(document.Id);
            }

            var seen = new Dictionary<long, Seen>();
            var siteNumber = 0;
            foreach (var (site, ids) in bySite.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                siteNumber++;
                var read = 0;
                foreach (var chunk in ids.Chunk(DocumentsPerRead))
                {
                    var raws = await _db.ScrapedDocuments.AsNoTracking()
                        .Where(d => chunk.Contains(d.Id))
                        .Select(d => d.RawJsonLd)
                        .ToListAsync(cancellationToken);
                    foreach (var raw in raws)
                    {
                        if (RewriteFidelity.Source(raw) is not { } source)
                        {
                            result.DocumentsUnread++;
                            continue;
                        }

                        read++;
                        foreach (var text in source.Steps.Prepend(source.Description))
                        {
                            foreach (var hash in ProsePhrases.Hashes(ProsePhrases.Words(text)))
                            {
                                seen.TryGetValue(hash, out var entry);
                                if (entry.LastSite == siteNumber) continue;
                                entry.LastSite = siteNumber;
                                entry.Sites++;
                                seen[hash] = entry;
                            }
                        }
                    }
                }

                if (read > 0) result.DocumentsBySite[site] = read;
            }

            result.Sites = result.DocumentsBySite.Count;
            result.Phrases = seen.Count;
            var stock = seen.Where(e => e.Value.Sites >= ProseStockIndex.StockSites).ToDictionary(e => e.Key, e => e.Value.Sites);
            result.StockPhrases = stock.Count;

            var current = await LoadAsync(cancellationToken);
            var currentHashes = current.Version == 0
                ? new HashSet<long>()
                : (await _db.ProseStockPhrases.AsNoTracking().Where(p => p.Version == current.Version).Select(p => p.Hash).ToListAsync(cancellationToken)).ToHashSet();
            result.CurrentVersion = current.Version;
            result.Added = stock.Keys.Count(h => !currentHashes.Contains(h));
            result.Removed = currentHashes.Count(h => !stock.ContainsKey(h));
            var unchanged = current.Version > 0 && result.Added == 0 && result.Removed == 0;
            result.Version = unchanged ? current.Version : current.Version + 1;
            if (!apply || unchanged) return result;

            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
            if (_db.Database.IsRelational())
            {
                await _db.ProseStockPhrases.ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                _db.ProseStockPhrases.RemoveRange(await _db.ProseStockPhrases.ToListAsync(cancellationToken));
                await _db.SaveChangesAsync(cancellationToken);
            }

            foreach (var chunk in stock.OrderBy(e => e.Key).Chunk(RowsPerSave))
            {
                _db.ProseStockPhrases.AddRange(chunk.Select(e => new ProseStockPhraseEntity { Hash = e.Key, SiteCount = e.Value, Version = result.Version }));
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = EntityType,
                EntityId = result.Version,
                ChangeType = RebuildChangeType,
                PropertyName = nameof(ProseStockPhraseEntity.Version),
                OldValue = string.Create(CultureInfo.InvariantCulture, $"version {current.Version}: {currentHashes.Count} phrases"),
                NewValue = string.Create(CultureInfo.InvariantCulture,
                    $"version {result.Version}: {result.StockPhrases} phrases of {ProseStockIndex.StockSites}+ sites from {result.Documents - result.DocumentsUnread - result.DocumentsWithoutSite} documents on {result.Sites} sites; added {result.Added}, removed {result.Removed}"),
                Timestamp = DateTime.UtcNow,
                ChangedByPersonId = _currentUser.PersonIdOrSystem,
            });
            await _db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return result;
        }

        public async Task<ProseStockIndex> LoadAsync(CancellationToken cancellationToken = default)
        {
            var version = await _db.ProseStockPhrases.AsNoTracking().MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0;
            if (version == 0) return ProseStockIndex.Empty;
            var hashes = await _db.ProseStockPhrases.AsNoTracking().Where(p => p.Version == version).Select(p => p.Hash).ToListAsync(cancellationToken);
            return new ProseStockIndex(version, hashes);
        }

        /// <summary>The recipe site a source address belongs to: its host, lower-cased, without "www."; null when it names none.</summary>
        public static string? Site(string? sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
            var host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        }
    }
}
