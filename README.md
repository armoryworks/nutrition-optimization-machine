# Nutrition Optimization Machine (NOM)

NOM is meal planning for households where one person's diet isn't everyone's diet. It holds each
member's restrictions, goals, and preferences at once, plans a week that works for all of them,
and turns that week into an aisle-ordered shopping list.

A hosted instance runs free while NOM is in beta at **[nom.nommeal.com](https://nom.nommeal.com)**;
the product site is **[nommeal.com](https://nommeal.com)**. This repository holds the whole
application — Angular front end, .NET API, PostgreSQL schema, test suite, and deployment
compose files — under the Apache License 2.0, so you can also run your own.

## What it does

- **Household planning.** Multiple members per household, each with their own dietary
  restrictions, nutrition targets, and preferences. Plans and policies are evaluated against the
  household, not a single profile.
- **Meal plans.** Build a week from recipes, standalone whole foods, or dish groups, with
  restriction checking as you go.
- **Shopping lists.** Generated from the plan, consolidated across recipes and ordered for the
  store, with retail packaging sizes taken into account.
- **Recipes and cookbooks.** Create and organize recipes, or import them by URL. NOM itself
  contains no scraping code — import is delegated to an operator-provided service against an
  admin-approved domain whitelist, and imported content is quarantined until curated. See
  [docs/scraper-integration.md](docs/scraper-integration.md).
- **Grocery export.** Sending a list to a retailer or share sheet is likewise delegated to an
  operator-provided service; NOM stores no retailer integration code. See
  [docs/grocery-integration.md](docs/grocery-integration.md).
- **Food and nutrition data.** A food catalog built from USDA FoodData Central imports, with
  cross-check tooling in `ops/` for reconciling sources. Nutrition is stored per 100 g and
  servings are derived per person. Imports land pending curation and change nothing users see
  until an admin approves them.
- **Pantry.** Track what is on hand so the shopping list only asks for what is missing.
- **Privacy.** Data export and deletion flows backed by a privacy request pipeline and a retention
  service.

Optional AI assistance (Azure OpenAI, Anthropic, or a local Ollama model) supports the import and
catalog-enrichment paths. It is configured per install, is not required to run NOM, and is not
permitted to author nutrition values — models classify and normalize; numbers come from
authoritative sources only.

## Stack

| Component | Technology |
|---|---|
| Front end | Angular 21, Angular Material, standalone components |
| Back end | .NET 9, ASP.NET Core, Entity Framework Core |
| Database | PostgreSQL 16, declarative schema (no EF migrations) |
| End-to-end tests | Cypress 13 |
| Back-end tests | xUnit |
| Front-end tests | Vitest, through the Angular CLI |
| Packaging | Docker and Docker Compose |

```
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│   Angular UI    │ ---> │    .NET API     │ ---> │  PostgreSQL 16  │
│    (nom-ui)     │      │    (nom-api)    │      │                 │
│                 │      │                 │      │  declarative    │
│  nginx in prod  │      │  JWT auth       │      │  schema + seed  │
│  proxies /api   │      │  /health        │      │                 │
└─────────────────┘      └─────────────────┘      └─────────────────┘
```

Rate limiting, audit logging, and session handling are in-process in the API. The API will register
a Redis health check if a `RedisConnection` connection string is configured, but no Redis service
ships in any of the compose files.

## Repository layout

```
nom-api/                .NET solution
  Nom.Api/              Controllers, middleware, startup
  Nom.Data/             EF Core entities and configurations
  Nom.Orch/             Orchestration and business logic services
  Nom.Import/           Import, enrichment, and seeding utilities
  Nom.Api.Tests/        xUnit tests
nom-ui/                 Angular application
  src/app/              Feature areas: plan, recipe, shopping, pantry, household, admin, ...
nom-test/               Cypress end-to-end and API suites
db/                     schema.sql, seed.sql, and the apply/diff tooling
ops/                    Python tools for food-catalog cross-checking and normalization
docs/                   Architecture, requirements, development, and workflow documentation
data-analysis/          Ad hoc analysis of catalog and nutrition data
docker-compose.yml      Production stack
docker-compose.dev*.yml Development stacks
dev.sh / dev.bat        Development helper scripts
.github/workflows/      Back-end, front-end, e2e, and packaging CI
```

## Running it locally

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine with Compose —
  needed for every path, because PostgreSQL always runs in a container
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) — only for running the API natively
- [Node.js 20+](https://nodejs.org/) — only for running the UI natively

### Start the database

```bash
git clone https://github.com/armoryworks/nutrition-optimization-machine.git
cd nutrition-optimization-machine

./dev.sh start          # Linux/macOS; use dev.bat start on Windows
```

That brings up PostgreSQL on `localhost:5432` (database `nom_dev`, user `nom`, password
`dev_password`). On the first start the container applies `db/schema.sql` and `db/seed.sql`
automatically — there is nothing to migrate. `./dev.sh start-tools` adds pgAdmin on
`http://localhost:5050`.

To update an existing database after pulling schema changes:

```bash
./db/apply.sh --dry-run   # preview the delta
./db/apply.sh             # apply it
```

See [db/README.md](db/README.md) for how the declarative schema workflow works.

### Run the API and UI

Create `nom-api/Nom.Api/appsettings.Development.json` (gitignored — never commit real
credentials):

```json
{
  "ConnectionStrings": {
    "NomConnection": "Host=localhost;Database=nom_dev;Username=nom;Password=dev_password"
  }
}
```

Then, in two terminals:

```bash
cd nom-api
dotnet run --project Nom.Api/Nom.Api.csproj --urls http://localhost:8080
```

```bash
cd nom-ui
npm install
npm start
```

The UI serves on `http://localhost:4200` and proxies `/api` to `http://localhost:8080` per
`nom-ui/proxy.config.json`. The `--urls` flag above matters: `launchSettings.json` otherwise starts
the API on port 7053, which the proxy does not point at.

### Full stack in containers

`docker-compose.dev.full.yml` runs PostgreSQL, the API, and the UI together with hot reload. On
Windows, `dev.bat start-full` starts it. On Linux and macOS there is no `dev.sh` equivalent yet,
so invoke Compose directly:

```bash
docker compose -f docker-compose.dev.full.yml up -d
```

Only `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `PGADMIN_EMAIL`, and `PGADMIN_PASSWORD`
are read from the environment by that file; everything else is baked in. The UI comes up on
**http://localhost:4210** and the API on **http://localhost:8080**.

### Grant yourself admin

After registering your first account:

```bash
docker exec -i nom_postgres_dev psql -U nom -d nom_dev < _GrantInitialAdminClaims.sql
```

## Testing

```bash
cd nom-api && dotnet test        # xUnit, Nom.Api.Tests
cd nom-ui  && npm test           # Vitest via the Angular CLI
cd nom-ui  && npm run lint       # ESLint
cd nom-ui  && npm run lint:testids       # data-testid coverage ratchet
cd nom-test && npm install && npm test   # Cypress, against a running stack
```

Cypress needs `nom-test/cypress.env.json`, which is gitignored:

```json
{
  "baseUrl": "http://localhost:4200",
  "apiUrl": "http://localhost:8080"
}
```

`nom-test` also has focused runs — `npm run test:daily`, `test:admin`, `test:anon`,
`test:screenshots` — and `--headed` variants of each. See [nom-test/README.md](nom-test/README.md).
`./dev.sh test-start`, `test-run`, and `test-stop` drive the containerized test environment in
`docker-compose.test.yml`.

## Deployment

`docker-compose.yml` builds and runs the API, the nginx-served UI, and PostgreSQL 16. Copy
`.env.example` to `.env` and fill it in first:

```bash
cp .env.example .env
docker compose up -d
curl http://localhost:8080/health     # API health, honoring API_PORT
```

The variables that matter are `POSTGRES_PASSWORD`, `ALLOWED_ORIGINS` (CORS fails closed in
Production, so this must be set), `FRONTEND_URL` for links in account email, `API_PORT` and
`UI_PORT`, and the optional `EMAIL_SMTP_*` block — leaving `EMAIL_SMTP_HOST` empty disables
outbound email.

Sign-in issues ASP.NET Core Identity bearer tokens, which are protected by Data Protection
rather than a hand-configured signing key — there is no `Jwt:Key` to set. One consequence is
worth knowing before you deploy: the compose stack does not persist the Data Protection
keyring, so it is regenerated on every container start. Tokens issued before a restart stop
working, and two replicas will not accept each other's tokens. Persist the keyring (a volume
plus `PersistKeysToFileSystem`, or a shared store) before running more than one instance or
expecting sessions to survive a restart.

[docs/PRODUCTION_DEPLOYMENT.md](docs/PRODUCTION_DEPLOYMENT.md) covers the full production
procedure.

## Documentation

[docs/README.md](docs/README.md) is the index. The most useful entry points:

- [System architecture](docs/architecture/system-architecture.md) and
  [data architecture](docs/architecture/data-architecture.md)
- [Development standards](docs/DEVELOPMENT_STANDARDS.md) — naming, file separation, and conventions
  this codebase enforces
- [Component architecture](docs/architecture/component-architecture.md) and the
  [component quick reference](docs/architecture/component-quick-reference.md) for the Angular side
- [C# and Entity Framework patterns](docs/architecture/csharp-entity-framework-patterns.md) for the
  API side
- [API reference](docs/API_REFERENCE.md) and [user guide](docs/USER_GUIDE.md)
- [Troubleshooting](docs/development/troubleshooting.md)

## Contributing

Read [docs/DEVELOPMENT_STANDARDS.md](docs/DEVELOPMENT_STANDARDS.md) before your first change; it is
enforced in review. In short: one class or interface per file, `I` prefix on interfaces and `_`
prefix on abstract classes, and Angular standalone components built on the shared base components.

Two mechanical rules are enforced by tooling rather than by review:

- **Schema is declarative.** `db/schema.sql` is the source of truth and there are no EF migrations.
  After changing entities in `Nom.Data`, run `./db/sync-from-model.sh` to regenerate it;
  `--check` is the CI drift guard.
- **Every interactive element carries a `data-testid`.** `npm run lint:testids` in `nom-ui` is a
  per-file ratchet: new templates must be fully covered, and baselined templates may not regress.

Changes need tests and should keep the documentation current.

## Issues

Report bugs and request features at
[github.com/armoryworks/nutrition-optimization-machine/issues](https://github.com/armoryworks/nutrition-optimization-machine/issues).

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
