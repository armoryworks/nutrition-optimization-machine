using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Cookbook
{
    public class CookbookUpdateModel
    {
        [MaxLength(255)]
        public string? Name { get; set; }

        [MaxLength(2047)]
        public string? Description { get; set; }

        public bool? IsPublic { get; set; }
    }
}
