# Use a plain Ubuntu LTS base — .NET SDK is installed via the devcontainer
# dotnet feature so the SDK stays controlled by devcontainer.json, while the
# OS stays on a feature-compatible release instead of floating to newer codenames.
FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# Skip XML doc extraction for faster NuGet restores
ENV NUGET_XMLDOC_MODE=skip

# Baseline tools used by repo diagnostics, Playwright setup, OpenCode support,
# discovery probes, and review/resolution workflows. Keep these in the image so
# rebuilds do not depend on post-create timing or extension side effects.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        dnsutils \
        fontconfig \
        fonts-dejavu-core \
        fonts-open-sans \
        gnupg \
        iputils-ping \
        jq \
        less \
        netcat-openbsd \
        nodejs \
        npm \
        openssh-client \
        procps \
        python3 \
        python3-pip \
        python3-venv \
        ripgrep \
        sqlite3 \
        unzip \
        zip \
    && rm -rf /var/lib/apt/lists/*
