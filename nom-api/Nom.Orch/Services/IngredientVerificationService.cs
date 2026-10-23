using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;

namespace Nom.Orch.Services
{
    public class IngredientVerificationService : IIngredientVerificationService
    {
        public const int AskAgainAfterDays = 30;
        public const int MaxLabelScansPerHour = 20;
        public const int MaxIngredientIds = 200;
        public const int LabelImageMaxWidth = 2000;
        public const int LabelImageQuality = 90;
        public const int OcrTextCap = 16000;
        public const int OcrDisplayCap = 4000;
        public const string CaloriesNutrientName = "Calories";
        private static readonly TimeSpan UpsertWindow = TimeSpan.FromHours(24);
        private static readonly string[] OpenStatuses =
        {
            IngredientVerificationStatus.Pending, IngredientVerificationStatus.Corroborated, IngredientVerificationStatus.Published,
        };

        private readonly ApplicationDbContext _context;
        private readonly ITesseractOcrService _ocr;
        private readonly IBarcodeDecodeService _barcodes;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IVerificationLookupLane _lane;
        private readonly ILogger<IngredientVerificationService> _logger;

        public IngredientVerificationService(
            ApplicationDbContext context,
            ITesseractOcrService ocr,
            IBarcodeDecodeService barcodes,
            IMediaStorageService mediaStorage,
            IVerificationLookupLane lane,
            ILogger<IngredientVerificationService> logger)
        {
            _context = context;
            _ocr = ocr;
            _barcodes = barcodes;
            _mediaStorage = mediaStorage;
            _lane = lane;
            _logger = logger;
        }

        public static string UserLabelSource(long personId) => $"label:user:{personId}";

        public async Task<List<NeedsVerificationModel>> NeedsVerificationAsync(long householdId, IReadOnlyCollection<long> ingredientIds)
        {
            var ids = ingredientIds.Distinct().Take(MaxIngredientIds).ToList();
            if (ids.Count == 0) return new List<NeedsVerificationModel>();

            var caloriesId = await CaloriesNutrientIdAsync();
            var cutoff = DateTime.UtcNow.AddDays(-AskAgainAfterDays);
            var curated = (long)CurationStatusEnum.Curated;

            var rows = await _context.Ingredients.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    HasUpc = i.GtinUpc != null,
                    Verified = (i.FdcId != null || i.CurationStatusId == curated)
                               && i.IngredientNutrients.Any(n => n.NutrientId == caloriesId),
                    Asked = _context.IngredientVerifications.Any(v =>
                        v.IngredientId == i.Id && v.HouseholdId == householdId
                        && (v.LastAskedDate >= cutoff || OpenStatuses.Contains(v.Status))),
                })
                .ToListAsync();

            return rows.Select(r => new NeedsVerificationModel
            {
                IngredientId = r.Id,
                Name = r.Name,
                HasVerifiedNutrition = r.Verified,
                HasUpc = r.HasUpc,
                AskedRecently = r.Asked,
                ShouldAsk = !r.Verified && !r.Asked,
            }).ToList();
        }

        public async Task<bool> SkipAsync(long ingredientId, long householdId, long personId)
        {
            if (!await IngredientExistsAsync(ingredientId)) return false;
            var now = DateTime.UtcNow;
            _context.IngredientVerifications.Add(new IngredientVerificationEntity
            {
                IngredientId = ingredientId,
                HouseholdId = householdId,
                Source = UserLabelSource(personId),
                Status = IngredientVerificationStatus.Skipped,
                Kind = IngredientVerificationKind.Skip,
                LastAskedDate = now,
                CreatedDate = now,
                CreatedByPersonId = personId,
            });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<LabelScanResultModel?> ScanLabelAsync(long ingredientId, long householdId, long personId, byte[] image)
        {
            if (!await IngredientExistsAsync(ingredientId)) return null;

            var hourAgo = DateTime.UtcNow.AddHours(-1);
            var recent = await _context.IngredientVerifications.CountAsync(v =>
                v.Kind == IngredientVerificationKind.Label && v.CreatedByPersonId == personId && v.CreatedDate >= hourAgo);
            if (recent >= MaxLabelScansPerHour)
                throw new InvalidOperationException("Too many label scans this hour; try again later.");

            var jpeg = ImageTranscoder.ToJpeg(image, LabelImageMaxWidth, LabelImageQuality)
                ?? throw new ArgumentException("The file is not an image that can be read.");

            string ocrText;
            try
            {
                ocrText = await _ocr.ExtractRawTextAsync(image) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR failed for a label photo of ingredient {IngredientId}", ingredientId);
                ocrText = string.Empty;
            }

            var parsed = NutritionFactsParser.Parse(ocrText);
            var now = DateTime.UtcNow;
            var row = new IngredientVerificationEntity
            {
                IngredientId = ingredientId,
                HouseholdId = householdId,
                Source = UserLabelSource(personId),
                Status = IngredientVerificationStatus.Pending,
                Kind = IngredientVerificationKind.Label,
                LabelImageContentType = "image/jpeg",
                OcrText = ocrText.Length > OcrTextCap ? ocrText.Substring(0, OcrTextCap) : ocrText,
                NutrientsJson = VerificationNutrients.ToJson(parsed),
                ServingText = Clip(parsed.ServingText, 255),
                ServingGrams = parsed.ServingGrams,
                ServingMl = parsed.ServingMl,
                ServingsPerContainer = parsed.ServingsPerContainer,
                LastAskedDate = now,
                CreatedDate = now,
                CreatedByPersonId = personId,
            };
            if (_mediaStorage.IsConfigured)
                row.LabelImagePath = await _mediaStorage.SaveAsync($"ingredient-labels/{ingredientId}/{Guid.NewGuid():N}.jpg", jpeg);
            else
                row.LabelImageData = jpeg;

            _context.IngredientVerifications.Add(row);
            await _context.SaveChangesAsync();
            await _lane.QueueAsync(row.Id);

            return new LabelScanResultModel
            {
                VerificationId = row.Id,
                Parsed = parsed,
                OcrText = ocrText.Length > OcrDisplayCap ? ocrText.Substring(0, OcrDisplayCap) : ocrText,
                ImageStored = true,
            };
        }

        public async Task<IngredientVerificationModel?> GetAsync(long verificationId)
        {
            var row = await _context.IngredientVerifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
            return row == null ? null : ToModel(row);
        }

        public async Task<IngredientVerificationModel?> ConfirmAsync(long verificationId, long personId, IngredientVerificationConfirmModel model)
        {
            var row = await _context.IngredientVerifications.FirstOrDefaultAsync(v => v.Id == verificationId);
            if (row == null) return null;
            if (row.Kind != IngredientVerificationKind.Label)
                throw new InvalidOperationException("Only a label scan can be confirmed.");
            if (row.Status != IngredientVerificationStatus.Pending && row.Status != IngredientVerificationStatus.Held)
                throw new InvalidOperationException("This verification is no longer open.");

            var result = new NutritionFactsParseResult
            {
                Basis = NutritionBasis.Serving,
                ServingText = Clip(model.ServingText, 255),
                ServingGrams = model.ServingGrams,
                ServingMl = model.ServingMl,
                ServingsPerContainer = model.ServingsPerContainer,
                Calories = model.Calories,
            };
            foreach (var n in model.Nutrients)
            {
                var unit = (n.Unit ?? "g").Trim().ToLowerInvariant();
                if (unit is not ("g" or "mg" or "mcg"))
                    throw new ArgumentException($"Unit '{n.Unit}' is not g, mg or mcg.");
                if (n.Amount is < 0)
                    throw new ArgumentException($"{n.Key} cannot be negative.");
                if (string.IsNullOrWhiteSpace(n.Key) || result.Nutrients.Any(x => x.Key == n.Key))
                    continue;
                result.Nutrients.Add(new NutrientReading { Key = n.Key.Trim(), Amount = n.Amount, Unit = unit, PercentDailyValue = n.PercentDailyValue });
            }
            NutritionFactsParser.Assess(result);

            var now = DateTime.UtcNow;
            row.NutrientsJson = VerificationNutrients.ToJson(result);
            row.ServingText = result.ServingText;
            row.ServingGrams = result.ServingGrams;
            row.ServingMl = result.ServingMl;
            row.ServingsPerContainer = result.ServingsPerContainer;
            row.Confirmed = true;
            row.ConfirmedByPersonId = personId;
            row.ConfirmedAt = now;
            row.LastModifiedDate = now;
            row.LastModifiedByPersonId = personId;
            await _context.SaveChangesAsync();
            return ToModel(row);
        }

        public async Task<BarcodeResultModel?> RecordBarcodeAsync(long ingredientId, long householdId, long personId, string upc)
        {
            if (!Gtin.TryNormalize(upc, out var digits))
                throw new ArgumentException("That is not a valid GTIN-8/12/13/14 barcode.");
            return await StoreBarcodeAsync(ingredientId, householdId, personId, digits, BarcodeSource.Client, null);
        }

        public async Task<BarcodeResultModel?> RecordBarcodeFromImageAsync(long ingredientId, long householdId, long personId, byte[] image)
        {
            if (!await IngredientExistsAsync(ingredientId)) return null;
            var decoded = _barcodes.Decode(image);
            if (decoded == null || !Gtin.TryNormalize(decoded.Text, out var digits))
                return new BarcodeResultModel { Decoded = false, Source = BarcodeSource.Server, Format = decoded?.Format };
            return await StoreBarcodeAsync(ingredientId, householdId, personId, digits, BarcodeSource.Server, decoded.Format);
        }

        private async Task<BarcodeResultModel?> StoreBarcodeAsync(long ingredientId, long householdId, long personId, string digits, string source, string? format)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId);
            if (ingredient == null) return null;

            var now = DateTime.UtcNow;
            var row = await LatestOpenRowAsync(ingredientId, householdId, now, IngredientVerificationKind.Label)
                      ?? NewRow(ingredientId, householdId, personId, IngredientVerificationKind.Barcode, now);
            row.Upc = digits;
            row.LastModifiedDate = now;
            row.LastModifiedByPersonId = personId;

            var applied = false;
            if (string.IsNullOrEmpty(ingredient.GtinUpc))
            {
                ingredient.GtinUpc = digits;
                ingredient.LastModifiedDate = now;
                ingredient.LastModifiedByPersonId = personId;
                applied = true;
            }
            await _context.SaveChangesAsync();
            await _lane.QueueAsync(row.Id);

            return new BarcodeResultModel
            {
                VerificationId = row.Id,
                Upc = digits,
                Decoded = true,
                Source = source,
                Format = format,
                AppliedToIngredient = applied,
            };
        }

        public async Task<IngredientVerificationModel?> RecordMeasurementsAsync(long ingredientId, long householdId, long personId, IngredientMeasurementsModel model)
        {
            if (!await IngredientExistsAsync(ingredientId)) return null;

            decimal? packageGrams = null, packageMl = null;
            if (model.PackageAmount is { } amount)
            {
                switch ((model.PackageUnit ?? string.Empty).ToLowerInvariant())
                {
                    case "g": packageGrams = amount; break;
                    case "kg": packageGrams = amount * 1000m; break;
                    case "oz": packageGrams = amount * 28.3495m; break;
                    case "lb": packageGrams = amount * 453.592m; break;
                    case "ml": packageMl = amount; break;
                    case "l": packageMl = amount * 1000m; break;
                    case "floz": packageMl = amount * 29.5735m; break;
                    default: throw new ArgumentException("A package amount needs a unit of g, kg, oz, lb, ml, l or floz.");
                }
            }
            if (packageGrams == null && packageMl == null && model.PiecesPerPackage == null && model.CupGrams == null)
                throw new ArgumentException("Nothing was measured.");

            var now = DateTime.UtcNow;
            var row = await LatestOpenRowAsync(ingredientId, householdId, now, null)
                      ?? NewRow(ingredientId, householdId, personId, IngredientVerificationKind.Measurements, now);
            if (packageGrams != null) row.PackageGrams = Math.Round(packageGrams.Value, 2);
            if (packageMl != null) row.PackageMl = Math.Round(packageMl.Value, 2);
            if (model.PiecesPerPackage != null) row.PiecesPerPackage = Math.Round(model.PiecesPerPackage.Value, 2);
            if (model.CupGrams != null) row.CupGrams = Math.Round(model.CupGrams.Value, 2);
            row.LastModifiedDate = now;
            row.LastModifiedByPersonId = personId;
            await _context.SaveChangesAsync();
            await _lane.QueueAsync(row.Id);
            return ToModel(row);
        }

        public async Task<List<IngredientVerificationModel>> ListAsync(long householdId, long ingredientId)
        {
            var rows = await _context.IngredientVerifications.AsNoTracking()
                .Where(v => v.HouseholdId == householdId && v.IngredientId == ingredientId)
                .OrderByDescending(v => v.CreatedDate)
                .ThenByDescending(v => v.Id)
                .ToListAsync();
            return rows.Select(ToModel).ToList();
        }

        public async Task<(byte[] Data, string ContentType)?> GetImageAsync(long verificationId)
        {
            var row = await _context.IngredientVerifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
            if (row == null) return null;
            var contentType = row.LabelImageContentType ?? "image/jpeg";
            if (!string.IsNullOrEmpty(row.LabelImagePath))
            {
                var data = await _mediaStorage.ReadAsync(row.LabelImagePath);
                return data == null ? null : (data, contentType);
            }
            return row.LabelImageData is { Length: > 0 } ? (row.LabelImageData, contentType) : null;
        }

        private async Task<long> CaloriesNutrientIdAsync()
        {
            var id = await _context.Nutrients.Where(n => n.Name == CaloriesNutrientName).Select(n => n.Id).FirstOrDefaultAsync();
            if (id == 0)
                throw new InvalidOperationException($"The nutrient catalog has no '{CaloriesNutrientName}' row; the database seed is missing.");
            return id;
        }

        private Task<bool> IngredientExistsAsync(long ingredientId) =>
            _context.Ingredients.AnyAsync(i => i.Id == ingredientId);

        private Task<IngredientVerificationEntity?> LatestOpenRowAsync(long ingredientId, long householdId, DateTime now, string? kind)
        {
            var since = now - UpsertWindow;
            return _context.IngredientVerifications
                .Where(v => v.IngredientId == ingredientId && v.HouseholdId == householdId
                            && v.Status == IngredientVerificationStatus.Pending && v.CreatedDate >= since
                            && (kind == null || v.Kind == kind))
                .OrderByDescending(v => v.CreatedDate)
                .ThenByDescending(v => v.Id)
                .FirstOrDefaultAsync();
        }

        private IngredientVerificationEntity NewRow(long ingredientId, long householdId, long personId, string kind, DateTime now)
        {
            var row = new IngredientVerificationEntity
            {
                IngredientId = ingredientId,
                HouseholdId = householdId,
                Source = UserLabelSource(personId),
                Status = IngredientVerificationStatus.Pending,
                Kind = kind,
                LastAskedDate = now,
                CreatedDate = now,
                CreatedByPersonId = personId,
            };
            _context.IngredientVerifications.Add(row);
            return row;
        }

        private static IngredientVerificationModel ToModel(IngredientVerificationEntity row)
        {
            var nutrients = VerificationNutrients.FromJson(row.NutrientsJson, row.ServingGrams, row.ServingText);
            if (nutrients != null) NutritionFactsParser.Assess(nutrients);
            return new IngredientVerificationModel
            {
                Id = row.Id,
                IngredientId = row.IngredientId,
                HouseholdId = row.HouseholdId,
                Kind = row.Kind,
                Status = row.Status,
                Source = row.Source,
                Upc = row.Upc,
                ServingText = row.ServingText,
                ServingGrams = row.ServingGrams,
                ServingMl = row.ServingMl,
                ServingsPerContainer = row.ServingsPerContainer,
                PackageGrams = row.PackageGrams,
                PackageMl = row.PackageMl,
                PiecesPerPackage = row.PiecesPerPackage,
                CupGrams = row.CupGrams,
                Nutrients = nutrients,
                Confirmed = row.Confirmed,
                Warnings = nutrients?.Warnings ?? new List<string>(),
                LastAskedDate = row.LastAskedDate,
                CreatedDate = row.CreatedDate,
            };
        }

        private static string? Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }
    }
}
