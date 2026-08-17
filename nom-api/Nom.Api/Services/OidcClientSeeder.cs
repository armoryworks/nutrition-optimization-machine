using Microsoft.EntityFrameworkCore;
using Nom.Data;
using OpenIddict.Abstractions;

namespace Nom.Api.Services
{
    /// <summary>
    /// Registers the OIDC clients this authority serves. Clients are described in
    /// configuration (Oidc:Clients) rather than hardcoded, so a new environment
    /// only needs its redirect URIs set — and re-running is safe: an existing
    /// client is updated in place, never duplicated.
    ///
    /// Browser clients are public — no secret, PKCE required. A descriptor with
    /// a ClientSecret describes a RESOURCE SERVER instead: confidential, allowed
    /// only to call introspection, never to start an interactive flow.
    /// </summary>
    public class OidcClientSeeder : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OidcClientSeeder> _logger;

        public OidcClientSeeder(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<OidcClientSeeder> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public sealed class ClientDescriptor
        {
            public string ClientId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string[] RedirectUris { get; set; } = [];
            public string[] PostLogoutRedirectUris { get; set; } = [];
            public string[] Scopes { get; set; } = [];

            /// <summary>
            /// Set for RESOURCE SERVERS (e.g. brigade-api) that call the
            /// introspection endpoint. A client with a secret is confidential and
            /// gets no interactive flow — it can ask about tokens, never mint them.
            /// </summary>
            public string? ClientSecret { get; set; }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var clients = _configuration.GetSection("Oidc:Clients").Get<ClientDescriptor[]>() ?? [];
            if (clients.Length == 0)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();

            // The OpenIddict tables may not exist yet on a database that has not
            // had the migration applied; seeding must never block startup.
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                _logger.LogWarning("OIDC client seeding skipped: database unreachable");
                return;
            }

            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            foreach (var client in clients)
            {
                var isResourceServer = !string.IsNullOrWhiteSpace(client.ClientSecret);

                if (string.IsNullOrWhiteSpace(client.ClientId) ||
                    (!isResourceServer && client.RedirectUris.Length == 0))
                {
                    _logger.LogWarning("Skipping OIDC client with no id or no redirect URIs");
                    continue;
                }

                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = client.ClientId,
                    DisplayName = string.IsNullOrWhiteSpace(client.DisplayName) ? client.ClientId : client.DisplayName,
                };

                if (isResourceServer)
                {
                    // Introspection only: no interactive flow, no redirect URIs.
                    descriptor.ClientSecret = client.ClientSecret;
                    descriptor.ClientType = OpenIddictConstants.ClientTypes.Confidential;
                    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
                }
                else
                {
                    descriptor.ClientType = OpenIddictConstants.ClientTypes.Public;
                    descriptor.ConsentType = OpenIddictConstants.ConsentTypes.Implicit;
                    foreach (var permission in new[]
                    {
                        OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.Endpoints.EndSession,
                        OpenIddictConstants.Permissions.Endpoints.Revocation,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                        OpenIddictConstants.Permissions.ResponseTypes.Code,
                        OpenIddictConstants.Permissions.Scopes.Email,
                        OpenIddictConstants.Permissions.Scopes.Profile,
                    })
                    {
                        descriptor.Permissions.Add(permission);
                    }

                    descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
                }

                foreach (var uri in client.RedirectUris)
                {
                    descriptor.RedirectUris.Add(new Uri(uri));
                }

                foreach (var uri in client.PostLogoutRedirectUris)
                {
                    descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
                }

                foreach (var scopeName in client.Scopes)
                {
                    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
                }

                try
                {
                    var existing = await manager.FindByClientIdAsync(client.ClientId, cancellationToken);
                    if (existing is null)
                    {
                        await manager.CreateAsync(descriptor, cancellationToken);
                        _logger.LogInformation("Registered OIDC client {ClientId}", client.ClientId);
                    }
                    else
                    {
                        await manager.UpdateAsync(existing, descriptor, cancellationToken);
                        _logger.LogInformation("Updated OIDC client {ClientId}", client.ClientId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to seed OIDC client {ClientId}", client.ClientId);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
