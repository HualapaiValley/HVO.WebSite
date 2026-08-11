FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Edge.Contracts/packages.lock.json", "src/HVO.Edge.Contracts/"]
COPY ["src/HVO.Edge.Hosting/packages.lock.json", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Edge.HomeAssistant.Mqtt/packages.lock.json", "src/HVO.Edge.HomeAssistant.Mqtt/"]
COPY ["src/HVO.Edge.Outbox/packages.lock.json", "src/HVO.Edge.Outbox/"]
COPY ["src/HVO.Hardware.Eg4/packages.lock.json", "src/HVO.Hardware.Eg4/"]
COPY ["src/HVO.Edge.Contracts/HVO.Edge.Contracts.csproj", "src/HVO.Edge.Contracts/"]
COPY ["src/HVO.Edge.Hosting/HVO.Edge.Hosting.csproj", "src/HVO.Edge.Hosting/"]
COPY ["src/HVO.Edge.HomeAssistant.Mqtt/HVO.Edge.HomeAssistant.Mqtt.csproj", "src/HVO.Edge.HomeAssistant.Mqtt/"]
COPY ["src/HVO.Edge.Outbox/HVO.Edge.Outbox.csproj", "src/HVO.Edge.Outbox/"]
COPY ["src/HVO.Hardware.Eg4/HVO.Hardware.Eg4.csproj", "src/HVO.Hardware.Eg4/"]
COPY . .
RUN dotnet restore "src/HVO.Hardware.Eg4/HVO.Hardware.Eg4.csproj" --locked-mode
WORKDIR "/src/src/HVO.Hardware.Eg4"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
VOLUME /app/data
ENV ASPNETCORE_URLS=http://+:8080 \
    OUTBOX__DATABASEPATH=/app/data/outbox.db
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HVO.Hardware.Eg4.dll"]
