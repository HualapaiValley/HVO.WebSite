#!/bin/bash
set -e
set -o pipefail

command_exists() {
	command -v "$1" >/dev/null 2>&1
}

echo "Running post-create setup..."

# Fix .dotnet directory ownership
echo "Fixing .dotnet directory ownership..."
sudo chown -R vscode:vscode /home/vscode/.dotnet || true

# Display .NET version
echo "Checking .NET installation..."
dotnet --info
echo "Installed SDKs:"
dotnet --list-sdks || true

# Install development CLI utilities
echo "Installing development CLI utilities..."
sudo apt-get update -y
sudo apt-get install -y --no-install-recommends \
	jq ripgrep \
	fontconfig fonts-dejavu-core fonts-open-sans \
	|| echo "Warning: Some package installations failed, continuing..."
sudo fc-cache -f || true

# Add vscode user to docker group
echo "Adding vscode user to docker group..."
if getent group docker >/dev/null 2>&1; then
	sudo usermod -aG docker vscode || true
else
	echo "Docker group not present; skipping usermod"
fi

# Set docker socket permissions
if [ -S /var/run/docker.sock ]; then
	sudo chmod 666 /var/run/docker.sock || true
fi

# Verify docker is working
if command_exists docker; then
	docker --version
else
	echo "Warning: docker CLI not found on PATH"
fi

# Setup SSH agent
echo "Setting up SSH agent..."
if [ -z "$SSH_AUTH_SOCK" ]; then
	eval "$(ssh-agent -s)"
else
	echo "Using existing SSH agent at $SSH_AUTH_SOCK"
fi

# Load SSH keys if available
if compgen -G "/home/vscode/.ssh/id_*" >/dev/null 2>&1; then
	for key in /home/vscode/.ssh/id_*; do
		if [[ -f "$key" && "$key" != *.pub ]]; then
			ssh-add "$key" >/dev/null 2>&1 && echo "Loaded SSH key: $key" || true
		fi
	done
fi

# Configure git identity from environment
echo "Configuring git identity..."
if [[ -n "${GIT_AUTHOR_NAME:-}" ]]; then
	git config --global user.name "${GIT_AUTHOR_NAME}"
	git config --global user.email "${GIT_AUTHOR_EMAIL}"
	echo "Git identity set to: ${GIT_AUTHOR_NAME} <${GIT_AUTHOR_EMAIL}>"
else
	echo "Warning: GIT_AUTHOR_NAME not set — git commits will need manual identity config."
fi
git config --global commit.gpgsign false

# Authenticate GitHub CLI if token is available
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

# Bootstrap .env from GitHub Gist
# Shared secrets gist: ADO PAT, GH PAT, Azure details, SSH keys, host credentials
ENV_GIST="1f014918502877f0c37738fa733dad65"
ENV_FILE="/workspaces/HVO.WebSite/.env"
echo "Bootstrapping .env from GitHub Gist..."
if [[ -f "$ENV_FILE" ]]; then
	echo ".env already exists — loading it"
else
	_gist_token="${GH_PAT:-${GH_TOKEN:-${GITHUB_TOKEN:-}}}"
	if [[ -z "$_gist_token" ]] && command_exists gh; then
		_gist_token=$(gh auth token 2>/dev/null) || true
	fi
	if [[ -n "$_gist_token" ]]; then
		if GH_TOKEN="$_gist_token" gh gist view "$ENV_GIST" --raw --filename .env \
				> "$ENV_FILE" 2>/dev/null; then
			chmod 600 "$ENV_FILE"
			echo ".env fetched from Gist successfully"
		else
			echo "Warning: Could not fetch .env from Gist — secrets will not be available"
			rm -f "$ENV_FILE"
		fi
	else
		echo "Warning: No GitHub token available — cannot fetch .env from Gist"
	fi
fi

if [[ -f "$ENV_FILE" ]]; then
	set -a
	# shellcheck disable=SC1090
	source "$ENV_FILE"
	set +a
	# Persist .env sourcing into shell profiles so it's available in new terminals
	for _rc in /home/vscode/.bashrc /home/vscode/.zshrc; do
		if [[ -f "$_rc" ]] && ! grep -qF "# >>> devcontainer-env >>>" "$_rc" 2>/dev/null; then
			printf '\n# >>> devcontainer-env >>>\nif [ -f %s ]; then set -a; source %s; set +a; fi\n# <<< devcontainer-env <<<\n' \
				"$ENV_FILE" "$ENV_FILE" >> "$_rc"
		fi
	done
	echo ".env sourced and added to shell profiles"
fi

# Install .NET global tools
echo "Installing .NET global tools..."

# Ensure dotnet tools directory is on PATH for this session and future shells
export PATH="$HOME/.dotnet/tools:$PATH"
for _rc in /home/vscode/.bashrc /home/vscode/.zshrc; do
	if [[ -f "$_rc" ]] && ! grep -q '\.dotnet/tools' "$_rc" 2>/dev/null; then
		printf '\nexport PATH="$HOME/.dotnet/tools:$PATH"\n' >> "$_rc"
	fi
done

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

# Generate HTTPS developer certificate
echo "Generating HTTPS developer certificate..."
dotnet dev-certs https --clean
dotnet dev-certs https

echo "Tool versions:"
echo "dotnet-ef: $(dotnet ef --version 2>/dev/null || echo 'not installed')"
echo "sqlpackage: $(sqlpackage --version 2>/dev/null || echo 'not installed')"
echo "gh: $(gh --version 2>/dev/null | head -1 || echo 'not installed')"

echo "Post-create setup completed successfully!"
