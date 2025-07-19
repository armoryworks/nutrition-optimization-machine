// File: Nom.Orch/Models/Privacy/PrivacyRequestStatusResponse.cs

using System;

namespace Nom.Orch.Models.Privacy
{
    /// <summary>
    /// A generic response model for asynchronous privacy operations
    /// like data export or deletion, confirming that the request has been received.
    /// </summary>
    public class PrivacyRequestStatusResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long RequestId { get; set; } // CORRECTED: Changed from Guid to long
        public string Status { get; set; } = string.Empty;
    }
}
