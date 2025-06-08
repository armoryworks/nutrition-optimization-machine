using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    public class Reference : BaseEntity
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        [ForeignKey(nameof(Group.Id))]
        public virtual ICollection<Group>? Groups { get; set; }
    }
}