using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    public class Group : BaseEntity
    {
        public required string Name { get; set; }
        [ForeignKey(nameof(Reference.Id))]
        public virtual ICollection<Reference>? References { get; set; }
    }
}