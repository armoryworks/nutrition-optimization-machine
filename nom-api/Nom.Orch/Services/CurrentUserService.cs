using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public long? PersonId
        {
            get
            {
                var claim = _httpContextAccessor.HttpContext?.User?.FindFirstValue("PersonId");
                return long.TryParse(claim, out var personId) ? personId : null;
            }
        }

        public long PersonIdOrSystem => PersonId ?? Nom.Data.SystemConstants.SystemPersonId;

        public long RequiredPersonId => PersonId
            ?? throw new UnauthorizedAccessException("PersonId claim is missing from the caller's token.");

        public string? UserId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                return user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }

        public string RequiredUserId => UserId
            ?? throw new UnauthorizedAccessException("User is not authenticated.");
    }
}
