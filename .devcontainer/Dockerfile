# Use a plain Ubuntu LTS base — .NET SDK is installed via the devcontainer
# dotnet feature so the SDK stays controlled by devcontainer.json, while the
# OS stays on a feature-compatible release instead of floating to newer codenames.
FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# Skip XML doc extraction for faster NuGet restores
ENV NUGET_XMLDOC_MODE=skip
