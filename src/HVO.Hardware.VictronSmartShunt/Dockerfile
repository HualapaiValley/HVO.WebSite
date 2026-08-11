FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.WebSite.Themes/packages.lock.json", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.Edge.Hosting/packages.lock.json", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Hardware.VictronSmartShunt/packages.lock.json", "src/HVO.Hardware.VictronSmartShunt/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.Edge.Hosting/HVO.Edge.Hosting.csproj", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Hardware.VictronSmartShunt/HVO.Hardware.VictronSmartShunt.csproj", "src/HVO.Hardware.VictronSmartShunt/"]
COPY . .
RUN dotnet restore "src/HVO.Hardware.VictronSmartShunt/HVO.Hardware.VictronSmartShunt.csproj" --locked-mode
WORKDIR "/src/src/HVO.Hardware.VictronSmartShunt"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
VOLUME /app/data
ENV ASPNETCORE_URLS=http://+:8080 \
    OUTBOX__DBPATH=/app/data/outbox.db
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HVO.Hardware.VictronSmartShunt.dll"]
