using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Person
{
    public class PersonAttribute : BaseEntity
    {
        [ForeignKey(nameof(Person))]
        public required long PersonId { get; set; }



        public virtual required Person Person { get; set; }
    }
}