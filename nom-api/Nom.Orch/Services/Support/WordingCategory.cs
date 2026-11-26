using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The foods a category verdict of the verified wording table names (<c>IngredientWordingVerdict.Category</c>): a wording
    /// that is a food of a USDA category no single catalog food is ("isinglass" is fish, "calf's head" is a veal variety meat,
    /// "citron" is a fruit), given as the category and, when it narrows it, the leading part of the USDA description the
    /// wording's foods share ("Fish", "Veal, variety meats and by-products"). The group is the live curated foods of that
    /// category whose description, or name when they have none, starts with the head at a comma or its end. Its lines stay on
    /// their uncurated row (nothing is moved, no nutrition is claimed); the diet gate decides them by what the group's curated
    /// foods agree on (<see cref="DietGateStoreBuilder"/>).
    /// </summary>
    public static class WordingCategory
    {
        /// <summary>The longest head a row may give.</summary>
        public const int HeadLength = 255;

        /// <summary>A head as the table keeps it: trimmed, inner spaces folded, null when empty.</summary>
        public static string? Head(string? head)
        {
            if (string.IsNullOrWhiteSpace(head)) return null;
            var folded = Regex.Replace(head.Trim(), @"\s+", " ");
            return folded.Length == 0 ? null : folded;
        }

        /// <summary>Whether a curated food of <paramref name="categoryId"/> with this description (or name) is of the group.</summary>
        public static bool Holds(int groupCategory, string? head, int? categoryId, string? description, string? name)
        {
            if (categoryId != groupCategory) return false;
            if (head == null) return true;
            var text = (string.IsNullOrWhiteSpace(description) ? name : description)?.Trim() ?? string.Empty;
            if (!text.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return false;
            return text.Length == head.Length || text[head.Length] == ',';
        }

        /// <summary>The group as the journal and the export write it: the category, with the head after a bar when it has one.</summary>
        public static string Text(int categoryId, string? head) =>
            head == null ? categoryId.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{categoryId}|{head}");

        /// <summary>Reads <see cref="Text"/> back; null when it is no group.</summary>
        public static (int CategoryId, string? Head)? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var bar = text.IndexOf('|');
            var id = bar < 0 ? text : text[..bar];
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var categoryId)) return null;
            return (categoryId, bar < 0 ? null : Head(text[(bar + 1)..]));
        }
    }
}
