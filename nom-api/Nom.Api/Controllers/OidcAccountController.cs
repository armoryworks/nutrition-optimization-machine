using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The sign-in page for the OIDC authorization flow only. The SPA has its own
    /// login against the bearer-token endpoints; this one exists because
    /// /connect/authorize needs an interactive, cookie-based sign-in it can
    /// redirect to. Rendered inline rather than via Razor to keep nom-api free of
    /// a view engine for one page.
    /// </summary>
    [AllowAnonymous]
    public class OidcAccountController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;

        public OidcAccountController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet("~/connect/login")]
        public IActionResult Login(string? returnUrl = null, string? error = null)
            => Content(Page(returnUrl, error), "text/html");

        [HttpPost("~/connect/login")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            var user = await _userManager.FindByEmailAsync(email ?? string.Empty);
            if (user is not null)
            {
                var result = await _signInManager.PasswordSignInAsync(
                    user, password ?? string.Empty, isPersistent: false, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    // Only ever return to a local path — an attacker-supplied
                    // absolute URL here would be an open redirect.
                    return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
                }

                if (result.IsLockedOut)
                {
                    return Content(Page(returnUrl, "This account is temporarily locked. Try again later."), "text/html");
                }
            }

            // Deliberately identical for unknown accounts and wrong passwords.
            return Content(Page(returnUrl, "That email and password combination was not recognised."), "text/html");
        }

        private static string Page(string? returnUrl, string? error) => $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex, nofollow">
              <title>Sign in — NOM</title>
              <style>
                :root { color-scheme: light dark; }
                body { font: 16px/1.5 system-ui, sans-serif; display: grid; place-items: center;
                       min-height: 100vh; margin: 0; background: #f6f7f9; color: #16181d; }
                @media (prefers-color-scheme: dark) { body { background: #16181d; color: #f6f7f9; } }
                form { width: min(22rem, 90vw); display: grid; gap: .75rem; padding: 2rem;
                       border-radius: .75rem; background: Canvas; box-shadow: 0 1px 3px #0002; }
                h1 { font-size: 1.25rem; margin: 0 0 .5rem; }
                label { display: grid; gap: .25rem; font-size: .875rem; }
                input { font: inherit; padding: .5rem .625rem; border: 1px solid #8884;
                        border-radius: .375rem; background: Field; color: FieldText; }
                button { font: inherit; font-weight: 600; padding: .625rem; border: 0;
                         border-radius: .375rem; background: #1f6f4a; color: #fff; cursor: pointer; }
                .error { font-size: .875rem; color: #b3261e; margin: 0; }
                .note { font-size: .75rem; opacity: .7; margin: .5rem 0 0; }
              </style>
            </head>
            <body>
              <form method="post" action="/connect/login">
                <h1>Sign in to continue</h1>
                {{(error is null ? "" : $"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>")}}
                <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? "/")}}">
                <label>Email
                  <input name="email" type="email" autocomplete="username" required autofocus>
                </label>
                <label>Password
                  <input name="password" type="password" autocomplete="current-password" required>
                </label>
                <button type="submit">Sign in</button>
                <p class="note">You are signing in to your NOM account. The application you
                came from will receive your name, email address and permissions.</p>
              </form>
            </body>
            </html>
            """;
    }
}
