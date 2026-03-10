namespace Nom.Data
{
    /// <summary>
    /// System-wide constants for well-known entity IDs.
    /// </summary>
    public static class SystemConstants
    {
        /// <summary>
        /// The PersonId used for system-initiated operations (e.g., registration,
        /// seed data, automated processes). This corresponds to the system person
        /// created during initial database setup.
        /// </summary>
        public const long SystemPersonId = 1L;
    }
}
