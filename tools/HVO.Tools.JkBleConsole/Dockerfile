FROM mcr.microsoft.com/dotnet/runtime:10.0.13@sha256:939555726cae4c916d212323ac3c0792996dcac2614f00e82cc40fcf059e1af3 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
COPY ["tools/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj", "tools/HVO.Tools.JkBleConsole/"]
COPY . .
RUN dotnet restore -nodeReuse:false -p:UseSharedCompilation=false "tools/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj"
WORKDIR "/src/tools/HVO.Tools.JkBleConsole"
RUN dotnet publish -nodeReuse:false -p:UseSharedCompilation=false -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HVO.Tools.JkBleConsole.dll"]
