using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The household's shared shopping list: ad-hoc items, check-off marks, and the
    /// label/barcode/measurement evidence a member supplies for a food whose nutrition is
    /// not verified. Every action is scoped to a household the caller belongs to.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ShoppingController : BaseApiController
    {
        public const int MaxUploadBytes = 10 * 1024 * 1024;
        private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/webp" };

        private readonly IHouseholdShoppingService _shopping;
        private readonly IIngredientVerificationService _verifications;

        public ShoppingController(IHouseholdShoppingService shopping, IIngredientVerificationService verifications)
        {
            _shopping = shopping;
            _verifications = verifications;
        }

        [HttpGet]
        public async Task<ActionResult<ShoppingStateModel>> GetState([FromQuery] long householdId)
        {
            if (!IsHouseholdMember(householdId)) return Forbid();
            return Ok(await _shopping.GetStateAsync(householdId));
        }

        [HttpPost("items")]
        public async Task<ActionResult<HouseholdShoppingItemModel>> AddItem([FromBody] HouseholdShoppingItemCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(model.HouseholdId)) return Forbid();

            try
            {
                var result = await _shopping.AddItemAsync(model.HouseholdId, GetCurrentPersonIdRequired(), model);
                if (result == null) return NotFound(new { message = "Ingredient not found" });
                return result.Created
                    ? CreatedAtAction(nameof(GetItem), new { id = result.Item.Id }, result.Item)
                    : Ok(result.Item);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("items/{id:long}")]
        public async Task<ActionResult<HouseholdShoppingItemModel>> GetItem(long id)
        {
            var item = await _shopping.GetItemAsync(id);
            if (item == null) return NotFound();
            if (!IsHouseholdMember(item.HouseholdId)) return Forbid();
            return Ok(item);
        }

        [HttpPut("items/{id:long}")]
        public async Task<ActionResult<HouseholdShoppingItemModel>> UpdateItem(long id, [FromBody] HouseholdShoppingItemUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _shopping.GetItemAsync(id);
            if (existing == null) return NotFound();
            if (!IsHouseholdMember(existing.HouseholdId)) return Forbid();

            try
            {
                var updated = await _shopping.UpdateItemAsync(id, GetCurrentPersonIdRequired(), model);
                return updated == null ? NotFound() : Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("items/{id:long}")]
        public async Task<IActionResult> DeleteItem(long id)
        {
            var existing = await _shopping.GetItemAsync(id);
            if (existing == null) return NotFound();
            if (!IsHouseholdMember(existing.HouseholdId)) return Forbid();

            await _shopping.DeleteItemAsync(id, GetCurrentPersonIdRequired());
            return NoContent();
        }

        [HttpPost("checks")]
        public async Task<ActionResult<ShoppingCheckResultModel>> SetCheck([FromBody] ShoppingCheckRequestModel request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();

            try
            {
                var result = await _shopping.SetCheckAsync(request.HouseholdId, GetCurrentPersonIdRequired(), request);
                return result == null ? NotFound(new { message = "Item not found" }) : Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("checks/clear")]
        public async Task<IActionResult> ClearChecks([FromBody] HouseholdRequestModel request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();

            await _shopping.ClearChecksAsync(request.HouseholdId, GetCurrentPersonIdRequired());
            return NoContent();
        }

        [HttpGet("needs-verification")]
        public async Task<ActionResult<List<NeedsVerificationModel>>> NeedsVerification([FromQuery] long householdId, [FromQuery] string? ingredientIds)
        {
            if (!IsHouseholdMember(householdId)) return Forbid();

            var ids = (ingredientIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count > Nom.Orch.Services.IngredientVerificationService.MaxIngredientIds)
                return BadRequest(new { message = $"At most {Nom.Orch.Services.IngredientVerificationService.MaxIngredientIds} ingredient ids per request" });

            return Ok(await _verifications.NeedsVerificationAsync(householdId, ids));
        }

        [HttpPost("verification/{ingredientId:long}/skip")]
        public async Task<IActionResult> Skip(long ingredientId, [FromBody] HouseholdRequestModel request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();

            var ok = await _verifications.SkipAsync(ingredientId, request.HouseholdId, GetCurrentPersonIdRequired());
            return ok ? NoContent() : NotFound();
        }

        [HttpPost("verification/{ingredientId:long}/not-food")]
        public async Task<ActionResult<NotFoodResultModel>> MarkNotFood(long ingredientId, [FromBody] HouseholdRequestModel request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();

            var result = await _verifications.MarkNotFoodAsync(ingredientId, request.HouseholdId, GetCurrentPersonIdRequired());
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("verification/{ingredientId:long}/label")]
        [RequestSizeLimit(12_000_000)]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<LabelScanResultModel>> ScanLabel(long ingredientId, [FromForm] long householdId, IFormFile image)
        {
            if (!IsHouseholdMember(householdId)) return Forbid();
            var problem = ValidateImage(image);
            if (problem != null) return BadRequest(new { message = problem });

            var bytes = await ReadAsync(image);
            try
            {
                var result = await _verifications.ScanLabelAsync(ingredientId, householdId, GetCurrentPersonIdRequired(), bytes);
                return result == null ? NotFound() : Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
            }
        }

        [HttpPut("verification/{verificationId:long}")]
        public async Task<ActionResult<IngredientVerificationModel>> Confirm(long verificationId, [FromBody] IngredientVerificationConfirmModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _verifications.GetAsync(verificationId);
            if (existing == null) return NotFound();
            if (existing.HouseholdId is not { } householdId || !IsHouseholdMember(householdId)) return Forbid();

            try
            {
                var updated = await _verifications.ConfirmAsync(verificationId, GetCurrentPersonIdRequired(), model);
                return updated == null ? NotFound() : Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPost("verification/{ingredientId:long}/barcode")]
        public async Task<ActionResult<BarcodeResultModel>> RecordBarcode(long ingredientId, [FromBody] BarcodeRequestModel request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();

            try
            {
                var result = await _verifications.RecordBarcodeAsync(ingredientId, request.HouseholdId, GetCurrentPersonIdRequired(), request.Upc);
                return result == null ? NotFound() : Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("verification/{ingredientId:long}/barcode/image")]
        [RequestSizeLimit(12_000_000)]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<BarcodeResultModel>> RecordBarcodeFromImage(long ingredientId, [FromForm] long householdId, IFormFile image)
        {
            if (!IsHouseholdMember(householdId)) return Forbid();
            var problem = ValidateImage(image);
            if (problem != null) return BadRequest(new { message = problem });

            var bytes = await ReadAsync(image);
            try
            {
                var result = await _verifications.RecordBarcodeFromImageAsync(ingredientId, householdId, GetCurrentPersonIdRequired(), bytes);
                return result == null ? NotFound() : Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
            }
        }

        [HttpPost("verification/{ingredientId:long}/measurements")]
        public async Task<ActionResult<IngredientVerificationModel>> RecordMeasurements(long ingredientId, [FromBody] IngredientMeasurementsModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!IsHouseholdMember(model.HouseholdId)) return Forbid();

            try
            {
                var result = await _verifications.RecordMeasurementsAsync(ingredientId, model.HouseholdId, GetCurrentPersonIdRequired(), model);
                return result == null ? NotFound() : Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("verification")]
        public async Task<ActionResult<List<IngredientVerificationModel>>> ListVerifications([FromQuery] long householdId, [FromQuery] long ingredientId)
        {
            if (!IsHouseholdMember(householdId)) return Forbid();
            return Ok(await _verifications.ListAsync(householdId, ingredientId));
        }

        [HttpGet("verification/{verificationId:long}/image")]
        public async Task<IActionResult> GetVerificationImage(long verificationId)
        {
            var existing = await _verifications.GetAsync(verificationId);
            if (existing == null) return NotFound();
            if (existing.HouseholdId is not { } householdId || !IsHouseholdMember(householdId)) return Forbid();

            var image = await _verifications.GetImageAsync(verificationId);
            if (image == null) return NotFound();
            return File(image.Value.Data, image.Value.ContentType);
        }

        private static string? ValidateImage(IFormFile? image)
        {
            if (image == null || image.Length == 0) return "No image provided";
            if (image.Length > MaxUploadBytes) return "Image is larger than 10 MB";
            if (!AllowedImageTypes.Contains((image.ContentType ?? string.Empty).ToLowerInvariant()))
                return "Only JPEG, PNG and WebP images are allowed";
            return null;
        }

        private static async Task<byte[]> ReadAsync(IFormFile image)
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms);
            return ms.ToArray();
        }
    }
}
