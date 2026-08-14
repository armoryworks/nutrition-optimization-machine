using System;
using System.Text.Json;
using Nom.Data.Reference;

namespace Nom.Data.Nutrition
{
    /// <summary>A classification result parsed from an AI response.</summary>
    public sealed record FoodEnrichment(long? FoodGroupId, bool? IsWholeFood);

    /// <summary>
    /// Parses a local-model classification reply into a validated <see cref="FoodEnrichment"/>.
    /// Expects JSON like {"food_group":"Vegetables","whole_food":true} but tolerates surrounding
    /// prose and key-name variants. A group name that doesn't resolve to a known food group is
    /// dropped (null) rather than trusted — guards against hallucinated groups.
    /// </summary>
    public static class FoodEnrichmentParser
    {
        public static FoodEnrichment Parse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new FoodEnrichment(null, null);

            long? groupId = null;
            bool? wholeFood = null;

            // Prefer a JSON object embedded anywhere in the response.
            var json = ExtractJsonObject(response);
            if (json != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var key = prop.Name.ToLowerInvariant();
                        if (key.Contains("group") && prop.Value.ValueKind == JsonValueKind.String)
                            groupId ??= FoodGroupCatalog.TryResolve(prop.Value.GetString());
                        else if (key.Contains("whole"))
                            wholeFood ??= ReadBool(prop.Value);
                    }
                }
                catch (JsonException) { /* fall through to text scan */ }
            }

            // Text fallbacks.
            if (groupId == null)
                groupId = ResolveByDisplayNameSubstring(response);
            if (wholeFood == null)
                wholeFood = ScanWholeFood(response);

            return new FoodEnrichment(groupId, wholeFood);
        }

        private static string? ExtractJsonObject(string s)
        {
            var start = s.IndexOf('{');
            var end = s.LastIndexOf('}');
            return (start >= 0 && end > start) ? s.Substring(start, end - start + 1) : null;
        }

        private static bool? ReadBool(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ParseBoolText(e.GetString()),
            _ => null,
        };

        private static bool? ParseBoolText(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().ToLowerInvariant();
            if (s is "true" or "yes" or "y" or "1") return true;
            if (s is "false" or "no" or "n" or "0") return false;
            return null;
        }

        private static long? ResolveByDisplayNameSubstring(string text)
        {
            var lower = text.ToLowerInvariant();
            foreach (var (id, name) in FoodGroupCatalog.DisplayNames)
                if (lower.Contains(name.ToLowerInvariant()))
                    return id;
            return null;
        }

        private static bool? ScanWholeFood(string text)
        {
            var lower = text.ToLowerInvariant();
            var idx = lower.IndexOf("whole");
            if (idx < 0) return null;
            // Look at a small window after "whole..." for an affirmative/negative.
            var window = lower.Substring(idx, Math.Min(40, lower.Length - idx));
            if (window.Contains("true") || window.Contains("yes")) return true;
            if (window.Contains("false") || window.Contains(" no")) return false;
            return null;
        }
    }
}
