using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>One stored reading as the export gives it: the recipe, the model, the prompt and text signatures it was made under, and the answer per type (word and carrier food).</summary>
    public sealed record DietReadingExport(long RecipeId, string Model, string PromptSignature, string TextSignature, string StoreVersion, bool Readable, bool Complete,
        IReadOnlyList<string> Unlisted, IReadOnlyDictionary<string, string[]> Answers, double Seconds, DateTime ReadAt);

    /// <summary>A page of the export; <see cref="Next"/> is the recipe id to continue after, null on the last page.</summary>
    public sealed record DietReadingExportPage(int Recipes, IReadOnlyList<DietReadingExport> Items, long? Next);

    /// <summary>The text a reading was made of, for an answers file of the offline scorer that carries no text signature: the signature is taken from it as the lane takes it (<see cref="DietSecondGate.TextSignature"/>).</summary>
    public sealed class DietReadingText
    {
        public string Title { get; set; } = string.Empty;
        public List<string> Lines { get; set; } = new();
        public List<string> Method { get; set; } = new();
    }

    /// <summary>One reading to import: an exported reading, or a line of the offline scorer's answers file with its model, prompt signature and either its text signature or its text.</summary>
    public sealed class DietReadingImportItem
    {
        public long RecipeId { get; set; }
        public string Model { get; set; } = string.Empty;
        public string? PromptSignature { get; set; }
        public string? TextSignature { get; set; }
        public DietReadingText? Text { get; set; }
        public bool? Readable { get; set; }
        public bool Complete { get; set; }
        public List<string>? Unlisted { get; set; }
        public Dictionary<string, string[]>? Answers { get; set; }
        public double Seconds { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    /// <summary>What an import took, or would take in a dry run, and why it left the rest.</summary>
    public sealed class DietReadingImportReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Received { get; set; }
        public int Taken { get; set; }
        public Dictionary<string, int> TakenByModel { get; } = new();
        public Dictionary<string, int> Skipped { get; } = new();
        public List<string> Lines { get; } = new();
    }

    /// <summary>What an undo of an import took back, or would in a dry run.</summary>
    public sealed class DietReadingUndoReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Undone { get; set; }
        public int Skipped { get; set; }
        public List<string> Lines { get; } = new();
    }

    public partial class DietSecondReaderLaneService
    {
        public const string ReadingEntityType = "RecipeDietReading";
        public const string ImportChange = "DietReadingImport";
        public const string ImportUndoChange = "DietReadingImportUndo";
        public const int MaxImport = 5000;
        private const int ShownLines = 200;

        public const string SkipModel = "model not of the pair";
        public const string SkipPrompt = "prompt signature differs";
        public const string SkipRecipe = "no live recipe of this id in the lane's scope";
        public const string SkipNoText = "no text signature or text";
        public const string SkipText = "text signature differs (the recipe changed)";
        public const string SkipUnreadable = "unreadable reading, nothing to take";
        public const string SkipAnswers = "answers not one of free, may or contains for each of the eleven types";
        public const string SkipCurrent = "already read under these signatures";
        public const string SkipTwice = "given twice";

        /// <summary>A reading as the journal keeps it, before and after an import.</summary>
        public sealed record ReadingValue(string PromptSignature, string TextSignature, string StoreVersion, bool Readable, bool Complete, string Verdicts, string? Answer, double Seconds, DateTime ReadAt);

        private static ReadingValue ValueOf(RecipeDietReadingEntity row) =>
            new(row.PromptSignature, row.TextSignature, row.StoreVersion, row.Readable, row.Complete, row.Verdicts, row.Answer, row.Seconds, row.ReadAt);

        /// <summary>Whether a stored reading is still the one a journal entry wrote; the reply's JSON is left out, since the database keeps it in its own form.</summary>
        private static bool Same(ReadingValue a, ReadingValue b) =>
            a with { Answer = null } == b with { Answer = null };

        /// <summary>
        /// A page of the stored readings, by recipe id after <paramref name="afterRecipeId"/>, at most <paramref name="recipes"/>
        /// recipes; with <paramref name="currentOnly"/> only readable readings under the reader's current prompt signature.
        /// </summary>
        public async Task<DietReadingExportPage> ExportAsync(long afterRecipeId, int recipes, bool currentOnly = true, CancellationToken cancellationToken = default)
        {
            var query = _db.RecipeDietReadings.AsNoTracking().Where(r => r.RecipeId > afterRecipeId);
            if (currentOnly) query = query.Where(r => r.Readable && r.PromptSignature == OllamaDietSecondReader.Signature);
            var ids = await query.Select(r => r.RecipeId).Distinct().OrderBy(id => id).Take(Math.Clamp(recipes, 1, MaxImport)).ToListAsync(cancellationToken);
            if (ids.Count == 0) return new DietReadingExportPage(0, Array.Empty<DietReadingExport>(), null);
            var rows = await query.Where(r => ids.Contains(r.RecipeId)).OrderBy(r => r.RecipeId).ThenBy(r => r.Model).ToListAsync(cancellationToken);
            var items = rows.Select(r =>
            {
                var (unlisted, answers) = AnswerOf(r.Answer);
                return new DietReadingExport(r.RecipeId, r.Model, r.PromptSignature, r.TextSignature, r.StoreVersion, r.Readable, r.Complete, unlisted, answers, r.Seconds, r.ReadAt);
            }).ToList();
            var last = ids[^1];
            var more = await query.AnyAsync(r => r.RecipeId > last, cancellationToken);
            return new DietReadingExportPage(ids.Count, items, more ? last : null);
        }

        private static (IReadOnlyList<string> Unlisted, IReadOnlyDictionary<string, string[]> Answers) AnswerOf(string? json)
        {
            var unlisted = new List<string>();
            var answers = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(json)) return (unlisted, answers);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("unlisted", out var u) && u.ValueKind == JsonValueKind.Array)
                unlisted.AddRange(u.EnumerateArray().Select(e => e.GetString() ?? string.Empty));
            if (doc.RootElement.TryGetProperty("answers", out var a) && a.ValueKind == JsonValueKind.Object)
                foreach (var p in a.EnumerateObject())
                    answers[p.Name] = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray() : Array.Empty<string>();
            return (unlisted, answers);
        }

        /// <summary>The reply an imported reading's answers stand for, or null unless each of the reader's eleven types has a known word.</summary>
        private static DietSecondRead? ReadOf(DietReadingImportItem item)
        {
            if (item.Answers == null) return null;
            var answers = new List<DietSecondAnswer>();
            foreach (var (_, type) in OllamaDietSecondReader.Types)
            {
                if (!item.Answers.TryGetValue(type, out var pair) || pair.Length == 0) return null;
                var word = pair[0];
                var reading = Enum.GetValues<DietSecondReading>().Where(r => OllamaDietSecondReader.WordOf(r) == word).Cast<DietSecondReading?>().FirstOrDefault();
                if (reading == null) return null;
                answers.Add(new DietSecondAnswer(type, reading.Value, pair.Length > 1 ? pair[1] ?? string.Empty : string.Empty));
            }
            return new DietSecondRead(item.Unlisted ?? new List<string>(), item.Complete, answers);
        }

        /// <summary>
        /// Takes readings made elsewhere (an export of another copy of the same recipes, or the offline scorer's answers) where
        /// they match exactly: a model of the pair, the reader's current prompt signature, a live recipe of the lane's scope
        /// whose text signature now is the reading's, a readable answer for each type, and no current readable reading of that
        /// recipe and model here. Every other reading is left, counted by its reason. A dry run unless <paramref name="apply"/>;
        /// every reading taken is journaled (DietReadingImport, with the row before and after) under the returned stamp.
        /// </summary>
        public async Task<DietReadingImportReport> ImportAsync(IReadOnlyList<DietReadingImportItem> items, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var stamp = Millis(DateTime.UtcNow);
            var report = new DietReadingImportReport { Applied = apply, Stamp = stamp, Received = items.Count };
            void Skip(DietReadingImportItem item, string reason)
            {
                report.Skipped[reason] = report.Skipped.GetValueOrDefault(reason) + 1;
                if (report.Lines.Count < ShownLines) report.Lines.Add(string.Create(CultureInfo.InvariantCulture, $"skip {item.RecipeId} {item.Model}: {reason}"));
            }

            var ids = items.Select(i => i.RecipeId).Distinct().ToList();
            var live = new HashSet<long>();
            var texts = new Dictionary<long, string>();
            var rows = new Dictionary<long, List<Row>>();
            foreach (var chunk in ids.Chunk(Chunk))
            {
                live.UnionWith(await InScope(_db.Recipes.IgnoreQueryFilters().AsNoTracking()).Where(r => chunk.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken));
                foreach (var (id, text) in await TextsAsync(chunk, cancellationToken)) texts[id] = text.Signature;
                foreach (var (id, list) in await RowsAsync(chunk, cancellationToken)) rows[id] = list;
            }

            var seen = new HashSet<(long, string)>();
            var taken = new List<(DietReadingImportItem Item, DietSecondRead Read, string Text)>();
            foreach (var item in items)
            {
                var model = (item.Model ?? string.Empty).Trim();
                if (!DietSecondGate.Pair.Contains(model)) { Skip(item, SkipModel); continue; }
                if (!seen.Add((item.RecipeId, model))) { Skip(item, SkipTwice); continue; }
                if (item.PromptSignature != OllamaDietSecondReader.Signature) { Skip(item, SkipPrompt); continue; }
                if (!live.Contains(item.RecipeId) || !texts.TryGetValue(item.RecipeId, out var current)) { Skip(item, SkipRecipe); continue; }
                var signature = !string.IsNullOrEmpty(item.TextSignature) ? item.TextSignature
                    : item.Text is { } t ? DietSecondGate.TextSignature(t.Title, t.Lines, t.Method) : null;
                if (signature == null) { Skip(item, SkipNoText); continue; }
                if (signature != current) { Skip(item, SkipText); continue; }
                if (item.Readable == false || item.Answers == null) { Skip(item, SkipUnreadable); continue; }
                if (ReadOf(item) is not { } read) { Skip(item, SkipAnswers); continue; }
                var row = rows.GetValueOrDefault(item.RecipeId)?.FirstOrDefault(r => r.Model == model);
                if (row != null && row.Readable && row.Prompt == OllamaDietSecondReader.Signature && row.Text == current) { Skip(item, SkipCurrent); continue; }
                item.Model = model;
                taken.Add((item, read, current));
                report.Taken++;
                report.TakenByModel[model] = report.TakenByModel.GetValueOrDefault(model) + 1;
                if (report.Lines.Count < ShownLines) report.Lines.Add(string.Create(CultureInfo.InvariantCulture, $"take {item.RecipeId} {model}: {DietSecondGate.Letters(read)}"));
            }

            if (!apply || taken.Count == 0) return report;
            foreach (var chunk in taken.Chunk(Chunk))
            {
                var chunkIds = chunk.Select(c => c.Item.RecipeId).Distinct().ToList();
                var stored = await _db.RecipeDietReadings.Where(r => chunkIds.Contains(r.RecipeId)).ToListAsync(cancellationToken);
                foreach (var (item, read, text) in chunk)
                {
                    var entity = stored.FirstOrDefault(r => r.RecipeId == item.RecipeId && r.Model == item.Model);
                    var old = entity == null ? null : ValueOf(entity);
                    if (entity == null)
                    {
                        entity = new RecipeDietReadingEntity { RecipeId = item.RecipeId, Model = item.Model };
                        _db.RecipeDietReadings.Add(entity);
                    }
                    entity.PromptSignature = OllamaDietSecondReader.Signature;
                    entity.TextSignature = text;
                    entity.StoreVersion = DietSecondGate.Version;
                    entity.Readable = true;
                    entity.Complete = read.Complete;
                    entity.Verdicts = DietSecondGate.Letters(read);
                    entity.Answer = LaneJournal.Write(new
                    {
                        read.Unlisted,
                        read.Complete,
                        Answers = read.Answers.ToDictionary(a => a.Type, a => new[] { OllamaDietSecondReader.WordOf(a.Reading), a.Food }),
                    });
                    entity.Seconds = Math.Max(0, Math.Round(item.Seconds, 2));
                    entity.ReadAt = Millis(item.ReadAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : stamp);
                    _db.AuditLogEntries.Add(ReadingEntry(item.RecipeId, item.Model, ImportChange, old == null ? null : LaneJournal.Write(old), LaneJournal.Write(ValueOf(entity)), stamp, personId));
                }
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }
            return report;
        }

        /// <summary>Takes back every reading the import stamped <paramref name="stamp"/> took, newest first: the reading before it is put back, or the row is removed when there was none; a reading changed since (read again by the lane, or another import) is skipped. A dry run unless <paramref name="apply"/>.</summary>
        public async Task<DietReadingUndoReport> UndoImportAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var report = new DietReadingUndoReport { Applied = apply, Stamp = stamp };
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ReadingEntityType && a.ChangeType == ImportChange && a.Timestamp == stamp)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var chunk in entries.Chunk(Chunk))
            {
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var stored = await _db.RecipeDietReadings.Where(r => ids.Contains(r.RecipeId)).ToListAsync(cancellationToken);
                foreach (var entry in chunk)
                {
                    var model = entry.PropertyName ?? string.Empty;
                    var row = stored.FirstOrDefault(r => r.RecipeId == entry.EntityId && r.Model == model);
                    var expected = LaneJournal.Read<ReadingValue>(entry.NewValue);
                    var label = string.Create(CultureInfo.InvariantCulture, $"{entry.EntityId} {model}");
                    if (row == null || expected == null || !Same(ValueOf(row), expected))
                    {
                        report.Skipped++;
                        if (report.Lines.Count < ShownLines) report.Lines.Add($"skipped {label}: changed since");
                        continue;
                    }
                    var old = LaneJournal.Read<ReadingValue>(entry.OldValue);
                    report.Undone++;
                    if (report.Lines.Count < ShownLines) report.Lines.Add($"undo {label}: {(old == null ? "removed" : "the reading before put back")}");
                    if (!apply) continue;
                    if (old == null) _db.RecipeDietReadings.Remove(row);
                    else
                    {
                        row.PromptSignature = old.PromptSignature;
                        row.TextSignature = old.TextSignature;
                        row.StoreVersion = old.StoreVersion;
                        row.Readable = old.Readable;
                        row.Complete = old.Complete;
                        row.Verdicts = old.Verdicts;
                        row.Answer = old.Answer;
                        row.Seconds = old.Seconds;
                        row.ReadAt = old.ReadAt;
                    }
                    _db.AuditLogEntries.Add(ReadingEntry(entry.EntityId, model, ImportUndoChange, entry.NewValue, entry.OldValue, now, personId));
                }
                if (apply)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                }
            }
            return report;
        }

        private static AuditLogEntryEntity ReadingEntry(long recipeId, string model, string changeType, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = ReadingEntityType,
            EntityId = recipeId,
            ChangeType = changeType,
            PropertyName = model,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static DateTime Millis(DateTime at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
