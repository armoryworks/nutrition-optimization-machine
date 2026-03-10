using System;
using Nom.Data.Person; // IMPORTANT: Need this using statement for PersonEntity

namespace Nom.Data
{
    public abstract class BaseEntity
    {
        public long Id { get; set; }

        // Audit Fields
        public DateTime CreatedDate { get; set; }
        public long? CreatedByPersonId { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public long? LastModifiedByPersonId { get; set; }
    }
}