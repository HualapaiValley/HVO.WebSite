FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Edge.Contracts/packages.lock.json", "src/HVO.Edge.Contracts/"]
COPY ["src/HVO.Edge.HomeAssistant.Mqtt/packages.lock.json", "src/HVO.Edge.HomeAssistant.Mqtt/"]
COPY ["src/HVO.Edge.Hosting/packages.lock.json", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Edge.Outbox/packages.lock.json", "src/HVO.Edge.Outbox/"]
COPY ["src/HVO.Hardware.VictronSmartShunt/packages.lock.json", "src/HVO.Hardware.VictronSmartShunt/"]
COPY ["src/HVO.Edge.Contracts/HVO.Edge.Contracts.csproj", "src/HVO.Edge.Contracts/"]
COPY ["src/HVO.Edge.HomeAssistant.Mqtt/HVO.Edge.HomeAssistant.Mqtt.csproj", "src/HVO.Edge.HomeAssistant.Mqtt/"]
COPY ["src/HVO.Edge.Hosting/HVO.Edge.Hosting.csproj", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Edge.Outbox/HVO.Edge.Outbox.csproj", "src/HVO.Edge.Outbox/"]
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
    HVO_EDGE_CONFIG_FILE=/app/config/gateway.json
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD curl --fail http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "HVO.Hardware.VictronSmartShunt.dll"]
