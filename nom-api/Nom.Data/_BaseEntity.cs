using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person; // IMPORTANT: Need this using statement for PersonEntity

namespace Nom.Data
{
    public abstract class BaseEntity : IAuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        // Audit Fields
        public DateTime CreatedDate { get; set; }
        public long? CreatedByPersonId { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public long? LastModifiedByPersonId { get; set; }
    }
}