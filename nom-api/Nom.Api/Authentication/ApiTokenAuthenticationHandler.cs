using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nom.Data;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Authenticates requests that carry an API token in the X-Api-Key header.
    /// The presented token is hashed (SHA-256, matching UserManagementOrchestrationService.HashToken)
    /// and matched against active ApiToken rows; on success the request runs as the token's
    /// owner with their full claims, and LastUsedDate is stamped.
    /// </summary>
    public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiToken";
        public const string HeaderName = "X-Api-Key";

        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public ApiTokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ApplicationDbContext dbContext,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager)
            : base(options, logger, encoder)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
            {
                return AuthenticateResult.NoResult();
            }

            var rawToken = headerValues.ToString();
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return AuthenticateResult.NoResult();
            }

            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

            var token = await _dbContext.ApiTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.IsActive);
            if (token == null)
            {
                return AuthenticateResult.Fail("Invalid API token.");
            }

            var user = await _userManager.FindByIdAsync(token.UserId);
            if (user == null)
            {
                return AuthenticateResult.Fail("API token owner no longer exists.");
            }

            token.LastUsedDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var principal = await _signInManager.CreateUserPrincipalAsync(user);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
    }
}
