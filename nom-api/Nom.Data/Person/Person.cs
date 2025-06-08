using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Nom.Data.Person
{
    public class Person : BaseEntity
    {
        public required string Name { get; set; }
     
        [ForeignKey(nameof(User))]
        public string? AspNetUserId { get; set; } 

        public virtual IdentityUser? User { get; set; }
    }
}