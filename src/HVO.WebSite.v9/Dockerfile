# syntax=docker/dockerfile:1

# -------------------------
# Build the application
# -------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# Copy solution metadata and project files to warm the restore cache
COPY ["global.json", "./"]
COPY ["NuGet.config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["src/HVO.DataModels/packages.lock.json", "src/HVO.DataModels/"]
COPY ["src/HVO.WebSite.Themes/packages.lock.json", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.WebSite.v9/packages.lock.json", "src/HVO.WebSite.v9/"]
COPY ["src/HVO.DataModels/HVO.DataModels.csproj", "src/HVO.DataModels/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.WebSite.v9/HVO.WebSite.v9.csproj", "src/HVO.WebSite.v9/"]
COPY . ./

# Restore dependencies
RUN dotnet restore "src/HVO.WebSite.v9/HVO.WebSite.v9.csproj" \
    --locked-mode \
    --disable-parallel

# Copy the remainder of the source tree and publish the app
WORKDIR /src/src/HVO.WebSite.v9

RUN dotnet publish "HVO.WebSite.v9.csproj" \
    --configuration Release \
    -p:PublishReadyToRun=false \
    --self-contained false \
    --no-restore \
    --output /app/publish

# -------------------------
# Runtime image
# -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app

# Install minimal native dependencies for fonts and health checks
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    fontconfig \
    fonts-dejavu-core \
    fonts-liberation2 \
    fonts-noto-core && \
    rm -rf /var/lib/apt/lists/* && \
    fc-cache -f

# Copy the published binaries from the build stage
COPY --from=build /app/publish .

# Configure ASP.NET Core and logging defaults.
# Default to HTTP-only for Azure Container Apps. Local container deployments can
# override ASPNETCORE_URLS and Kestrel certificate settings to enable HTTPS.
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning \
    TZ=UTC

# HTTP ingress / health checks. If local HTTPS is needed, mount a PFX at runtime
# and set the Kestrel certificate environment variables from local secrets.
EXPOSE 8080

# Lightweight health check — uses HTTP on 8080 (no cert validation needed)
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run the web site
ENTRYPOINT ["dotnet", "HVO.WebSite.v9.dll"]
