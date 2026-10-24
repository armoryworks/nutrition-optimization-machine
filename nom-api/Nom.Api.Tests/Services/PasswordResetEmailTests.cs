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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Api.Authentication;
using Nom.Api.Settings;
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
        public async Task The_emailed_link_resets_the_password_once_through_the_identity_endpoint()
        {
            await using var app = await StartAsync();
            var client = app.GetTestClient();
            var users = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "cook@example.com", Email = "cook@example.com", EmailConfirmed = true };
            (await users.CreateAsync(user, "OldPassword1!")).Succeeded.Should().BeTrue();

            var forgot = await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = "cook@example.com" });
            forgot.StatusCode.Should().Be(HttpStatusCode.OK);

            var log = app.Services.GetRequiredService<CapturingLoggerProvider>();
            log.ResetLinks.Should().ContainSingle();
            var link = new Uri(log.ResetLinks[0]);
            link.GetLeftPart(UriPartial.Path).Should().Be("https://nom.example/reset-password");
            var query = QueryHelpers.ParseQuery(link.Query);
            var body = new
            {
                email = query["email"].ToString(),
                resetCode = query["code"].ToString(),
                newPassword = "NewPassword1!",
            };

            var reset = await client.PostAsJsonAsync("/api/auth/resetPassword", body);

            reset.StatusCode.Should().Be(HttpStatusCode.OK);
            var fresh = app.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var reloaded = await fresh.FindByEmailAsync("cook@example.com");
            (await fresh.CheckPasswordAsync(reloaded!, "NewPassword1!")).Should().BeTrue();
            (await fresh.CheckPasswordAsync(reloaded!, "OldPassword1!")).Should().BeFalse();

            var replay = await client.PostAsJsonAsync("/api/auth/resetPassword", body);
            replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await replay.Content.ReadAsStringAsync()).Should().Contain("InvalidToken");
        }

        [Fact]
        public async Task Reset_codes_come_from_the_short_lived_provider()
        {
            await using var app = await StartAsync();

            app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Tokens.PasswordResetTokenProvider
                .Should().Be(PasswordResetTokenProvider.ProviderName);
            app.Services.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value.TokenLifespan
                .Should().Be(TimeSpan.FromHours(1));
            app.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan
                .Should().Be(TimeSpan.FromDays(1));
        }

        [Fact]
        public async Task In_production_the_no_op_sender_logs_neither_the_link_nor_the_code()
        {
            var log = new CapturingLoggerProvider();
            var environment = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production);
            var sender = new NoOpEmailSender(
                new LoggerFactory(new[] { log }).CreateLogger<NoOpEmailSender>(),
                Options.Create(new FrontendSettings { Url = FrontendUrl }),
                environment);

            await sender.SendPasswordResetCodeAsync(new IdentityUser { Id = "u-1" }, "cook@example.com", "Q2Zk-secret");
            await sender.SendPasswordResetLinkAsync(new IdentityUser { Id = "u-1" }, "cook@example.com", "https://nom.example/x?code=Q2Zk-secret");

            log.Messages.Should().NotBeEmpty();
            log.Messages.Should().NotContain(m => m.Contains("Q2Zk-secret") || m.Contains("reset-password"));
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
            app.Services.GetRequiredService<CapturingLoggerProvider>().ResetLinks.Should().ContainSingle();
        }

        private static async Task<WebApplication> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            var log = new CapturingLoggerProvider();
            builder.Logging.AddProvider(log);
            builder.Services.AddSingleton(log);
            var database = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<IdentityOnlyDbContext>(o => o.UseInMemoryDatabase(database));
            builder.Services.AddAuthorization();
            builder.Services.AddIdentityApiEndpoints<IdentityUser>()
                .AddEntityFrameworkStores<IdentityOnlyDbContext>()
                .AddPasswordResetTokenProvider();
            builder.Services.Configure<FrontendSettings>(f => f.Url = FrontendUrl);
            builder.Services.AddTransient<IEmailSender<IdentityUser>, NoOpEmailSender>();

            var app = builder.Build();
            app.MapGroup("api/auth").MapIdentityApi<IdentityUser>();
            await app.StartAsync();
            return app;
        }

        private sealed class IdentityOnlyDbContext(DbContextOptions<IdentityOnlyDbContext> options)
            : IdentityDbContext<IdentityUser>(options);

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public List<string> Messages { get; } = new();

            public List<string> ResetLinks => Messages
                .Where(m => m.StartsWith("Link: ", StringComparison.Ordinal) && m.Contains("/reset-password?"))
                .Select(m => m["Link: ".Length..])
                .ToList();

            public ILogger CreateLogger(string categoryName) =>
                categoryName == typeof(NoOpEmailSender).FullName ? new Capturing(Messages) : NullLogger.Instance;

            public void Dispose()
            {
            }

            private sealed class Capturing(List<string> messages) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (messages)
                    {
                        messages.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}
