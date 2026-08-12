#!/bin/bash
set -e
set -o pipefail

echo "Running HVO.WebSite post-create setup..."

# ─────────────────────────────────────────────────────────────────────
# Load shared devcontainer base script (common to all RoySalisbury repos)
# Gist: https://gist.github.com/RoySalisbury/bceb71a9120e4d393b68308a03399ca5
# Provides: dc_bootstrap_env, dc_setup_dotnet, dc_install_cli,
#           dc_setup_docker, dc_setup_ssh, dc_create_contexts, dc_scan_hosts
# ─────────────────────────────────────────────────────────────────────
BASE_GIST="bceb71a9120e4d393b68308a03399ca5"

_load_base_script() {
	local token="${GH_TOKEN:-${GITHUB_TOKEN:-}}"
	if [ -z "$token" ] && command -v gh >/dev/null 2>&1; then
		token=$(gh auth token 2>/dev/null) || true
	fi
	if [ -z "$token" ] && command -v git >/dev/null 2>&1; then
		token=$(printf 'protocol=https\nhost=github.com\n' \
			| GIT_TERMINAL_PROMPT=0 git credential fill 2>/dev/null \
			| grep '^password=' | head -1 | cut -d= -f2-) || true
	fi
	if command -v gh >/dev/null 2>&1 && [ -n "$token" ]; then
		GH_TOKEN="$token" gh gist view "$BASE_GIST" --raw --filename devcontainer-base.sh 2>/dev/null && return 0
	fi
	curl -fsSL "https://gist.githubusercontent.com/RoySalisbury/${BASE_GIST}/raw/devcontainer-base.sh" 2>/dev/null && return 0
	return 1
}

BASE_SCRIPT_FILE="$(mktemp)"
trap 'rm -f "$BASE_SCRIPT_FILE"' EXIT
if _load_base_script > "$BASE_SCRIPT_FILE" && \
		grep -Eq '^[[:space:]]*(function[[:space:]]+)?dc_bootstrap_env[[:space:]]*\(\)' "$BASE_SCRIPT_FILE"; then
	# shellcheck disable=SC1090
	. "$BASE_SCRIPT_FILE"
else
	echo "⚠  Could not load devcontainer-base.sh from gist (or missing expected functions). Continuing without shared setup."
fi

# ─────────────────────────────────────────────────────────────────────
# Resolve and export a GitHub token so base script functions can use it
# ─────────────────────────────────────────────────────────────────────
if [ -z "${GH_TOKEN:-}" ] && [ -z "${GITHUB_TOKEN:-}" ]; then
	_resolved_token=""
	if command -v gh >/dev/null 2>&1; then
		_resolved_token=$(gh auth token 2>/dev/null) || true
	fi
	if [ -z "$_resolved_token" ] && command -v git >/dev/null 2>&1; then
		_resolved_token=$(printf 'protocol=https\nhost=github.com\n' \
			| GIT_TERMINAL_PROMPT=0 git credential fill 2>/dev/null \
			| grep '^password=' | head -1 | cut -d= -f2-) || true
	fi
	if [ -n "$_resolved_token" ]; then
		export GH_TOKEN="$_resolved_token"
	fi
	unset _resolved_token
fi

# ─────────────────────────────────────────────────────────────────────
# Shared setup — .NET, CLI tools, Docker, SSH, .env bootstrap
# ─────────────────────────────────────────────────────────────────────

_ensure_user_writable_dir() {
	local dir="$1"
	local mode="${2:-}"
	mkdir -p "$dir"
	if [ ! -w "$dir" ] || [ ! -O "$dir" ]; then
		sudo chown -R "$(id -u)":"$(id -g)" "$dir" 2>/dev/null || true
	fi
	if [ -n "$mode" ]; then
		chmod "$mode" "$dir" 2>/dev/null || true
	fi
}

# Named volumes can be created as root-owned mount points. Normalize ownership
# before the shared setup restores SSH keys or writes CLI configuration.
_ensure_user_writable_dir "$HOME/.ssh" 700
_ensure_user_writable_dir "$HOME/.docker"
_ensure_user_writable_dir "$HOME/.config/gh"
_ensure_user_writable_dir "$HOME/.azure"
_ensure_user_writable_dir "$HOME/.dotnet/tools"
_ensure_user_writable_dir "$HOME/.opencode"
_ensure_user_writable_dir "$HOME/.aspnet/DataProtection-Keys"

# [CUSTOMIZE] .env gist ID for this repo
ENV_GIST="f343db002d980ebe5fcc51413b0b7227"

if type dc_bootstrap_env >/dev/null 2>&1; then
	dc_bootstrap_env "$ENV_GIST" "/workspaces/HVO.WebSite"
	dc_setup_dotnet
	dc_install_cli
	dc_setup_docker
	dc_setup_ssh
else
	echo "⚠  Base script not loaded — running inline fallback..."
	sudo chown -R vscode:vscode /home/vscode/.dotnet || true
	dotnet --info
	sudo apt-get update -y && sudo apt-get install -y jq ripgrep sqlite3 python3 nodejs npm openssh-client || true
	if getent group docker >/dev/null 2>&1; then sudo usermod -aG docker vscode || true; fi
	if [ -S /var/run/docker.sock ]; then sudo chmod 666 /var/run/docker.sock || true; fi
fi

# Baseline packages should already be present from the Dockerfile. Keep this
# idempotent fallback so older cached images still recover during post-create.
missing_packages=()
command -v sqlite3 >/dev/null 2>&1 || missing_packages+=(sqlite3)
command -v python3 >/dev/null 2>&1 || missing_packages+=(python3 python3-pip python3-venv)
command -v node >/dev/null 2>&1 || missing_packages+=(nodejs)
command -v npm >/dev/null 2>&1 || missing_packages+=(npm)
command -v rg >/dev/null 2>&1 || missing_packages+=(ripgrep)
command -v jq >/dev/null 2>&1 || missing_packages+=(jq)
command -v ssh >/dev/null 2>&1 || missing_packages+=(openssh-client)
if (( ${#missing_packages[@]} > 0 )); then
	echo "Installing missing baseline packages: ${missing_packages[*]}"
	sudo apt-get update -y
	sudo apt-get install -y --no-install-recommends "${missing_packages[@]}"
fi

if command -v az >/dev/null 2>&1; then
	az config set extension.use_dynamic_install=yes_without_prompt >/dev/null 2>&1 || true
	az extension add --name log-analytics --only-show-errors >/dev/null 2>&1 || \
		az extension update --name log-analytics --only-show-errors >/dev/null 2>&1 || true
else
	echo "Warning: Azure CLI not found — rebuild the devcontainer to install the azure-cli feature."
fi

# NuGet uses ~/.local/share/NuGet for vulnerability metadata by default. Ensure
# that path and the explicit cache paths are writable after restored volumes or
# VS Code create nested mount parent directories.
nuget_local_home="$HOME/.local"
if [ ! -d "$nuget_local_home" ]; then
	mkdir -p "$nuget_local_home"
fi
if [ ! -w "$nuget_local_home" ] || [ ! -O "$nuget_local_home" ]; then
	sudo chown -R "$(id -u)":"$(id -g)" "$nuget_local_home" 2>/dev/null || true
fi

nuget_share_home="$nuget_local_home/share"
nuget_data_home="$HOME/.local/share/NuGet"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$HOME/.nuget/v3-cache}"
export NUGET_PLUGINS_CACHE_PATH="${NUGET_PLUGINS_CACHE_PATH:-$HOME/.nuget/plugins-cache}"
mkdir -p "$nuget_share_home" "$nuget_data_home" "$NUGET_HTTP_CACHE_PATH" "$NUGET_PLUGINS_CACHE_PATH" "/tmp/NuGetScratch$(id -un)"
sudo chown -R "$(id -u)":"$(id -g)" "$HOME/.local" "$NUGET_HTTP_CACHE_PATH" "$NUGET_PLUGINS_CACHE_PATH" "/tmp/NuGetScratch$(id -un)" 2>/dev/null || true
chmod -R u+rwX "$nuget_data_home" "$NUGET_HTTP_CACHE_PATH" "$NUGET_PLUGINS_CACHE_PATH" "/tmp/NuGetScratch$(id -un)" 2>/dev/null || true

# Install fonts (not in base script — needed for the website's PDF/chart rendering)
sudo apt-get install -y --no-install-recommends \
	fontconfig fonts-dejavu-core fonts-open-sans 2>/dev/null || true
sudo fc-cache -f 2>/dev/null || true

# Install Bluetooth development packages
echo "Installing Bluetooth development packages..."
sudo apt-get install -y --no-install-recommends \
	bluez \
	libbluetooth-dev \
	bluetooth 2>/dev/null || true

# Install mssql-tools18 (sqlcmd) — needed for Azure SQL querying and diagnostics
echo "Installing mssql-tools18 (sqlcmd)..."
if ! command -v /opt/mssql-tools18/bin/sqlcmd >/dev/null 2>&1; then
	curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
		| sudo gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
	echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/24.04/prod noble main" \
		| sudo tee /etc/apt/sources.list.d/mssql.list > /dev/null
	sudo apt-get update -qq
	sudo ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev -qq 2>/dev/null || \
		echo "Warning: mssql-tools18 installation failed"
fi
# Persist /opt/mssql-tools18/bin on PATH
for _rc in /home/vscode/.bashrc /home/vscode/.zshrc; do
	if [[ -f "$_rc" ]] && ! grep -q 'mssql-tools18' "$_rc" 2>/dev/null; then
		printf '\nexport PATH="$PATH:/opt/mssql-tools18/bin"\n' >> "$_rc"
	fi
done

# ─────────────────────────────────────────────────────────────────────
# HVO.WebSite-specific setup
# ─────────────────────────────────────────────────────────────────────

# Configure git identity from environment (set via remoteEnv from /etc/environment on host)
echo "Configuring git identity..."
if [[ -n "${GIT_AUTHOR_NAME:-}" ]]; then
	git config --global user.name "${GIT_AUTHOR_NAME}"
	git config --global user.email "${GIT_AUTHOR_EMAIL}"
	echo "Git identity set to: ${GIT_AUTHOR_NAME} <${GIT_AUTHOR_EMAIL}>"
else
	echo "Warning: GIT_AUTHOR_NAME not set — git commits will need manual identity config."
fi
git config --global commit.gpgsign false

# Authenticate GitHub CLI
echo "Setting up GitHub CLI authentication..."
if [[ -n "${GH_PAT:-}" ]]; then
	(unset GITHUB_TOKEN GH_TOKEN 2>/dev/null; echo "${GH_PAT}" | gh auth login --with-token 2>/dev/null) || true
	gh auth setup-git 2>/dev/null || true
	echo "gh CLI authenticated with GH_PAT"
elif [[ -n "${GH_TOKEN:-}" || -n "${GITHUB_TOKEN:-}" ]]; then
	echo "No GH_PAT set — using existing token (GH_TOKEN/GITHUB_TOKEN)"
	gh auth setup-git 2>/dev/null || true
else
	echo "Warning: No GitHub credentials detected — set GH_PAT in /etc/environment on hvo-dev-host and rebuild."
fi

# Ensure dotnet tools and an existing OpenCode install are on PATH for this session and future shells.
export PATH="$HOME/.opencode/bin:$HOME/.dotnet/tools:$PATH"
for _rc in /home/vscode/.bashrc /home/vscode/.zshrc; do
	if [[ -f "$_rc" ]] && ! grep -q '\.dotnet/tools' "$_rc" 2>/dev/null; then
		printf '\nexport PATH="$HOME/.dotnet/tools:$PATH"\n' >> "$_rc"
	fi
	if [[ -f "$_rc" ]] && ! grep -q '\.opencode/bin' "$_rc" 2>/dev/null; then
		printf '\nexport PATH="$HOME/.opencode/bin:$PATH"\n' >> "$_rc"
	fi
done

# Install a pinned OpenCode CLI release without executing remote install scripts.
OPENCODE_VERSION="1.15.10"
OPENCODE_ASSET="opencode-linux-x64.tar.gz"
OPENCODE_SHA256="a4c0c94a7fdbf637e3ae479c046ca49e925370b4cee503dfba7ab677a13cd0c5"
echo "Checking OpenCode CLI..."
if [[ "$(opencode --version 2>/dev/null || true)" != "${OPENCODE_VERSION}" ]]; then
	if [[ "$(uname -s)" == "Linux" && "$(uname -m)" == "x86_64" ]]; then
		_tmp_opencode_dir="$(mktemp -d)"
		curl -fsSL \
			-o "${_tmp_opencode_dir}/${OPENCODE_ASSET}" \
			"https://github.com/anomalyco/opencode/releases/download/v${OPENCODE_VERSION}/${OPENCODE_ASSET}"
		printf '%s  %s\n' "${OPENCODE_SHA256}" "${_tmp_opencode_dir}/${OPENCODE_ASSET}" | sha256sum -c -
		mkdir -p "$HOME/.opencode/bin"
		tar -xzf "${_tmp_opencode_dir}/${OPENCODE_ASSET}" -C "$HOME/.opencode/bin"
		chmod +x "$HOME/.opencode/bin/opencode"
		rm -rf "${_tmp_opencode_dir}"
	else
		echo "Warning: pinned OpenCode install only supports Linux x86_64 in this devcontainer."
	fi
fi

# Install .NET global tools
echo "Installing .NET global tools..."

# Entity Framework Core CLI (for database migrations)
if dotnet tool list -g | grep -q '^dotnet-ef\s'; then
	dotnet tool update --global dotnet-ef
else
	dotnet tool install --global dotnet-ef
fi

# SQLPackage CLI (for SQL project extract/deploy workflows)
if dotnet tool list -g | grep -q '^microsoft.sqlpackage\s'; then
	dotnet tool update --global microsoft.sqlpackage
else
	dotnet tool install --global microsoft.sqlpackage
fi

# Restore NuGet packages. Force the first restore after container creation so
# generated assets use the explicit writable NuGet cache locations above.
echo "Restoring NuGet packages..."
dotnet restore HVO.WebSite.sln --configfile NuGet.config --force || true

# Install Azure CLI
echo "Installing Azure CLI..."
if ! command -v az >/dev/null 2>&1; then
	curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
else
	echo "az CLI already installed: $(az version --query '"azure-cli"' -o tsv 2>/dev/null)"
fi

# Authenticate Azure CLI using service principal from .env
# Requires AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID in .env
echo "Configuring Azure CLI authentication..."
if [[ -n "${AZURE_CLIENT_ID:-}" && -n "${AZURE_CLIENT_SECRET:-}" && -n "${AZURE_TENANT_ID:-}" ]]; then
	az login --service-principal \
		--username "${AZURE_CLIENT_ID}" \
		--password "${AZURE_CLIENT_SECRET}" \
		--tenant "${AZURE_TENANT_ID}" \
		--output none 2>/dev/null \
		&& echo "az CLI authenticated with service principal" \
		|| echo "Warning: az login failed — check AZURE_CLIENT_ID/SECRET/TENANT_ID in .env"
	[[ -n "${AZURE_SUBSCRIPTION_ID:-}" ]] \
		&& az account set --subscription "${AZURE_SUBSCRIPTION_ID}" --output none 2>/dev/null \
		|| true
else
	echo "No Azure service principal in .env — add AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID to enable auto-login."
fi

# The private gist is only a bootstrap cache. Once Azure authentication is
# available, refresh allowlisted credentials from the authoritative vault.
if az account show --output none >/dev/null 2>&1; then
	echo "Synchronizing credentials from hvo-central-kv..."
	/workspaces/HVO.WebSite/scripts/sync-secrets-from-keyvault.sh --apply
else
	echo "Warning: Azure is not authenticated — credentials remain at their bootstrap-cache versions."
fi

# Generate HTTPS developer certificate
echo "Generating HTTPS developer certificate..."
dotnet dev-certs https --clean
dotnet dev-certs https

echo "Tool versions:"
echo "dotnet-ef:  $(dotnet ef --version 2>/dev/null || echo 'not installed')"
echo "sqlpackage: $(sqlpackage --version 2>/dev/null || echo 'not installed')"
echo "gh:         $(gh --version 2>/dev/null | head -1 || echo 'not installed')"
echo "az:         $(az version --query '"azure-cli"' -o tsv 2>/dev/null || echo 'not installed')"
echo "opencode:   $(opencode --version 2>/dev/null || echo 'not installed')"
echo "sqlite3:    $(sqlite3 --version 2>/dev/null | awk '{print $1}' || echo 'not installed')"
echo "python3:    $(python3 --version 2>/dev/null || echo 'not installed')"
echo "node:       $(node --version 2>/dev/null || echo 'not installed')"
echo "npm:        $(npm --version 2>/dev/null || echo 'not installed')"

echo "Post-create setup completed successfully!"
