using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The second diet reader in the stored gate (plan item ck21): while the platform setting <see cref="FeatureKey"/> is on,
    /// a recipe the rules find clear for a severe set of one of the reader's eleven types is shown only when both models of
    /// <see cref="Pair"/> have a stored reading of it (<see cref="RecipeDietReadingEntity"/>) under the reader's current
    /// prompt signature and the recipe's current text, and both call it clearly free of that type; a recipe not yet read so
    /// is held as <see cref="DietFlagReason.SecondUnread"/>, one a reading does not call free as
    /// <see cref="DietFlagReason.SecondReader"/>. The readers never clear what the rules hold. With the setting off the
    /// build reads no reading and decides as before.
    /// </summary>
    public static class DietSecondGate
    {
        public const string FeatureKey = "diet-second-reader-gate";

        /// <summary>The version of this rule and of the readings table's format.</summary>
        public const string Version = "diet-second-gate/1";

        /// <summary>The two readers, of two model families, whose readings the gate and the auto-approval read: the pair measured in ck20 (mistral-small:24b and phi4).</summary>
        public static readonly IReadOnlyList<string> Pair = new[] { "mistral-small:24b", "phi4:latest" };

        /// <summary>What decides the gate's use of the readings: this rule's version, the reader's prompt signature and the pair.</summary>
        public static string Signature => Version + ":" + OllamaDietSecondReader.Signature + ":" + string.Join('+', Pair);

        /// <summary>The entry of this rule in the store's version (<see cref="DietGateStore.Version"/>): a new rule, prompt or pair is a new build.</summary>
        public static string StoreEntry => Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Signature)))[..8].ToLowerInvariant();

        /// <summary>The letter a reading stores for each answer.</summary>
        public static char LetterOf(DietSecondReading reading) => reading switch
        {
            DietSecondReading.Contains => 'c',
            DietSecondReading.Free => 'f',
            _ => 'm',
        };

        /// <summary>The index of a restriction type in the reader's type order, or -1 for a type the reader does not read.</summary>
        public static int IndexOf(string? typeName)
        {
            for (var i = 0; i < OllamaDietSecondReader.Types.Count; i++)
                if (string.Equals(OllamaDietSecondReader.Types[i].Type, typeName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The letters of a reply, one per type in the reader's order.</summary>
        public static string Letters(DietSecondRead read) =>
            new(OllamaDietSecondReader.Types.Select(t => read.Answers.FirstOrDefault(a => a.Type == t.Type) is { } a ? LetterOf(a.Reading) : 'm').ToArray());

        /// <summary>
        /// The text the reader is shown, as the build job and the lane both read it: the title, every live line in order (its
        /// own text, or its food's name when it has none) and the method steps. The signature is taken over the text with all
        /// white space folded, so a method read as one string and the same method read step by step sign alike.
        /// </summary>
        public static string TextSignature(string? title, IEnumerable<string?> lines, IEnumerable<string?> steps)
        {
            var text = Fold(title) + "\n" + string.Join("\n", lines.Select(Fold).Where(l => l.Length > 0)) + "\n" + Fold(string.Join(' ', steps.Where(s => s != null)));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }

        /// <summary>A line's text as the reader is shown it: its own text, or its food's name when it has none.</summary>
        public static string LineText(string? raw, string? foodName) => string.IsNullOrWhiteSpace(raw) ? foodName ?? string.Empty : raw;

        private static string Fold(string? text) => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>A stored reading as the gate reads it.</summary>
        public sealed record Stored(string Model, string Prompt, string Text, bool Readable, string Verdicts);

        public static Task<bool> OnAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            db.PlatformFeatures.AsNoTracking().AnyAsync(f => f.Key == FeatureKey && f.IsEnabled && !f.IsDeleted, cancellationToken);

        /// <summary>The stored readings of the pair, for the given recipes or for all.</summary>
        public static async Task<Dictionary<long, List<Stored>>> LoadAsync(ApplicationDbContext db, IReadOnlyCollection<long>? recipeIds, CancellationToken cancellationToken = default)
        {
            var pair = Pair.ToList();
            var result = new Dictionary<long, List<Stored>>();
            async Task Add(IQueryable<RecipeDietReadingEntity> rows)
            {
                foreach (var r in await rows.Where(r => pair.Contains(r.Model))
                             .Select(r => new { r.RecipeId, r.Model, r.PromptSignature, r.TextSignature, r.Readable, r.Verdicts })
                             .ToListAsync(cancellationToken))
                {
                    if (!result.TryGetValue(r.RecipeId, out var list)) result[r.RecipeId] = list = new List<Stored>();
                    list.Add(new Stored(r.Model, r.PromptSignature, r.TextSignature, r.Readable, r.Verdicts));
                }
            }
            if (recipeIds == null) await Add(db.RecipeDietReadings.AsNoTracking());
            else
                foreach (var batch in recipeIds.Distinct().Chunk(5000))
                    await Add(db.RecipeDietReadings.AsNoTracking().Where(r => batch.Contains(r.RecipeId)));
            return result;
        }

        /// <summary>Whether a stored reading is current for a recipe whose text has the given signature: the reader's prompt signature, that text, and a readable reply with a letter per type.</summary>
        public static bool Current(Stored reading, string textSignature) =>
            reading.Readable && reading.Prompt == OllamaDietSecondReader.Signature && reading.Text == textSignature && reading.Verdicts.Length == OllamaDietSecondReader.Types.Count;

        /// <summary>
        /// The pair's reading of a recipe as the gate decides with it: empty when a model of the pair has no current reading
        /// (not yet read), otherwise one letter per type, <c>f</c> where every model of the pair calls the recipe clearly free
        /// of the type and <c>h</c> where one does not.
        /// </summary>
        public static string Summary(IReadOnlyList<Stored>? readings, string textSignature)
        {
            if (readings == null) return string.Empty;
            var current = new List<Stored>();
            foreach (var model in Pair)
            {
                var reading = readings.FirstOrDefault(r => r.Model == model && Current(r, textSignature));
                if (reading == null) return string.Empty;
                current.Add(reading);
            }
            var letters = new char[OllamaDietSecondReader.Types.Count];
            for (var i = 0; i < letters.Length; i++) letters[i] = current.All(r => r.Verdicts[i] == 'f') ? 'f' : 'h';
            return new string(letters);
        }
    }
}
