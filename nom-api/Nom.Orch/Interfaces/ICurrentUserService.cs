namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Single source of truth for the authenticated caller's identity, replacing the
    /// divergent per-service GetCurrentPersonId/GetCurrentUserId copies.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>PersonId claim of the caller, or null (e.g. during registration).</summary>
        long? PersonId { get; }

        /// <summary>PersonId, falling back to the System person when no claim is present.</summary>
        long PersonIdOrSystem { get; }

        /// <summary>PersonId, throwing UnauthorizedAccessException when absent.</summary>
        long RequiredPersonId { get; }

        /// <summary>Identity user id (sub / NameIdentifier claim), or null.</summary>
        string? UserId { get; }

        /// <summary>Identity user id, throwing UnauthorizedAccessException when absent.</summary>
        string RequiredUserId { get; }
    }
}
