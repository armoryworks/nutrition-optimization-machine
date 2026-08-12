// File: nom-api/Nom.Data/Recipe/RecipeLicenseStatus.cs

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Known values for <see cref="RecipeEntity.LicenseStatus"/>. Stored as a
    /// string rather than a reference row so new statuses don't need seeding.
    /// </summary>
    public static class RecipeLicenseStatus
    {
        /// <summary>Scraped, license not determined — treat as all-rights-reserved.</summary>
        public const string Unknown = "Unknown";

        public const string AllRightsReserved = "AllRightsReserved";

        public const string CreativeCommons = "CreativeCommons";

        public const string PublicDomain = "PublicDomain";

        /// <summary>Entered by a user, not scraped.</summary>
        public const string UserSubmitted = "UserSubmitted";
    }
}
