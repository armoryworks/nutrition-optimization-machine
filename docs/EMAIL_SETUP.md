# Outbound email (SMTP)

NOM sends account emails — registration confirmation, password reset — through
any standard SMTP server. **Email is optional**: when `EMAIL_SMTP_HOST` is
empty (the default), NOM uses a no-op sender and accounts work without
confirmation email.

## Configuration

Set in `.env` (compose maps these to the API's `Email` settings section):

| Variable | Default | Notes |
|---|---|---|
| `EMAIL_SMTP_HOST` | *(empty = disabled)* | SMTP server hostname or IP |
| `EMAIL_SMTP_PORT` | `587` | 587 STARTTLS, 465 SSL, 25 plain (internal relays) |
| `EMAIL_SMTP_USER` | *(empty)* | Empty = unauthenticated (e.g. an internal relay) |
| `EMAIL_SMTP_PASSWORD` | *(empty)* | |
| `EMAIL_FROM_ADDRESS` | `noreply@nom.local` | Sender shown on emails |
| `EMAIL_FROM_NAME` | `NOM` | |
| `EMAIL_USE_SSL` | `true` | Set `false` for plain port-25 relays |
| `FRONTEND_URL` | `http://localhost:4210` | **Public URL of your NOM UI** — links in account emails point here |

Don't forget `FRONTEND_URL`: without it, confirmation links in the emails
point at localhost.

## Examples

**Google Workspace SMTP relay** (host allowlisted by IP or authenticated with
an app password; Google rejects sender domains not registered to the
Workspace, so `EMAIL_FROM_ADDRESS` must use one of your Workspace domains):

```env
EMAIL_SMTP_HOST=smtp-relay.gmail.com
EMAIL_SMTP_PORT=587
EMAIL_SMTP_USER=you@yourdomain.com
EMAIL_SMTP_PASSWORD=<16-char app password>
EMAIL_FROM_ADDRESS=noreply@yourdomain.com
EMAIL_USE_SSL=true
```

**Internal LAN relay** (postfix or similar, no auth):

```env
EMAIL_SMTP_HOST=mail.internal.example
EMAIL_SMTP_PORT=25
EMAIL_USE_SSL=false
EMAIL_FROM_ADDRESS=noreply@yourdomain.com
```

**SendGrid / Mailgun / etc.**: use the provider's SMTP host with the API key
as the password, per their docs.
