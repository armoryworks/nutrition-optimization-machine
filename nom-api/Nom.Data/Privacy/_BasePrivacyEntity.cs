// File: Nom.Data/Privacy/_BasePrivacyEntity.cs

using Nom.Data.Person;

namespace Nom.Data.Privacy
{
    /// <summary>
    /// Abstract base class for all privacy-related entities.
    /// It ensures that every privacy record is associated with a Person.
    /// Inherits from BaseEntity to include common audit fields.
    /// </summary>
    public abstract class BasePrivacyEntity : BaseEntity
    {
        /// <summary>
        /// The unique identifier of the person to whom this privacy record belongs.
        /// This is a required foreign key.
        /// </summary>
        public long PersonId { get; set; }

        /// <summary>
        /// Navigation property to the associated PersonEntity.
        /// This links the privacy record directly to the user's profile.
        /// </summary>
        public virtual PersonEntity? Person { get; set; }
    }
}
