FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Gateway.TplinkKasa/packages.lock.json", "src/HVO.Gateway.TplinkKasa/"]
COPY ["src/HVO.Gateway.TplinkKasa/HVO.Gateway.TplinkKasa.csproj", "src/HVO.Gateway.TplinkKasa/"]
COPY . .
RUN dotnet restore "src/HVO.Gateway.TplinkKasa/HVO.Gateway.TplinkKasa.csproj" --locked-mode
WORKDIR "/src/src/HVO.Gateway.TplinkKasa"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN tdnf install -y curl && tdnf clean all
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HVO.Gateway.TplinkKasa.dll"]
