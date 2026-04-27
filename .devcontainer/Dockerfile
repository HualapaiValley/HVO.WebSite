# Use a plain Ubuntu base — .NET SDK is installed via the devcontainer dotnet
# feature so the exact SDK version is always controlled by devcontainer.json,
# not by whatever version happens to be baked into a dotnet-specific image.
FROM mcr.microsoft.com/devcontainers/base:ubuntu

# Skip XML doc extraction for faster NuGet restores
ENV NUGET_XMLDOC_MODE=skip
