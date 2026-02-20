# =================================================================
# Dockerfile for NOM .NET Backend
# =================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Nom.Api/Nom.Api.csproj", "Nom.Api/"]
COPY ["Nom.Data/Nom.Data.csproj", "Nom.Data/"]
COPY ["Nom.Orch/Nom.Orch.csproj", "Nom.Orch/"]
COPY ["Nom.Import/Nom.Import.csproj", "Nom.Import/"]
COPY ["nom-api.sln", "./"]

# Restore dependencies
RUN dotnet restore "nom-api.sln"

# Copy source code
COPY . .

# Build and publish
RUN dotnet build "nom-api.sln" -c Release -o /app/build
RUN dotnet publish "Nom.Api/Nom.Api.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
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