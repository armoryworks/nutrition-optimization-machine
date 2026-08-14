// File: nom-api/Nom.Data/Shopping/GroceryConnectionEntity.cs

using System;
using Nom.Data.Person;

namespace Nom.Data.Shopping
{
    /// <summary>
    /// A person's authorization with a retailer that supports cart push (e.g.
    /// Kroger). Tokens are stored encrypted at rest — they grant access to the
    /// user's real shopping account, so they are never returned to the client
    /// and never logged.
    /// </summary>
    public class GroceryConnectionEntity : BaseEntity
    {
        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        /// <summary>Provider key from the grocery service ("kroger").</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>Encrypted OAuth access token.</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>Encrypted OAuth refresh token, when the provider issues one.</summary>
        public string? RefreshToken { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        /// <summary>Retailer store the user shops at — carts are per-location.</summary>
        public string? LocationId { get; set; }

        public string? LocationName { get; set; }

        /// <summary>
        /// Encrypted OAuth state nonce while a connect handshake is in flight;
        /// cleared once tokens are stored. Guards against callbacks that didn't
        /// originate from a flow this user started.
        /// </summary>
        public string? PendingState { get; set; }
    }
}
