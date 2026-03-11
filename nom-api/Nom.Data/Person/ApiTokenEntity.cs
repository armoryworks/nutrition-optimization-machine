using System;

namespace Nom.Data.Person
{
    public class ApiTokenEntity : BaseEntity
    {
        public string UserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 hash of the token value. The raw token is only returned once at creation.
        /// </summary>
        public string TokenHash { get; set; } = string.Empty;

        public DateTime? LastUsedDate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
