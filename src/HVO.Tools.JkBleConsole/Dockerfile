FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["src/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj", "src/HVO.Tools.JkBleConsole/"]
COPY . .
RUN dotnet restore "src/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj"
WORKDIR "/src/src/HVO.Tools.JkBleConsole"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HVO.Tools.JkBleConsole.dll"]
