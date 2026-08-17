using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The OIDC flow endpoints. NOM is the authority for its sibling apps (the
    /// Brigade provider console today), so a user signs in here once and every
    /// app validates the resulting token against this server — one identity, one
    /// user store, one place to revoke.
    ///
    /// Interactive sign-in uses Identity's own cookie: an unauthenticated
    /// /connect/authorize request is challenged to the login page, which signs
    /// the cookie in and returns here to complete the flow.
    /// </summary>
    public class AuthorizationController : Controller
    {
        private readonly IOpenIddictApplicationManager _applications;
        private readonly IOpenIddictAuthorizationManager _authorizations;
        private readonly IOpenIddictScopeManager _scopes;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _db;

        public AuthorizationController(
            IOpenIddictApplicationManager applications,
            IOpenIddictAuthorizationManager authorizations,
            IOpenIddictScopeManager scopes,
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            ApplicationDbContext db)
        {
            _applications = applications;
            _authorizations = authorizations;
            _scopes = scopes;
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
        }

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Authorize()
        {
            var request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (result?.Succeeded != true)
            {
                // Not signed in (or asked to re-authenticate): bounce to the login
                // page, which returns here once the cookie is established.
                if (request.HasPromptValue(OpenIddictConstants.PromptValues.None))
                {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.LoginRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in.",
                        }));
                }

                return Challenge(
                    authenticationSchemes: IdentityConstants.ApplicationScheme,
                    properties: new AuthenticationProperties
                    {
                        RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                            Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList()),
                    });
            }

            var user = await _userManager.GetUserAsync(result.Principal)
                ?? throw new InvalidOperationException("The user details cannot be retrieved.");

            // A locked-out user must not be able to complete a flow just because
            // an older cookie is still valid.
            if (await _userManager.IsLockedOutAsync(user))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.AccessDenied,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "This account is locked.",
                    }));
            }

            var application = await _applications.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("The calling client application cannot be found.");

            var principal = await BuildPrincipalAsync(user, request.GetScopes());

            // First-party clients only for now, so consent is implicit; a consent
            // screen belongs here when third-party clients are registered.
            var authorization = await _authorizations.CreateAsync(
                principal: principal,
                subject: await _userManager.GetUserIdAsync(user),
                client: (await _applications.GetIdAsync(application))!,
                type: OpenIddictConstants.AuthorizationTypes.Permanent,
                scopes: principal.GetScopes());

            principal.SetAuthorizationId(await _authorizations.GetIdAsync(authorization));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                throw new InvalidOperationException("The specified grant type is not supported.");
            }

            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var user = result.Principal is null ? null : await _userManager.FindByIdAsync(result.Principal.GetClaim(OpenIddictConstants.Claims.Subject)!);

            // Re-check the user on every exchange: this is what makes a
            // disabled or locked account stop working mid-session.
            if (user is null || await _userManager.IsLockedOutAsync(user) ||
                !await _signInManager.CanSignInAsync(user))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "This account can no longer sign in.",
                    }));
            }

            // Rebuild claims from current state rather than replaying the old
            // ones, so revoked admin rights don't survive in a refreshed token.
            var principal = await BuildPrincipalAsync(user, result.Principal!.GetScopes());
            principal.SetAuthorizationId(result.Principal.GetAuthorizationId());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo"), Produces("application/json")]
        public async Task<IActionResult> UserInfo()
        {
            var subject = User.GetClaim(OpenIddictConstants.Claims.Subject);
            var user = subject is null ? null : await _userManager.FindByIdAsync(subject);
            if (user is null)
            {
                return Challenge(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidToken,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The access token is no longer valid.",
                    }));
            }

            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenIddictConstants.Claims.Subject] = await _userManager.GetUserIdAsync(user),
            };

            if (User.HasScope(OpenIddictConstants.Scopes.Email))
            {
                claims[OpenIddictConstants.Claims.Email] = (await _userManager.GetEmailAsync(user))!;
                claims[OpenIddictConstants.Claims.EmailVerified] = await _userManager.IsEmailConfirmedAsync(user);
            }

            var personId = await ResolvePersonIdAsync(user.Id);
            if (personId is not null)
            {
                claims["PersonId"] = personId;
            }

            return Ok(claims);
        }

        [HttpGet("~/connect/logout"), HttpPost("~/connect/logout"), IgnoreAntiforgeryToken]
        public async Task<IActionResult> LogoutPost()
        {
            await _signInManager.SignOutAsync();

            return SignOut(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties { RedirectUri = "/" });
        }

        /// <summary>
        /// Builds the principal from the user's CURRENT state — identity claims
        /// plus NOM's PersonId and admin claims — and marks which of them travel
        /// in the access token versus the identity token.
        /// </summary>
        private async Task<ClaimsPrincipal> BuildPrincipalAsync(IdentityUser user, IEnumerable<string> requestedScopes)
        {
            var principal = await _signInManager.CreateUserPrincipalAsync(user);

            principal.SetScopes(requestedScopes);
            var resources = new List<string>();
            await foreach (var resource in _scopes.ListResourcesAsync(principal.GetScopes()))
            {
                resources.Add(resource);
            }
            principal.SetResources(resources);

            var identity = (ClaimsIdentity)principal.Identity!;
            identity.SetClaim(OpenIddictConstants.Claims.Subject, await _userManager.GetUserIdAsync(user));
            identity.SetClaim(OpenIddictConstants.Claims.Email, await _userManager.GetEmailAsync(user));
            identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName);

            var personId = await ResolvePersonIdAsync(user.Id);
            if (personId is not null)
            {
                identity.SetClaim("PersonId", personId);
            }

            // Admin claims travel so resource servers can authorize without a
            // second lookup — this is what lets Brigade retire its email allowlist.
            foreach (var claim in await _userManager.GetClaimsAsync(user))
            {
                if (claim.Type is "CanManageCuration" or "CanManageUserRoles")
                {
                    identity.SetClaim(claim.Type, claim.Value);
                }
            }

            foreach (var claim in identity.Claims)
            {
                claim.SetDestinations(DestinationsFor(claim, principal));
            }

            return principal;
        }

        private async Task<string?> ResolvePersonIdAsync(string userId) =>
            (await _db.Persons.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync())?.ToString();

        private static IEnumerable<string> DestinationsFor(Claim claim, ClaimsPrincipal principal)
        {
            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Name:
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    if (principal.HasScope(OpenIddictConstants.Scopes.Profile))
                    {
                        yield return OpenIddictConstants.Destinations.IdentityToken;
                    }
                    yield break;

                case OpenIddictConstants.Claims.Email:
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    if (principal.HasScope(OpenIddictConstants.Scopes.Email))
                    {
                        yield return OpenIddictConstants.Destinations.IdentityToken;
                    }
                    yield break;

                // These also travel in the identity token so a browser client can
                // read them from userData instead of decoding the access token —
                // which it must not do, and could not if access tokens were
                // encrypted.
                case "PersonId":
                case "CanManageCuration":
                case "CanManageUserRoles":
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                    yield break;

                // Never leak the security stamp into a token.
                case "AspNet.Identity.SecurityStamp":
                    yield break;

                default:
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    yield break;
            }
        }
    }
}
