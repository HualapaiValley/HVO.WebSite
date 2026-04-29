FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Hardware.DavisVantagePro2/HVO.Hardware.DavisVantagePro2.csproj", "src/HVO.Hardware.DavisVantagePro2/"]
RUN dotnet restore "src/HVO.Hardware.DavisVantagePro2/HVO.Hardware.DavisVantagePro2.csproj"
COPY . .
WORKDIR "/src/src/HVO.Hardware.DavisVantagePro2"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
VOLUME /app/data
ENV ASPNETCORE_URLS=http://+:8080 \
    OUTBOX__DBPATH=/app/data/outbox.db
ENTRYPOINT ["dotnet", "HVO.Hardware.DavisVantagePro2.dll"]
