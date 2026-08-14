using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Sending shopping lists to external destinations (share sheet, Instacart,
    /// retailer carts) through the operator's grocery service. Every endpoint
    /// degrades gracefully when no service is configured: the provider list
    /// comes back empty and the UI hides the feature.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class GroceryExportController : BaseApiController
    {
        private readonly IGroceryExportOrchestrationService _grocery;
        private readonly ILogger<GroceryExportController> _logger;

        public GroceryExportController(
            IGroceryExportOrchestrationService grocery,
            ILogger<GroceryExportController> logger)
        {
            _grocery = grocery;
            _logger = logger;
        }

        /// <summary>Destinations available to this user, with their connection state.</summary>
        [HttpGet("providers")]
        public async Task<ActionResult<List<GroceryProviderInfo>>> GetProviders()
        {
            var personId = GetCurrentPersonIdRequired();
            return Ok(await _grocery.GetProvidersAsync(personId));
        }

        /// <summary>Send a shopping list to the chosen destination.</summary>
        [HttpPost("list/{shoppingListId:long}")]
        public async Task<ActionResult<GroceryExportResult>> ExportList(
            long shoppingListId, [FromBody] GroceryExportOptionsModel options)
        {
            var personId = GetCurrentPersonIdRequired();
            var result = await _grocery.ExportListAsync(shoppingListId, personId, options);

            // A failed export is an expected outcome (empty list, unmatched
            // items, expired connection) — the client renders result.error.
            return Ok(result);
        }

        /// <summary>
        /// Send lines the client is displaying. The shopping view is a live
        /// projection over the meal plan, so there is usually no saved list to
        /// reference — the client sends what the user sees.
        /// </summary>
        [HttpPost("items")]
        public async Task<ActionResult<GroceryExportResult>> ExportItems([FromBody] GroceryExportItemsModel model)
        {
            var personId = GetCurrentPersonIdRequired();
            return Ok(await _grocery.ExportItemsAsync(personId, model));
        }

        /// <summary>Begin connecting a retailer account; returns the URL to send the user to.</summary>
        [HttpPost("connect/{provider}")]
        public async Task<ActionResult<object>> StartConnection(string provider, [FromQuery] string? returnUrl)
        {
            var personId = GetCurrentPersonIdRequired();
            var redirectUri = BuildRedirectUri(provider, returnUrl);

            var url = await _grocery.StartConnectionAsync(provider, personId, redirectUri);
            if (url == null)
            {
                return BadRequest(new { message = $"{provider} cannot be connected on this server." });
            }

            return Ok(new { url });
        }

        /// <summary>
        /// OAuth callback. The retailer redirects the user's browser here, so it
        /// answers with a redirect back into the app rather than JSON.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("callback/{provider}")]
        public async Task<IActionResult> Callback(
            string provider,
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            [FromQuery] string? returnUrl)
        {
            var appReturn = string.IsNullOrWhiteSpace(returnUrl) ? "/shopping" : returnUrl!;

            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                _logger.LogWarning("Grocery callback for {Provider} returned an error: {Error}", provider, error);
                return Redirect($"{appReturn}?connected=failed&provider={Uri.EscapeDataString(provider)}");
            }

            // The person is identified by the state we issued, not by the
            // session — retailer callbacks arrive without our auth cookie.
            var personSegment = state.Split('.', 2)[0];
            if (!long.TryParse(personSegment, out var personId))
            {
                return Redirect($"{appReturn}?connected=failed&provider={Uri.EscapeDataString(provider)}");
            }

            var redirectUri = BuildRedirectUri(provider, returnUrl);
            var ok = await _grocery.CompleteConnectionAsync(provider, personId, code, state, redirectUri);

            return Redirect($"{appReturn}?connected={(ok ? "ok" : "failed")}&provider={Uri.EscapeDataString(provider)}");
        }

        /// <summary>Stores near a postal code, for choosing which cart to fill.</summary>
        [HttpGet("stores/{provider}")]
        public async Task<ActionResult<List<GroceryStore>>> FindStores(string provider, [FromQuery] string postalCode)
        {
            if (string.IsNullOrWhiteSpace(postalCode))
            {
                return BadRequest(new { message = "postalCode is required" });
            }

            return Ok(await _grocery.FindStoresAsync(provider, postalCode));
        }

        [HttpPut("stores/{provider}")]
        public async Task<IActionResult> SetStore(string provider, [FromBody] GroceryStoreSelectionModel model)
        {
            var personId = GetCurrentPersonIdRequired();
            return await _grocery.SetStoreAsync(provider, personId, model.LocationId, model.LocationName)
                ? NoContent()
                : NotFound(new { message = $"No {provider} connection to update." });
        }

        [HttpDelete("connect/{provider}")]
        public async Task<IActionResult> Disconnect(string provider)
        {
            var personId = GetCurrentPersonIdRequired();
            return await _grocery.DisconnectAsync(provider, personId)
                ? NoContent()
                : NotFound(new { message = $"No {provider} connection to remove." });
        }

        /// <summary>
        /// Absolute callback URL for this deployment. Must match byte-for-byte
        /// between the authorize call and the exchange, so both go through here.
        /// </summary>
        private string BuildRedirectUri(string provider, string? returnUrl)
        {
            var baseUri = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var uri = $"{baseUri}/api/GroceryExport/callback/{Uri.EscapeDataString(provider)}";
            return string.IsNullOrWhiteSpace(returnUrl)
                ? uri
                : $"{uri}?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }
    }
}
