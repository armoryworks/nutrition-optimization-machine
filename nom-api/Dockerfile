# =================================================================
# Dockerfile for NOM .NET Backend
# =================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Nom.Api/Nom.Api.csproj", "Nom.Api/"]
COPY ["Nom.Data/Nom.Data.csproj", "Nom.Data/"]
COPY ["Nom.Orch/Nom.Orch.csproj", "Nom.Orch/"]
COPY ["Nom.Import/Nom.Import.csproj", "Nom.Import/"]
COPY ["Nom.Api.Tests/Nom.Api.Tests.csproj", "Nom.Api.Tests/"]
COPY ["nom-api.sln", "./"]

# Restore dependencies
RUN dotnet restore "nom-api.sln"

# Copy source code
COPY . .

# Build and publish
RUN dotnet build "nom-api.sln" -c Release -o /app/build
RUN dotnet publish "Nom.Api/Nom.Api.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install health check tools + Tesseract OCR natives and English traineddata.
RUN apt-get update && apt-get install -y --no-install-recommends curl tesseract-ocr tesseract-ocr-eng libleptonica-dev && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/tessdata \
    && cp /usr/share/tesseract-ocr/*/tessdata/eng.traineddata /app/tessdata/

# Copy published application
COPY --from=build /app/publish .

# The Tesseract .NET wrapper P/Invokes versioned sonames under <app>/x64; the symlinks map the
# distro libraries to the names the wrapper expects. Verify with a container smoke test.
RUN mkdir -p /app/x64 \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /app/x64/libtesseract50.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /app/x64/libleptonica-1.82.0.so \
    && ln -sf /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so

# Create non-root user WITH a writable home: without mounted OIDC certificates
# (dev/test compose) OpenIddict's development certificate is persisted under
# $HOME/.dotnet, and a homeless system user crashed at startup (E2E "nom_api_test
# is unhealthy" since v0.3.15). Production mounts real certs and never writes there.
RUN groupadd -r nom && useradd -r -g nom -m -d /home/nom nom
RUN chown -R nom:nom /app
ENV HOME=/home/nom
USER nom

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Start application
ENTRYPOINT ["dotnet", "Nom.Api.dll"]