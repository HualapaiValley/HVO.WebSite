# syntax=docker/dockerfile:1

# -------------------------
# Build the application
# -------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG WEBSITE_RUNTIME=linux-arm64
WORKDIR /src

# Copy solution metadata and project files to warm the restore cache
COPY ["global.json", "./"]
COPY ["NuGet.config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["src/HVO.DataModels/HVO.DataModels.csproj", "src/HVO.DataModels/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.WebSite.v9/HVO.WebSite.v9.csproj", "src/HVO.WebSite.v9/"]

# Restore dependencies
RUN dotnet restore "src/HVO.WebSite.v9/HVO.WebSite.v9.csproj" \
    --runtime ${WEBSITE_RUNTIME} \
    --disable-parallel

# Copy the remainder of the source tree and publish the app
COPY . ./
WORKDIR /src/src/HVO.WebSite.v9

RUN dotnet publish "HVO.WebSite.v9.csproj" \
    --configuration Release \
    -p:PublishReadyToRun=false \
    --self-contained false \
    --runtime ${WEBSITE_RUNTIME} \
    --no-restore \
    --output /app/publish

# -------------------------
# Runtime image
# -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install minimal native dependencies for fonts and health checks
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    fontconfig \
    fonts-dejavu-core \
    fonts-liberation2 \
    fonts-noto-core \
    openssl && \
    rm -rf /var/lib/apt/lists/* && \
    fc-cache -f

# Build args for the self-signed cert used by local container deployments.
# ACA should terminate TLS at ingress and run the container on HTTP only.
# Local Docker Compose can still opt into HTTPS by overriding ASPNETCORE_URLS
# and the Kestrel certificate settings at runtime.
# CERT_IP: IP address for the SAN/CN (e.g. the host IP accessed by browsers)
# CERT_PASSWORD: PFX password passed to Kestrel at runtime via environment
ARG CERT_IP=192.168.0.20
ARG CERT_PASSWORD=HVODev2026

# Generate a self-signed TLS certificate so Kestrel can serve HTTPS directly.
# The IP SAN is required for browsers and Entra OIDC to trust the cert when
# accessing the site by IP address. Override CERT_IP at build time for your env.
RUN mkdir -p /https && \
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
    -keyout /https/tls.key \
    -out /https/tls.crt \
    -subj "/CN=${CERT_IP}" \
    -addext "subjectAltName=IP:${CERT_IP},DNS:localhost" 2>/dev/null && \
    openssl pkcs12 -export \
    -inkey /https/tls.key -in /https/tls.crt \
    -out /https/aspnetapp.pfx \
    -passout pass:${CERT_PASSWORD} && \
    rm /https/tls.key /https/tls.crt

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

# HTTP ingress / health checks. Local HTTPS remains available when ASPNETCORE_URLS
# is overridden at runtime.
EXPOSE 8080

# Lightweight health check — uses HTTP on 8080 (no cert validation needed)
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run the web site
ENTRYPOINT ["dotnet", "HVO.WebSite.v9.dll"]
