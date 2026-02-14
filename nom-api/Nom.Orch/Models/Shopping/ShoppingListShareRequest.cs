using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListShareRequest
    {
        [Required]
        public long PersonId { get; set; }
    }
}
