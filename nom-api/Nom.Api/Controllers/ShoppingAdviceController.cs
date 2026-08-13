using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Consumer pricing surface: "Where should I shop?" recommendations and
    /// receipt uploads that crowdsource price data.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ShoppingAdviceController : BaseApiController
    {
        private readonly IPricingOrchestrationService _pricing;

        public ShoppingAdviceController(IPricingOrchestrationService pricing)
        {
            _pricing = pricing;
        }

        /// <summary>Ranks nearby stores for the household's current basket.</summary>
        [HttpPost("where-to-shop")]
        [ProducesResponseType(typeof(ShopRecommendationModel), StatusCodes.Status200OK)]
        public async Task<ActionResult<ShopRecommendationModel>> WhereToShop([FromBody] ShopQueryModel query)
        {
            if (!IsHouseholdMember(query.HouseholdId)) return Forbid();
            if (string.IsNullOrWhiteSpace(query.PostalCode))
                return BadRequest(new { message = "A postal code is required to find nearby stores." });

            return Ok(await _pricing.WhereToShopAsync(query));
        }

        /// <summary>
        /// Uploads a receipt image to contribute crowdsourced prices. Returns the
        /// parsed lines; when no parser is wired the result asks for manual entry.
        /// </summary>
        [HttpPost("receipt")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        [ProducesResponseType(typeof(ReceiptParseResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ReceiptParseResultModel>> SubmitReceipt(
            IFormFile file, [FromQuery] string? postalCode)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "A receipt image is required." });
            if (!file.ContentType.StartsWith("image/"))
                return BadRequest(new { message = "The uploaded file must be an image." });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var result = await _pricing.SubmitReceiptAsync(
                ms.ToArray(), file.ContentType, GetCurrentPersonId(), postalCode);
            return Ok(result);
        }
    }
}
