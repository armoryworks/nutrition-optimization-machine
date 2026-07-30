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
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install health check tools + Tesseract OCR natives and English traineddata.
# The Tesseract .NET wrapper P/Invokes versioned sonames; the symlinks map the distro
# libraries to the names the wrapper expects. Verify with a container smoke test.
RUN apt-get update && apt-get install -y --no-install-recommends curl tesseract-ocr tesseract-ocr-eng libleptonica-dev && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /usr/lib/x86_64-linux-gnu/libtesseract50.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /usr/lib/x86_64-linux-gnu/libleptonica-1.82.0.so \
    && mkdir -p /app/tessdata \
    && cp /usr/share/tesseract-ocr/*/tessdata/eng.traineddata /app/tessdata/

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