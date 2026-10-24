using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api.Authentication;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The reset email must carry a link the UI can open and redeem: Identity's
    /// /resetPassword takes {email, resetCode, newPassword} and rejects anything else.
    /// </summary>
    public class PasswordResetEmailTests
    {
        private const string FrontendUrl = "https://nom.example";

        [Fact]
        public void The_link_opens_the_reset_page_with_the_email_and_code()
        {
            var link = PasswordResetEmail.Link(FrontendUrl + "/", "a+b@example.com", "Q2Zk-_x9");

            link.Should().Be("https://nom.example/reset-password?email=a%2Bb%40example.com&code=Q2Zk-_x9");
        }

        [Fact]
        public void The_link_carries_the_code_identity_html_encoded_as_the_raw_code()
        {
            var link = PasswordResetEmail.Link(FrontendUrl, "a@example.com", "a&amp;b");

            QueryHelpers.ParseQuery(new Uri(link).Query)["code"].ToString().Should().Be("a&b");
        }

        [Fact]
        public void Both_bodies_carry_the_link_and_neither_shows_a_bare_code()
        {
            var link = PasswordResetEmail.Link(FrontendUrl, "a@example.com", "Q2Zk");

            var html = PasswordResetEmail.HtmlBody(link);
            var text = PasswordResetEmail.TextBody(link);

            html.Should().Contain($"href=\"{WebUtility.HtmlEncode(link)}\"");
            html.Should().Contain("Reset Password");
            html.Should().NotContain("reset code");
            text.Should().Contain(link);
            text.Should().NotContain("<");
        }

        [Fact]
        public async Task The_emailed_link_resets_the_password_through_the_identity_endpoint()
        {
            await using var app = await StartAsync();
            var client = app.GetTestClient();
            var users = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "cook@example.com", Email = "cook@example.com", EmailConfirmed = true };
            (await users.CreateAsync(user, "OldPassword1!")).Succeeded.Should().BeTrue();

            var forgot = await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = "cook@example.com" });
            forgot.StatusCode.Should().Be(HttpStatusCode.OK);

            var sender = app.Services.GetRequiredService<CapturingEmailSender>();
            sender.Links.Should().ContainSingle();
            var link = new Uri(sender.Links[0]);
            link.GetLeftPart(UriPartial.Path).Should().Be("https://nom.example/reset-password");
            var query = QueryHelpers.ParseQuery(link.Query);

            var reset = await client.PostAsJsonAsync("/api/auth/resetPassword", new
            {
                email = query["email"].ToString(),
                resetCode = query["code"].ToString(),
                newPassword = "NewPassword1!",
            });

            reset.StatusCode.Should().Be(HttpStatusCode.OK);
            var fresh = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var reloaded = await fresh.FindByEmailAsync("cook@example.com");
            (await fresh.CheckPasswordAsync(reloaded!, "NewPassword1!")).Should().BeTrue();
            (await fresh.CheckPasswordAsync(reloaded!, "OldPassword1!")).Should().BeFalse();
        }

        [Fact]
        public async Task An_invalid_code_is_rejected_as_an_invalid_token()
        {
            await using var app = await StartAsync();
            var client = app.GetTestClient();
            var users = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "cook@example.com", Email = "cook@example.com", EmailConfirmed = true };
            (await users.CreateAsync(user, "OldPassword1!")).Succeeded.Should().BeTrue();

            var reset = await client.PostAsJsonAsync("/api/auth/resetPassword", new
            {
                email = "cook@example.com",
                resetCode = "bm90LWEtY29kZQ",
                newPassword = "NewPassword1!",
            });

            reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await reset.Content.ReadAsStringAsync()).Should().Contain("InvalidToken");
        }

        [Fact]
        public async Task Forgot_password_answers_the_same_whether_or_not_the_account_exists()
        {
            await using var app = await StartAsync();
            var client = app.GetTestClient();
            var users = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "cook@example.com", Email = "cook@example.com", EmailConfirmed = true };
            (await users.CreateAsync(user, "OldPassword1!")).Succeeded.Should().BeTrue();

            var known = await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = "cook@example.com" });
            var unknown = await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = "nobody@example.com" });

            unknown.StatusCode.Should().Be(known.StatusCode);
            (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());
            app.Services.GetRequiredService<CapturingEmailSender>().Links.Should().ContainSingle();
        }

        private static async Task<WebApplication> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            var database = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<IdentityOnlyDbContext>(o => o.UseInMemoryDatabase(database));
            builder.Services.AddAuthorization();
            builder.Services.AddIdentityApiEndpoints<IdentityUser>()
                .AddEntityFrameworkStores<IdentityOnlyDbContext>();
            builder.Services.AddSingleton<CapturingEmailSender>();
            builder.Services.AddSingleton<IEmailSender<IdentityUser>>(sp => sp.GetRequiredService<CapturingEmailSender>());

            var app = builder.Build();
            app.MapGroup("api/auth").MapIdentityApi<IdentityUser>();
            await app.StartAsync();
            return app;
        }

        private sealed class IdentityOnlyDbContext(DbContextOptions<IdentityOnlyDbContext> options)
            : IdentityDbContext<IdentityUser>(options);

        private sealed class CapturingEmailSender : IEmailSender<IdentityUser>
        {
            public List<string> Links { get; } = new();

            public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) => Task.CompletedTask;

            public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) => Task.CompletedTask;

            public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
            {
                Links.Add(PasswordResetEmail.Link(FrontendUrl, email, resetCode));
                return Task.CompletedTask;
            }
        }
    }
}
