FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Staging/packages.lock.json", "src/HVO.Staging/"]
COPY ["src/HVO.WebSite.Themes/packages.lock.json", "src/HVO.WebSite.Themes/"]
COPY ["src/HVO.Hardware.DavisVantagePro2/packages.lock.json", "src/HVO.Hardware.DavisVantagePro2/"]
COPY ["src/HVO.Hardware.DavisVantagePro2/HVO.Hardware.DavisVantagePro2.csproj", "src/HVO.Hardware.DavisVantagePro2/"]
COPY ["src/HVO.Staging/HVO.Staging.csproj", "src/HVO.Staging/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "src/HVO.WebSite.Themes/"]
COPY . .
RUN dotnet restore "src/HVO.Hardware.DavisVantagePro2/HVO.Hardware.DavisVantagePro2.csproj" --locked-mode
WORKDIR "/src/src/HVO.Hardware.DavisVantagePro2"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN tdnf install -y curl && tdnf clean all
WORKDIR /app
COPY --from=publish /app/publish .
VOLUME /app/data
ENV ASPNETCORE_URLS=http://+:8080 \
    OUTBOX__DBPATH=/app/data/outbox.db
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HVO.Hardware.DavisVantagePro2.dll"]
