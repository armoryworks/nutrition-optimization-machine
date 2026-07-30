# NOM API Security Inventory

Rewritten from the actual code on 2026-07-30 (the previous version of this document
claimed ~10 controls as IMPLEMENTED that were commented out or had zero consumers).
Statuses below are verified against source; keep this file honest when things change.

## Active controls (wired and enforced)

| Control | Where | Notes |
|---|---|---|
| Authentication | ASP.NET Identity bearer tokens (`Program.cs`, `MapIdentityApi`) | Opaque bearer tokens, 24 h expiry; lockout enabled (5 attempts / 15 min) |
| Claims | `CustomClaimsPrincipalFactory` | Tenant-scoped household/plan claims; global `CanManageCuration` / `CanManageUserRoles` come **only** from stored user claims (escalation from household admin severed 2026-07-30) |
| Authorization policies | `Program.cs` | `CanManageCuration`, `CanManageUserRoles` (both satisfiable). Former unsatisfiable policies removed |
| API token auth | `ApiTokenAuthenticationHandler` (`X-Api-Key`) | SHA-256 hash match against active tokens; requests run as the token owner; `LastUsedDate` stamped |
| Initial admin grant | `_GrantInitialAdminClaims.sql` | Deterministic (`ORDER BY "Id"`); run manually after first registration |
| Tenant scoping | `ShoppingListOrchestrationService` (2026-07-30), household/plan checks elsewhere | Shopping lists scoped to author + household members |
| CORS | `Program.cs` | Fail-closed outside Development; accepts `;` or `,` delimited `AllowedOrigins` |
| Global exception handling | `GlobalExceptionMiddleware` | Known issue: maps `InvalidOperationException` → 400 with raw message |
| Rate limiting | `RateLimitingMiddleware` | In-memory (per-process); per-minute/hour/day + burst caps |
| Upload validation (JSON body) | `UserManagementOrchestrationService.UploadUserImageAsync` | 5 MB cap, JPEG/PNG/WebP magic-byte check |
| Upload validation (multipart) | `FileUploadSecurityMiddleware` | Multipart uploads only |
| Security headers | `ContainerSecurityMiddleware` | Active header set |
| Soft delete + audit stamping | `ApplicationDbContext` | Global `IsDeleted` filter; `CreatedBy`/`ModifiedBy`/`DeletedBy` from `PersonId` claim. HTTP-context-less writes skip both |
| Audit logging | `AuditLoggingMiddleware` | Writes to `ILogger` only — no persistent audit store |
| Password reset | Identity token flow | Token no longer written to logs (fixed 2026-07-30); email delivery required for production |

## Present in the codebase but NOT wired (do not claim these)

- `DataEncryptionService` — zero consumers; also unsound (static IV, hardcoded fallback key)
- `MultiFactorAuthenticationService` — zero consumers, no endpoints
- `SessionManagementService`, `DataRetentionService`, `VulnerabilityScanningService`,
  `AdvancedMonitoringService` — zero consumers
- `SecurityHeadersMiddleware`, `InputValidationMiddleware` — commented out in `Program.cs`
  (the input-validation regex blocklist rejects apostrophes and should not be enabled as-is)

## Known open gaps

1. API tokens have no expiry or scope model — a token grants its owner's full access
   until deactivated. (Validation itself works: `ApiTokenAuthenticationHandler` accepts
   `X-Api-Key`, matches active token hashes, and stamps `LastUsedDate`.)
2. No persistent audit log store.
3. Secrets come from environment variables only — no secret manager / key vault.
4. `AllowedHosts: "*"` in appsettings.
5. Uploaded user images live outside the mounted data volume (lost on redeploy).
6. E2E/CI runs the API with `ASPNETCORE_ENVIRONMENT=Development` (Swagger exposed).
