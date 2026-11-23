using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>How one named ingredient of a statement was known: a curated catalog food, a recognised term, a term of label language (<see cref="StatementLabelTerms"/>), a compound known by its listed parts, or unresolved.</summary>
    public static class ContentsKinds
    {
        public const string Food = "food";
        public const string Term = "term";
        public const string Label = "label";
        public const string Compound = "compound";
        public const string Unresolved = "unresolved";
    }

    /// <summary>One named ingredient as stored in <see cref="IngredientContentsEntity.IngredientsJson"/>; a label item names the terms of <see cref="StatementLabelTerms"/> it was read as, joined by |.</summary>
    public sealed record ContentsItem(string Name, string? Parent, string Kind, long? FoodId = null, string? Label = null)
    {
        /// <summary>The label terms the item was read as; an item whose term the table no longer holds gives null, which the gate reads as unknown.</summary>
        public IReadOnlyList<LabelTerm>? LabelTerms()
        {
            if (Kind != ContentsKinds.Label || string.IsNullOrEmpty(Label)) return Array.Empty<LabelTerm>();
            var terms = new List<LabelTerm>();
            foreach (var key in Label.Split('|'))
            {
                if (StatementLabelTerms.ByName.TryGetValue(key, out var term)) terms.Add(term);
                else if (IngredientStatementVocabulary.Terms.Contains(key)) terms.Add(new LabelTerm(key, LabelTermKinds.Term, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "IngredientStatementVocabulary"));
                else return null;
            }
            return terms;
        }
    }

    /// <summary>The allergens as stored in <see cref="IngredientContentsEntity.AllergensJson"/>.</summary>
    public sealed record ContentsAllergens(IReadOnlyList<StatementAllergen> Declared, IReadOnlyList<StatementAllergen> Advisory);

    /// <summary>One statement to record: the food, where the statement came from, and its text.</summary>
    public sealed record ContentsStatement(long IngredientId, string Source, string Statement, long? HouseholdId = null, long? VerificationId = null);

    /// <summary>What a pass over statements did or, in a dry run, would do.</summary>
    public sealed class PreparedContentsReport
    {
        public string Batch { get; init; } = string.Empty;
        public bool Applied { get; init; }
        public int Statements { get; set; }
        public int Written { get; set; }
        public int Unchanged { get; set; }
        public int Read { get; set; }
        public int ReadResolved { get; set; }
        public int Unread { get; set; }
        public Dictionary<string, int> UnreadByReason { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> TopUnresolved { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads prepared meals' ingredient statements (<see cref="IngredientStatementReader"/>, no model) and resolves each
    /// named ingredient by rule: a term of the cited label table (<see cref="StatementLabelTerms"/>, with the facts the
    /// gate reads beside the name) first, keeping the catalog food the name also resolves to; otherwise against the
    /// catalog, as a recipe line's wording is: a verified wording that names a food, then a food's exact name, then a
    /// trusted alias (<see cref="IngredientAliasTrust"/>), each only to a curated food that is not itself a prepared food;
    /// otherwise a recognised term (<see cref="IngredientStatementVocabulary"/>) or a compound known by its listed parts;
    /// otherwise unresolved. Each statement is stored as evidence with its source
    /// (<see cref="IngredientContentsEntity"/>); a newer row of the same food and household supersedes an older one, and a
    /// pass is undone by its batch stamp, which brings the rows it superseded back.
    /// </summary>
    public class PreparedContentsService
    {
        public const string FdcSourcePrefix = "fdc:";

        /// <summary>The version a row is read with: the statement reader's and the label terms' (<see cref="StatementLabelTerms.Version"/>); a row of another version is read again by <see cref="RereadAsync"/>.</summary>
        public const string ReaderVersion = IngredientStatementReader.Version + " " + StatementLabelTerms.Version;
        public const int MaxStatementLength = 8000;
        public const int TopUnresolvedCount = 25;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;

        public PreparedContentsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public static string FdcSource(string fdcId) => FdcSourcePrefix + fdcId.Trim();

        public static string NewBatch(string kind) => $"{kind}-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

        /// <summary>The named ingredients a row stores.</summary>
        public static IReadOnlyList<ContentsItem> Items(IngredientContentsEntity row) =>
            string.IsNullOrEmpty(row.IngredientsJson) ? Array.Empty<ContentsItem>() : JsonSerializer.Deserialize<List<ContentsItem>>(row.IngredientsJson, Json) ?? new List<ContentsItem>();

        /// <summary>The allergens a row stores.</summary>
        public static ContentsAllergens Allergens(IngredientContentsEntity row) =>
            string.IsNullOrEmpty(row.AllergensJson)
                ? new ContentsAllergens(Array.Empty<StatementAllergen>(), Array.Empty<StatementAllergen>())
                : JsonSerializer.Deserialize<ContentsAllergens>(row.AllergensJson, Json) ?? new ContentsAllergens(Array.Empty<StatementAllergen>(), Array.Empty<StatementAllergen>());

        /// <summary>
        /// Reads and resolves each statement and, when <paramref name="apply"/> is set, stores a row for each one whose food,
        /// household, source, text and reader version differ from that food and household's current row. A statement that
        /// is the same as the current row's is left alone.
        /// </summary>
        public async Task<PreparedContentsReport> RecordAsync(IReadOnlyCollection<ContentsStatement> statements, string batch, bool apply, CancellationToken ct = default)
        {
            var report = new PreparedContentsReport { Batch = batch, Applied = apply, Statements = statements.Count };
            if (statements.Count == 0) return report;

            var foodIds = statements.Select(s => s.IngredientId).Distinct().ToList();
            var current = (await _db.IngredientContents.AsNoTracking()
                    .Where(c => foodIds.Contains(c.IngredientId))
                    .Select(c => new { c.Id, c.IngredientId, c.HouseholdId, c.Source, c.Statement, c.ReaderVersion })
                    .ToListAsync(ct))
                .GroupBy(c => (c.IngredientId, c.HouseholdId))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).First());

            var readings = statements.Select(s => (Statement: s, Text: Clip(s.Statement), Reading: IngredientStatementReader.Read(s.Statement))).ToList();
            var resolver = await ResolverAsync(readings.Where(r => r.Reading.Read).SelectMany(r => r.Reading.Ingredients).Select(i => i.Name), ct);
            var now = DateTime.UtcNow;

            foreach (var (statement, text, reading) in readings)
            {
                var row = Build(statement, text, reading, resolver, batch, now);
                Count(report, row);
                if (current.TryGetValue((statement.IngredientId, statement.HouseholdId), out var existing)
                    && existing.Source == row.Source && existing.Statement == row.Statement && existing.ReaderVersion == row.ReaderVersion)
                {
                    report.Unchanged++;
                    continue;
                }
                report.Written++;
                if (apply) _db.IngredientContents.Add(row);
            }
            if (apply) await _db.SaveChangesAsync(ct);
            return report;
        }

        /// <summary>Records the statement a household confirmed on a label of a prepared food; nothing when the food is not one or the row holds no statement.</summary>
        public async Task<IngredientContentsEntity?> RecordLabelAsync(IngredientVerificationEntity label, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(label.IngredientStatement) || label.HouseholdId == null) return null;
            var food = await _db.Ingredients.AsNoTracking().FirstOrDefaultAsync(i => i.Id == label.IngredientId, ct);
            if (food == null || !PreparedFoods.IsPrepared(food)) return null;
            var batch = $"label-{label.Id.ToString(CultureInfo.InvariantCulture)}";
            await RecordAsync(new[] { new ContentsStatement(label.IngredientId, label.Source, label.IngredientStatement, label.HouseholdId, label.Id) }, batch, apply: true, ct);
            return await _db.IngredientContents.AsNoTracking()
                .Where(c => c.IngredientId == label.IngredientId && c.HouseholdId == label.HouseholdId)
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>Reads every current row again with the current reader, vocabulary and catalog, writing a new row under <paramref name="batch"/> for each one whose reading changed.</summary>
        public async Task<PreparedContentsReport> RereadAsync(string batch, bool apply, CancellationToken ct = default)
        {
            var rows = (await _db.IngredientContents.AsNoTracking().ToListAsync(ct))
                .GroupBy(c => (c.IngredientId, c.HouseholdId))
                .Select(g => g.OrderByDescending(c => c.Id).First())
                .ToList();
            var readings = rows.Select(r => (Row: r, Reading: IngredientStatementReader.Read(r.Statement))).ToList();
            var resolver = await ResolverAsync(readings.Where(r => r.Reading.Read).SelectMany(r => r.Reading.Ingredients).Select(i => i.Name), ct);
            var report = new PreparedContentsReport { Batch = batch, Applied = apply, Statements = rows.Count };
            var now = DateTime.UtcNow;
            foreach (var (old, reading) in readings)
            {
                var row = Build(new ContentsStatement(old.IngredientId, old.Source, old.Statement, old.HouseholdId, old.VerificationId), old.Statement, reading, resolver, batch, now);
                row.ReadFromId = old.Id;
                Count(report, row);
                if (row.Outcome == old.Outcome && row.Reason == old.Reason && row.IngredientsJson == old.IngredientsJson && row.AllergensJson == old.AllergensJson && row.ReaderVersion == old.ReaderVersion)
                {
                    report.Unchanged++;
                    continue;
                }
                report.Written++;
                if (apply) _db.IngredientContents.Add(row);
            }
            if (apply) await _db.SaveChangesAsync(ct);
            return report;
        }

        /// <summary>
        /// Takes out the rows a pass wrote and every row a later reread read again from one of them, which brings back the
        /// rows they superseded; returns how many rows it took out or, in a dry run, would.
        /// </summary>
        public async Task<int> UndoAsync(string batch, bool apply, CancellationToken ct = default)
        {
            var rows = await _db.IngredientContents.Where(c => c.Batch == batch).ToListAsync(ct);
            var ids = rows.Select(r => r.Id).ToHashSet();
            for (var from = ids.ToList(); from.Count > 0;)
            {
                var rereads = await _db.IngredientContents
                    .Where(c => c.ReadFromId != null && from.Contains(c.ReadFromId.Value))
                    .ToListAsync(ct);
                rereads = rereads.Where(r => ids.Add(r.Id)).ToList();
                rows.AddRange(rereads);
                from = rereads.Select(r => r.Id).ToList();
            }
            if (apply && rows.Count > 0)
            {
                _db.IngredientContents.RemoveRange(rows);
                await _db.SaveChangesAsync(ct);
            }
            return rows.Count;
        }

        private static void Count(PreparedContentsReport report, IngredientContentsEntity row)
        {
            if (row.Outcome == IngredientContentsOutcome.Read)
            {
                report.Read++;
                if (row.Unresolved == 0) report.ReadResolved++;
                foreach (var item in Items(row).Where(i => i.Kind == ContentsKinds.Unresolved))
                    report.TopUnresolved[item.Name] = report.TopUnresolved.GetValueOrDefault(item.Name) + 1;
            }
            else
            {
                report.Unread++;
                var key = (row.Reason ?? "unread").Split(':')[0];
                report.UnreadByReason[key] = report.UnreadByReason.GetValueOrDefault(key) + 1;
            }
        }

        internal static IngredientContentsEntity Build(ContentsStatement statement, string text, IngredientStatementReading reading, IReadOnlyDictionary<string, long> resolver, string batch, DateTime now)
        {
            var row = new IngredientContentsEntity
            {
                IngredientId = statement.IngredientId,
                HouseholdId = statement.HouseholdId,
                VerificationId = statement.VerificationId,
                Source = statement.Source,
                Statement = text,
                ReaderVersion = ReaderVersion,
                Batch = batch,
                CreatedDate = now,
            };
            if (!reading.Read)
            {
                row.Outcome = IngredientContentsOutcome.Unread;
                row.Reason = reading.Reason is { Length: > 255 } r ? r[..255] : reading.Reason;
                return row;
            }
            var items = reading.Ingredients.Select(i => Resolve(i, resolver)).ToList();
            row.Outcome = IngredientContentsOutcome.Read;
            row.IngredientsJson = JsonSerializer.Serialize(items, Json);
            row.AllergensJson = JsonSerializer.Serialize(new ContentsAllergens(reading.Declared, reading.Advisory), Json);
            row.Unresolved = items.Count(i => i.Kind == ContentsKinds.Unresolved);
            return row;
        }

        /// <summary>
        /// How one named ingredient is known, by the catalog keys a pass looked up (none when there is no catalog to read). A name
        /// the cited label table reads (<see cref="StatementLabelTerms"/>) is a label item first, keeping the catalog food it also
        /// resolves to, so what a term may be made of is never lost to a catalog food of the same name ("vegetable oil" may be
        /// peanut oil, its source not stated: staging checkpoint 4, P4); otherwise the catalog food, then the vocabulary.
        /// </summary>
        internal static ContentsItem Resolve(StatementIngredient ingredient, IReadOnlyDictionary<string, long> resolver)
        {
            long? catalog = null;
            foreach (var key in Keys(ingredient.Name))
                if (resolver.TryGetValue(key, out var foodId))
                {
                    catalog = foodId;
                    break;
                }
            var terms = StatementLabelTerms.Match(ingredient.Name, ingredient.HasParts);
            if (terms != null && terms.Any(t => t.Kind != LabelTermKinds.Term))
                return new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Label, catalog, string.Join('|', terms.Select(t => t.Term)));
            if (catalog is { } food) return new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Food, food);
            if (terms != null)
                return terms.Count == 1 && terms[0].Kind == LabelTermKinds.Term && terms[0].Term == ingredient.Name
                    ? new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Term)
                    : new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Label, null, string.Join('|', terms.Select(t => t.Term)));
            if (IngredientStatementVocabulary.Terms.Contains(ingredient.Name)) return new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Term);
            if (IngredientStatementVocabulary.Recognises(ingredient)) return new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Compound);
            return new ContentsItem(ingredient.Name, ingredient.Parent, ContentsKinds.Unresolved);
        }

        private static IEnumerable<string> Keys(string name) => IngredientNameNormalizer.ExactKeys(name).Select(k => k.ToLowerInvariant()).Distinct();

        /// <summary>
        /// The curated, non-prepared catalog food each lookup key names: a verified wording first, then a food's exact name,
        /// then a trusted alias. A key that names two different foods at the same step resolves to neither.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, long>> ResolverAsync(IEnumerable<string> names, CancellationToken ct)
        {
            var keys = names.Distinct(StringComparer.Ordinal).SelectMany(Keys).Distinct(StringComparer.Ordinal).ToList();
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            if (keys.Count == 0) return result;
            var curated = (long)CurationStatusEnum.Curated;

            foreach (var chunk in keys.Chunk(500))
            {
                var wordings = await _db.IngredientWordings.AsNoTracking()
                    .Where(w => chunk.Contains(w.Wording) && w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                    .Join(_db.Ingredients.Where(i => i.CurationStatusId == curated).Where(PreparedFoods.NotPrepared), w => w.IngredientId, i => (long?)i.Id, (w, i) => new { w.Wording, i.Id })
                    .ToListAsync(ct);
                var byName = await _db.Ingredients.AsNoTracking()
                    .Where(i => i.CurationStatusId == curated && chunk.Contains(i.Name.ToLower()))
                    .Where(PreparedFoods.NotPrepared)
                    .Select(i => new { Key = i.Name.ToLower(), i.Id })
                    .ToListAsync(ct);
                var byAlias = (await _db.Set<IngredientAliasEntity>().AsNoTracking()
                        .Where(a => chunk.Contains(a.AliasName.ToLower()))
                        .Join(_db.Ingredients.Where(i => i.CurationStatusId == curated).Where(PreparedFoods.NotPrepared), a => a.IngredientId, i => i.Id, (a, i) => new { Key = a.AliasName.ToLower(), i.Id, a.SourceContext })
                        .ToListAsync(ct))
                    .Where(a => IngredientAliasTrust.IsTrusted(a.SourceContext))
                    .Select(a => new { a.Key, a.Id })
                    .ToList();

                foreach (var step in new[] { wordings.Select(w => (Key: w.Wording, w.Id)), byName.Select(n => (n.Key, n.Id)), byAlias.Select(a => (a.Key, a.Id)) })
                {
                    foreach (var group in step.GroupBy(p => p.Key, StringComparer.Ordinal))
                    {
                        if (result.ContainsKey(group.Key)) continue;
                        var ids = group.Select(p => p.Id).Distinct().ToList();
                        if (ids.Count == 1) result[group.Key] = ids[0];
                        else result[group.Key] = 0;
                    }
                }
            }
            foreach (var ambiguous in result.Where(r => r.Value == 0).Select(r => r.Key).ToList()) result.Remove(ambiguous);
            return result;
        }

        private static string Clip(string statement)
        {
            var t = statement.Trim();
            return t.Length <= MaxStatementLength ? t : t[..MaxStatementLength];
        }
    }
}
