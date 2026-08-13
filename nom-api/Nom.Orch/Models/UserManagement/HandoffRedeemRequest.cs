using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    /// <summary>
    /// Request model for redeeming a one-time login handoff code
    /// (cross-origin sign-in transfer from the marketing site).
    /// </summary>
    public class HandoffRedeemRequest
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
