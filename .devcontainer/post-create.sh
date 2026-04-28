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

# [CUSTOMIZE] .env gist ID for this repo
ENV_GIST="1f014918502877f0c37738fa733dad65"

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
	sudo apt-get update -y && sudo apt-get install -y jq ripgrep || true
	if getent group docker >/dev/null 2>&1; then sudo usermod -aG docker vscode || true; fi
	if [ -S /var/run/docker.sock ]; then sudo chmod 666 /var/run/docker.sock || true; fi
fi

# Install fonts (not in base script — needed for the website's PDF/chart rendering)
sudo apt-get install -y --no-install-recommends \
	fontconfig fonts-dejavu-core fonts-open-sans 2>/dev/null || true
sudo fc-cache -f 2>/dev/null || true

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

# Ensure dotnet tools directory is on PATH for this session and future shells
export PATH="$HOME/.dotnet/tools:$PATH"
for _rc in /home/vscode/.bashrc /home/vscode/.zshrc; do
	if [[ -f "$_rc" ]] && ! grep -q '\.dotnet/tools' "$_rc" 2>/dev/null; then
		printf '\nexport PATH="$HOME/.dotnet/tools:$PATH"\n' >> "$_rc"
	fi
done

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

# Restore NuGet packages
echo "Restoring NuGet packages..."
dotnet restore HVO.WebSite.sln --configfile NuGet.config || true

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

# Generate HTTPS developer certificate
echo "Generating HTTPS developer certificate..."
dotnet dev-certs https --clean
dotnet dev-certs https

echo "Tool versions:"
echo "dotnet-ef:  $(dotnet ef --version 2>/dev/null || echo 'not installed')"
echo "sqlpackage: $(sqlpackage --version 2>/dev/null || echo 'not installed')"
echo "gh:         $(gh --version 2>/dev/null | head -1 || echo 'not installed')"
echo "az:         $(az version --query '"azure-cli"' -o tsv 2>/dev/null || echo 'not installed')"

echo "Post-create setup completed successfully!"
