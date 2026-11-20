using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nom.Data.Recipe;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// How a parsed scraper record is stored as a recipe's source document: one JSON form, so the
    /// staged import and the source attach store the same text, and the same checksum, for the
    /// same record. The JSON-LD node a record carries is stored as its own document and is left
    /// out of the record's text. Text only: no database and no model.
    /// </summary>
    public static class SourceRecord
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Format(ScraperRecipe record) =>
            string.IsNullOrWhiteSpace(record.SourceUrl) ? ScrapedDocumentFormat.StagedCookbook : ScrapedDocumentFormat.ScraperRecipe;

        public static string Content(ScraperRecipe record)
        {
            var node = JsonSerializer.SerializeToNode(record, Options)!.AsObject();
            node.Remove("rawJsonLd");
            return node.ToJsonString(Options);
        }

        public static string Sha256(string content) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
