# syntax=docker/dockerfile:1

# -------------------------
# Build the application
# -------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG WEBSITE_RUNTIME=linux-arm64
WORKDIR /src

# Copy solution metadata and project files to warm the restore cache
COPY ["src/global.json", "./"]
COPY ["src/NuGet.config", "./"]
COPY ["Directory.Build.props", "../"]
COPY ["Directory.Packages.props", "../"]
COPY [".LocalPackages/", "/.LocalPackages/"]
COPY ["src/HVO/HVO.csproj", "HVO/"]
COPY ["src/HVO.DataModels/HVO.DataModels.csproj", "HVO.DataModels/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "HVO.WebSite.Themes/"]
COPY ["src/HVO.WebSite/HVO.WebSite.v9/HVO.WebSite.v9.csproj", "HVO.WebSite/HVO.WebSite.v9/"]

# Restore dependencies
RUN dotnet restore "HVO.WebSite/HVO.WebSite.v9/HVO.WebSite.v9.csproj" \
    --runtime ${WEBSITE_RUNTIME} \
    --disable-parallel

# Copy the remainder of the source tree and publish the app
COPY src/ ./
WORKDIR /src/HVO.WebSite/HVO.WebSite.v9

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
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
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

# Configure ASP.NET Core and logging defaults
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning \
    TZ=UTC

# Expose the HTTP port used by the site
EXPOSE 8080

# Lightweight health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run the web site
ENTRYPOINT ["dotnet", "HVO.WebSite.v9.dll"]
