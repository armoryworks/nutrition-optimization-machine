# NOM Production Readiness Implementation Plan

## Executive Summary

This document outlines a comprehensive plan to transform the Nutrition Optimization Machine (NOM) from a development-focused application to a production-ready system. The plan addresses all missing production features identified in the comparison with Mealie, including deployment infrastructure, CI/CD automation, testing, security, monitoring, and documentation.

## Current State Analysis

### What NOM Has (Strengths)

- ✅ Advanced AI/ML features (recipe suggestions, AI-powered recommendations)
- ✅ Sophisticated nutrition tracking and restriction management
- ✅ Content curation and moderation systems
- ✅ Advanced privacy and GDPR compliance
- ✅ Complex user onboarding workflows
- ✅ Advanced component architecture patterns
- ✅ Recipe scraping and web integration

### What NOM Is Missing (Production Gaps)

- ❌ Production deployment infrastructure (Docker, containerization)
- ❌ CI/CD automation and testing pipelines
- ❌ Production-grade security and monitoring
- ❌ Comprehensive testing infrastructure
- ❌ Production documentation and deployment guides
- ❌ Health checks and monitoring
- ❌ Multi-environment configuration management
- ❌ Backup and recovery strategies

## Implementation Phases

### Phase 1: Infrastructure & Deployment (Weeks 1-3)

### Phase 2: CI/CD & Testing (Weeks 4-6)

### Phase 3: Security & Monitoring (Weeks 7-8)

### Phase 4: Documentation & Production Setup (Weeks 9-10)

---

## Phase 1: Infrastructure & Deployment

### 1.1 Docker Containerization

#### 1.1.1 Multi-Stage Dockerfile

Create `nom-api/Dockerfile`:

```dockerfile
# =================================================================
# Dockerfile for NOM .NET Backend
# =================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Nom.Api/Nom.Api.csproj", "Nom.Api/"]
COPY ["Nom.Data/Nom.Data.csproj", "Nom.Data/"]
COPY ["Nom.Orch/Nom.Orch.csproj", "Nom.Orch/"]
COPY ["nom-api.sln", "./"]

# Restore dependencies
RUN dotnet restore "nom-api.sln"

# Copy source code
COPY . .

# Build and publish
RUN dotnet build "nom-api.sln" -c Release -o /app/build
RUN dotnet publish "nom-api.sln" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install health check tools
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create non-root user
RUN groupadd -r nom && useradd -r -g nom nom
RUN chown -R nom:nom /app
USER nom

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Start application
ENTRYPOINT ["dotnet", "Nom.Api.dll"]
```

#### 1.1.2 Angular Frontend Dockerfile

Create `nom-ui/Dockerfile`:

```dockerfile
# =================================================================
# Dockerfile for NOM Angular Frontend
# =================================================================

# Stage 1: Build
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files
COPY package*.json ./
COPY angular.json ./
COPY tsconfig*.json ./

# Install dependencies
RUN npm ci --only=production

# Copy source code
COPY src ./src/
COPY public ./public/

# Build for production
RUN npm run build -- --configuration production

# Stage 2: Production
FROM nginx:alpine
COPY --from=build /app/dist/nom-ui /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

### 1.2 Docker Compose Production Setup

Create `docker-compose.yml`:

```yaml
version: "3.8"

services:
  # NOM Backend API
  nom-api:
    container_name: nom_api
    build:
      context: ./nom-api
      dockerfile: Dockerfile
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://0.0.0.0:8080
      - ConnectionStrings__NomConnection=Host=postgres;Database=nom;Username=nom;Password=${POSTGRES_PASSWORD}
      - JWT__SecretKey=${JWT_SECRET_KEY}
      - JWT__Issuer=NOMApi
      - JWT__Audience=NOMAngular
      - JWT__ExpirationMinutes=1440
      - AllowedOrigins=${ALLOWED_ORIGINS}
    ports:
      - "${API_PORT:-8080}:8080"
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - nom-network
    volumes:
      - nom_data:/app/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

  # NOM Frontend
  nom-ui:
    container_name: nom_ui
    build:
      context: ./nom-ui
      dockerfile: Dockerfile
    restart: unless-stopped
    ports:
      - "${UI_PORT:-80}:80"
    depends_on:
      - nom-api
    networks:
      - nom-network

  # PostgreSQL Database
  postgres:
    container_name: nom_postgres
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-nom}
      POSTGRES_USER: ${POSTGRES_USER:-nom}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    networks:
      - nom-network
    healthcheck:
      test:
        [
          "CMD-SHELL",
          "pg_isready -U ${POSTGRES_USER:-nom} -d ${POSTGRES_DB:-nom}",
        ]
      interval: 10s
      timeout: 5s
      retries: 5

  # Redis Cache (for session management)
  redis:
    container_name: nom_redis
    image: redis:7-alpine
    restart: unless-stopped
    command: redis-server --appendonly yes
    volumes:
      - redis_data:/data
    networks:
      - nom-network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

volumes:
  nom_data:
    driver: local
  postgres_data:
    driver: local
  redis_data:
    driver: local

networks:
  nom-network:
    driver: bridge
```

### 1.3 Environment Configuration

Create `.env.example`:

```bash
# Database Configuration
POSTGRES_DB=nom
POSTGRES_USER=nom
POSTGRES_PASSWORD=change_this_in_production
POSTGRES_HOST=postgres
POSTGRES_PORT=5432

# JWT Configuration
JWT_SECRET_KEY=change_this_super_secret_jwt_key_in_production
JWT_ISSUER=NOMApi
JWT_AUDIENCE=NOMAngular
JWT_EXPIRATION_MINUTES=1440

# Application Configuration
ASPNETCORE_ENVIRONMENT=Production
API_PORT=8080
UI_PORT=80
ALLOWED_ORIGINS=http://localhost:80,https://yourdomain.com

# Redis Configuration
REDIS_CONNECTION_STRING=redis:6379

# Logging Configuration
LOG_LEVEL=Information
LOG_FILE_PATH=/app/logs

# Health Check Configuration
HEALTH_CHECK_INTERVAL=30
HEALTH_CHECK_TIMEOUT=10
HEALTH_CHECK_RETRIES=3
```

### 1.4 Nginx Configuration

Create `nom-ui/nginx.conf`:

```nginx
events {
    worker_connections 1024;
}

http {
    include       /etc/nginx/mime.types;
    default_type  application/octet-stream;

    # Logging
    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for"';

    access_log /var/log/nginx/access.log main;
    error_log /var/log/nginx/error.log warn;

    # Gzip compression
    gzip on;
    gzip_vary on;
    gzip_min_length 1024;
    gzip_proxied any;
    gzip_comp_level 6;
    gzip_types
        text/plain
        text/css
        text/xml
        text/javascript
        application/json
        application/javascript
        application/xml+rss
        application/atom+xml
        image/svg+xml;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=api:10m rate=10r/s;
    limit_req_zone $binary_remote_addr zone=ui:10m rate=100r/s;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    server {
        listen 80;
        server_name localhost;
        root /usr/share/nginx/html;
        index index.html;

        # UI rate limiting
        limit_req zone=ui burst=20 nodelay;

        # Security
        location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg)$ {
            expires 1y;
            add_header Cache-Control "public, immutable";
        }

        # Angular routing
        location / {
            try_files $uri $uri/ /index.html;
        }

        # API proxy
        location /api/ {
            limit_req zone=api burst=5 nodelay;
            proxy_pass http://nom-api:8080;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }

        # Health check endpoint
        location /health {
            access_log off;
            return 200 "healthy\n";
            add_header Content-Type text/plain;
        }
    }
}
```

---

## Phase 2: CI/CD & Testing

### 2.1 GitHub Actions Workflows

#### 2.1.1 Backend Testing Workflow

Create `.github/workflows/test-backend.yml`:

```yaml
name: Test Backend (.NET)

on:
  push:
    branches: [main, develop]
    paths: ["nom-api/**"]
  pull_request:
    branches: [main, develop]
    paths: ["nom-api/**"]

jobs:
  test:
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_PASSWORD: postgres
          POSTGRES_DB: nom_test
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Restore dependencies
        run: dotnet restore nom-api/nom-api.sln

      - name: Build
        run: dotnet build nom-api/nom-api.sln --no-restore --configuration Release

      - name: Test
        run: dotnet test nom-api/nom-api.sln --no-build --verbosity normal --configuration Release --collect:"XPlat Code Coverage"

      - name: Upload coverage reports
        uses: codecov/codecov-action@v3
        with:
          file: ./nom-api/**/coverage.cobertura.xml
          flags: backend
          name: backend-coverage
```

#### 2.1.2 Frontend Testing Workflow

Create `.github/workflows/test-frontend.yml`:

```yaml
name: Test Frontend (Angular)

on:
  push:
    branches: [main, develop]
    paths: ["nom-ui/**"]
  pull_request:
    branches: [main, develop]
    paths: ["nom-ui/**"]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: "20"
          cache: "npm"
          cache-dependency-path: nom-ui/package-lock.json

      - name: Install dependencies
        run: |
          cd nom-ui
          npm ci

      - name: Run linting
        run: |
          cd nom-ui
          npm run lint

      - name: Run unit tests
        run: |
          cd nom-ui
          npm run test:ci

      - name: Run E2E tests
        run: |
          cd nom-ui
          npm run e2e:ci

      - name: Build for production
        run: |
          cd nom-ui
          npm run build -- --configuration production
```

#### 2.1.3 Build and Package Workflow

Create `.github/workflows/build-package.yml`:

```yaml
name: Build and Package

on:
  push:
    tags: ["v*"]
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKER_USERNAME }}
          password: ${{ secrets.DOCKER_PASSWORD }}

      - name: Build and push Backend
        uses: docker/build-push-action@v5
        with:
          context: ./nom-api
          push: true
          tags: |
            your-org/nom-api:latest
            your-org/nom-api:${{ github.ref_name }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Build and push Frontend
        uses: docker/build-push-action@v5
        with:
          context: ./nom-ui
          push: true
          tags: |
            your-org/nom-ui:latest
            your-org/nom-ui:${{ github.ref_name }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Create Release
        uses: actions/create-release@v1
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        with:
          tag_name: ${{ github.ref }}
          release_name: Release ${{ github.ref }}
          draft: false
          prerelease: false
```

### 2.2 Testing Infrastructure

#### 2.2.1 Backend Testing Enhancement

Update `nom-api/Nom.Api.Tests/Nom.Api.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.0" />
    <PackageReference Include="Testcontainers" Version="3.6.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.6.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Nom.Api\Nom.Api.csproj" />
    <ProjectReference Include="..\Nom.Data\Nom.Data.csproj" />
    <ProjectReference Include="..\Nom.Orch\Nom.Orch.csproj" />
  </ItemGroup>

</Project>
```

#### 2.2.2 Frontend Testing Enhancement

Update `nom-ui/package.json` testing scripts:

```json
{
  "scripts": {
    "test": "ng test",
    "test:ci": "ng test --watch=false --browsers=ChromeHeadless --code-coverage",
    "test:coverage": "ng test --watch=false --browsers=ChromeHeadless --code-coverage --coverage-reporters=html",
    "e2e": "ng e2e",
    "e2e:ci": "ng e2e --configuration=production",
    "lint": "ng lint",
    "lint:fix": "ng lint --fix"
  },
  "devDependencies": {
    "@types/jasmine": "~5.1.0",
    "jasmine-core": "~5.1.0",
    "karma": "~6.4.0",
    "karma-chrome-launcher": "~3.2.0",
    "karma-coverage": "~2.2.0",
    "karma-jasmine": "~5.1.0",
    "karma-jasmine-html-reporter": "~2.1.0",
    "cypress": "^13.6.0",
    "@cypress/schematic": "^2.5.0"
  }
}
```

---

## Phase 3: Security & Monitoring

### 3.1 Health Check Endpoints

#### 3.1.1 Backend Health Controller

Create `nom-api/Nom.Api/Controllers/HealthController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(HealthCheckService healthCheckService, ILogger<HealthController> logger)
        {
            _healthCheckService = healthCheckService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var report = await _healthCheckService.CheckHealthAsync();

            var result = new
            {
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                })
            };

            return report.Status == HealthStatus.Healthy ? Ok(result) : StatusCode(503, result);
        }

        [HttpGet("ready")]
        public IActionResult Ready()
        {
            return Ok(new { status = "ready", timestamp = DateTime.UtcNow });
        }

        [HttpGet("live")]
        public IActionResult Live()
        {
            return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
        }
    }
}
```

#### 3.1.2 Health Check Services

Update `nom-api/Nom.Api/Program.cs`:

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("Database")
    .AddRedis(builder.Configuration.GetConnectionString("RedisConnection"), "Redis")
    .AddUrlGroup(new Uri("https://httpbin.org/status/200"), "External API");

// Add health check UI in development
if (app.Environment.IsDevelopment())
{
    app.MapHealthChecksUI();
}

// Map health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});
```

### 3.2 Security Enhancements

#### 3.2.1 Security Headers Middleware

Create `nom-api/Nom.Api/Middleware/SecurityHeadersMiddleware.cs`:

```csharp
namespace Nom.Api.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SecurityHeadersMiddleware> _logger;

        public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Security headers
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=(), camera=()");

            // Content Security Policy
            context.Response.Headers.Add("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' https:; " +
                "connect-src 'self' https:; " +
                "frame-ancestors 'none';");

            await _next(context);
        }
    }
}
```

#### 3.2.2 Rate Limiting

Update `nom-api/Nom.Api/Program.cs`:

```csharp
// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 50,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// Apply rate limiting to API endpoints
app.UseRateLimiter();
```

### 3.3 Logging and Monitoring

#### 3.3.1 Structured Logging

Update `nom-api/Nom.Api/Program.cs`:

```csharp
// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add Serilog for structured logging
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/nom-api-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Seq("http://localhost:5341"));
```

#### 3.3.2 Application Insights Integration

Add to `nom-api/Nom.Api/Program.cs`:

```csharp
// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddApplicationInsightsTelemetryWorkerService();
```

---

## Phase 4: Documentation & Production Setup

### 4.1 Production Deployment Guide

Create `docs/PRODUCTION_DEPLOYMENT.md`:

````markdown
# NOM Production Deployment Guide

## Prerequisites

- Docker and Docker Compose installed
- PostgreSQL 16+ (or use included container)
- Redis 7+ (or use included container)
- Domain name and SSL certificate (for production)

## Quick Start

1. **Clone and configure:**
   ```bash
   git clone https://github.com/your-org/nutrition-optimization-machine.git
   cd nutrition-optimization-machine
   cp .env.example .env
   # Edit .env with your production values
   ```
````

2. **Deploy:**

   ```bash
   docker-compose up -d
   ```

3. **Access:**
   - Frontend: http://your-domain.com
   - API: http://your-domain.com/api
   - Health: http://your-domain.com/health

## Production Configuration

### Environment Variables

| Variable                 | Description       | Example                  |
| ------------------------ | ----------------- | ------------------------ |
| `POSTGRES_PASSWORD`      | Database password | `secure_password_123`    |
| `JWT_SECRET_KEY`         | JWT signing key   | `64_char_random_string`  |
| `ALLOWED_ORIGINS`        | CORS origins      | `https://yourdomain.com` |
| `ASPNETCORE_ENVIRONMENT` | Environment       | `Production`             |

### SSL/HTTPS Setup

1. **Obtain SSL certificate** (Let's Encrypt recommended)
2. **Update nginx.conf** with SSL configuration
3. **Redirect HTTP to HTTPS**

### Database Backup

```bash
# Create backup script
docker exec nom_postgres pg_dump -U nom nom > backup_$(date +%Y%m%d_%H%M%S).sql

# Automated backup with cron
0 2 * * * docker exec nom_postgres pg_dump -U nom nom > /backups/nom_$(date +\%Y\%m\%d).sql
```

## Monitoring and Maintenance

### Health Checks

- **Application Health**: `/health`
- **Database Health**: `/health` (includes DB check)
- **Redis Health**: `/health` (includes Redis check)

### Logs

```bash
# View application logs
docker-compose logs -f nom-api

# View nginx logs
docker-compose logs -f nom-ui

# View database logs
docker-compose logs -f postgres
```

### Updates

```bash
# Pull latest changes
git pull origin main

# Rebuild and restart
docker-compose down
docker-compose build --no-cache
docker-compose up -d
```

## Troubleshooting

### Common Issues

1. **Database connection failed**

   - Check PostgreSQL container status
   - Verify connection string in .env

2. **Frontend not loading**

   - Check nginx container status
   - Verify nginx configuration

3. **API endpoints failing**
   - Check API container status
   - Verify health check endpoint

### Performance Tuning

1. **Database optimization**

   - Enable connection pooling
   - Configure appropriate memory limits

2. **Caching**

   - Redis for session storage
   - Response caching for static content

3. **Load balancing**
   - Multiple API instances
   - Nginx upstream configuration

````

### 4.2 API Documentation

Create `docs/API_REFERENCE.md`:

```markdown
# NOM API Reference

## Authentication

All API endpoints require JWT authentication unless specified otherwise.

### Login

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "password123"
}
````

### JWT Token

Include the JWT token in the Authorization header:

```http
Authorization: Bearer <your_jwt_token>
```

## Core Endpoints

### Recipes

#### Get Recipes

```http
GET /api/recipes?page=1&perPage=20&search=chicken
```

#### Create Recipe

```http
POST /api/recipes
Content-Type: application/json

{
  "name": "Chicken Parmesan",
  "description": "Classic Italian dish",
  "ingredients": [...],
  "instructions": [...]
}
```

### AI Features

#### Get Recipe Suggestions

```http
GET /api/recipe-suggestions/suggestions?ingredientIds=1,2,3
```

#### Generate AI Suggestions

```http
POST /api/recipe-suggestions/ai-suggestions
Content-Type: application/json

{
  "description": "Quick dinner with chicken and vegetables",
  "dietaryRestrictions": ["vegetarian"],
  "cookingTime": 30
}
```

## Health Endpoints

### Application Health

```http
GET /health
```

### Database Health

```http
GET /health
# Includes database connectivity check
```

## Rate Limiting

- **General API**: 100 requests per minute
- **Recipe endpoints**: 50 requests per minute
- **AI endpoints**: 20 requests per minute

## Error Handling

All errors follow this format:

```json
{
  "error": "Error message",
  "details": "Additional details",
  "timestamp": "2024-01-01T00:00:00Z",
  "requestId": "unique-request-id"
}
```

## Status Codes

- `200` - Success
- `201` - Created
- `400` - Bad Request
- `401` - Unauthorized
- `403` - Forbidden
- `404` - Not Found
- `429` - Too Many Requests
- `500` - Internal Server Error

````

### 4.3 User Documentation

Create `docs/USER_GUIDE.md`:

```markdown
# NOM User Guide

## Getting Started

### 1. Account Creation

1. Visit the registration page
2. Enter your email and create a password
3. Verify your email address
4. Complete your profile setup

### 2. Onboarding Process

The onboarding process helps us personalize your experience:

1. **Health Profile**: Enter basic health information
2. **Dietary Restrictions**: Specify any dietary limitations
3. **Nutrition Goals**: Set your nutrition objectives
4. **Preferences**: Choose your favorite cuisines and ingredients

### 3. First Recipe

1. Browse the recipe library
2. Use AI-powered search to find recipes
3. Save recipes to your favorites
4. Create your first meal plan

## Core Features

### Recipe Management

#### Finding Recipes
- **Search**: Use keywords, ingredients, or dietary restrictions
- **AI Suggestions**: Get personalized recipe recommendations
- **Browse**: Explore by category, cuisine, or difficulty

#### Creating Recipes
1. Click "Create Recipe"
2. Enter basic information (name, description, time)
3. Add ingredients with quantities
4. Write step-by-step instructions
5. Add nutritional information
6. Submit for curation

#### Recipe Curation
- All new recipes go through quality review
- Community moderators review submissions
- Feedback provided for rejected recipes
- Approved recipes added to public library

### Meal Planning

#### Creating Plans
1. Select recipes for each meal
2. Set serving sizes
3. Choose dates and times
4. Generate shopping lists automatically

#### Smart Planning
- AI suggests recipes based on preferences
- Considers dietary restrictions
- Balances nutrition across meals
- Accounts for available ingredients

### Nutrition Tracking

#### Personal Dashboard
- Daily nutrition summary
- Progress toward goals
- Meal history and trends
- Personalized recommendations

#### Dietary Restrictions
- Set multiple restriction types
- Get alerts for incompatible ingredients
- Find suitable alternatives
- Track compliance over time

## Advanced Features

### AI-Powered Features

#### Recipe Suggestions
- Based on available ingredients
- Considers dietary restrictions
- Adapts to cooking skill level
- Learns from your preferences

#### Smart Search
- Natural language queries
- Ingredient substitution suggestions
- Nutritional analysis
- Allergy warnings

### Content Curation

#### Community Moderation
- Submit recipes for review
- Vote on community submissions
- Report inappropriate content
- Earn reputation points

#### Quality Standards
- Recipe accuracy verification
- Nutritional information validation
- Photo quality requirements
- Instruction clarity review

## Privacy and Security

### Data Protection
- GDPR compliant
- Encrypted data storage
- Regular security audits
- User consent management

### Privacy Controls
- Control data sharing
- Manage consent preferences
- Request data export
- Account deletion options

## Troubleshooting

### Common Issues

1. **Can't log in**
   - Check email and password
   - Verify email confirmation
   - Reset password if needed

2. **Recipe not loading**
   - Check internet connection
   - Clear browser cache
   - Try refreshing the page

3. **AI features not working**
   - Ensure you're logged in
   - Check feature availability
   - Contact support if persistent

### Getting Help

- **Documentation**: Check this guide first
- **Community**: Join our Discord server
- **Support**: Email support@nom.com
- **Feedback**: Use in-app feedback form
````

---

## Implementation Timeline

### Week 1-3: Infrastructure & Deployment

- [ ] Docker containerization
- [ ] Docker Compose setup
- [ ] Environment configuration
- [ ] Nginx configuration
- [ ] Basic health checks

### Week 4-6: CI/CD & Testing

- [ ] GitHub Actions workflows
- [ ] Testing infrastructure
- [ ] Code coverage setup
- [ ] E2E testing
- [ ] Security scanning

### Week 7-8: Security & Monitoring

- [ ] Security headers
- [ ] Rate limiting
- [ ] Structured logging
- [ ] Application monitoring
- [ ] Performance optimization

### Week 9-10: Documentation & Production

- [ ] Production deployment guide
- [ ] API documentation
- [ ] User documentation
- [ ] Production testing
- [ ] Go-live preparation

## Success Metrics

### Technical Metrics

- [ ] 99.9% uptime
- [ ] <200ms API response time
- [ ] 100% test coverage
- [ ] Zero security vulnerabilities
- [ ] Automated deployment pipeline

### User Experience Metrics

- [ ] <3 second page load time
- [ ] 95% feature completion
- [ ] Comprehensive documentation
- [ ] Intuitive user interface
- [ ] Responsive design

## Risk Mitigation

### Technical Risks

- **Database migration issues**: Comprehensive testing and rollback plans
- **Performance degradation**: Load testing and monitoring
- **Security vulnerabilities**: Regular security audits and updates

### Business Risks

- **User adoption**: Gradual rollout and feedback collection
- **Data migration**: Backup strategies and validation
- **Service disruption**: Blue-green deployment strategy

## Conclusion

This implementation plan will transform NOM from a development-focused application to a production-ready system. The phased approach ensures minimal disruption while building robust infrastructure, comprehensive testing, and production-grade security.

Upon completion, NOM will have:

- Enterprise-grade deployment infrastructure
- Automated CI/CD pipelines
- Comprehensive testing and monitoring
- Production-ready security and performance
- Complete user and technical documentation

The result will be a system that rivals Mealie's production readiness while maintaining NOM's advanced feature set and technical sophistication.
