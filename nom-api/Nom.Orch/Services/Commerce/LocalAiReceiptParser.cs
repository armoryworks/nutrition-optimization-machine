using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Two-stage receipt parser (D-061, all open-source): Tesseract OCR turns
    /// the image into text, then a self-hosted Ollama model structures that
    /// text into store + line items. Falls back to manual entry when Ollama
    /// isn't configured or a stage fails — never fabricates prices.
    /// </summary>
    public class LocalAiReceiptParser : IReceiptParser
    {
        private readonly ITesseractOcrService _ocr;
        private readonly IOllamaClient _ollama;
        private readonly ILogger<LocalAiReceiptParser> _logger;

        public LocalAiReceiptParser(ITesseractOcrService ocr, IOllamaClient ollama, ILogger<LocalAiReceiptParser> logger)
        {
            _ocr = ocr;
            _ollama = ollama;
            _logger = logger;
        }

        public async Task<ReceiptParseResultModel> ParseAsync(byte[] imageData, string contentType)
        {
            if (!_ollama.IsConfigured)
                return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };

            string text;
            try
            {
                text = await _ocr.ExtractRawTextAsync(imageData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Receipt OCR failed");
                return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };
            }

            if (string.IsNullOrWhiteSpace(text))
                return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };

            var prompt =
                "You are parsing a grocery store receipt. Extract ONLY purchased line items " +
                "(never subtotal, tax, total, change, or payment lines). Return strict JSON: " +
                "{\"store\": string|null, \"postalCode\": string|null, \"items\": [{\"name\": string, \"price\": number}]}. " +
                "Prices are dollars. If unsure of a value use null.\n\nReceipt text:\n" + text;

            try
            {
                var json = await _ollama.GenerateAsync(prompt, jsonMode: true);
                var parsed = JsonSerializer.Deserialize<OllamaReceipt>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed?.Items == null || parsed.Items.Count == 0)
                    return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };

                var lines = parsed.Items
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Price is > 0)
                    .Select(i => new ReceiptLineModel { ItemText = i.Name!.Trim(), Price = i.Price!.Value })
                    .ToList();

                if (lines.Count == 0)
                    return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };

                return new ReceiptParseResultModel
                {
                    StoreNameRaw = parsed.Store,
                    PostalCode = parsed.PostalCode,
                    Lines = lines,
                    // Local 3B model: confident enough to persist, but flag for review workflows.
                    Confidence = 0.7m,
                    RequiresManualEntry = false,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Receipt structuring failed");
                return new ReceiptParseResultModel { RequiresManualEntry = true, Confidence = 0m };
            }
        }

        private sealed class OllamaReceipt
        {
            public string? Store { get; set; }
            public string? PostalCode { get; set; }
            public List<OllamaItem>? Items { get; set; }
        }

        private sealed class OllamaItem
        {
            public string? Name { get; set; }
            public decimal? Price { get; set; }
        }
    }
}
