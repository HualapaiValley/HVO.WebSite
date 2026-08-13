#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
vault_name="${HVO_KEY_VAULT_NAME:-hvo-central-kv}"
mode="check"
drift_count=0

usage() {
	printf 'Usage: %s [--check | --apply]\n' "$(basename "$0")"
	printf '\n'
	printf 'Synchronizes allowlisted local credentials from Azure Key Vault %s.\n' "$vault_name"
	printf 'Key Vault is authoritative; host addresses, ports, image tags, SSIDs, and device inventory are preserved.\n'
}

fail() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

while (($# > 0)); do
	case "$1" in
		--check)
			mode="check"
			;;
		--apply)
			mode="apply"
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			fail "Unknown argument '$1'."
			;;
	esac
	shift
done

command -v az >/dev/null 2>&1 || fail "Azure CLI is required."
az account show --output none >/dev/null 2>&1 || fail "Azure CLI is not authenticated."
az keyvault show --name "$vault_name" --output none >/dev/null 2>&1 || fail "Cannot access Key Vault '$vault_name'."

declare -A secret_cache=()

get_secret() {
	local secret_name="$1"
	if [[ ! -v "secret_cache[$secret_name]" ]]; then
		secret_cache["$secret_name"]="$(az keyvault secret show \
			--vault-name "$vault_name" \
			--name "$secret_name" \
			--query value \
			--output tsv)" || fail "Required Key Vault secret is unavailable: $secret_name"
	fi
	printf '%s' "${secret_cache[$secret_name]}"
}

read_dotenv_value() {
	local file="$1"
	local wanted="$2"
	local line
	[[ -f "$file" ]] || return 1
	while IFS= read -r line || [[ -n "$line" ]]; do
		line="${line#export }"
		if [[ "$line" == "$wanted="* ]]; then
			printf '%s' "${line#*=}"
			return 0
		fi
	done < "$file"
	return 1
}

write_dotenv_value() {
	local file="$1"
	local wanted="$2"
	local value="$3"
	local temp_file line found=false
	[[ "$value" != *$'\n'* && "$value" != *$'\r'* ]] || fail "$wanted cannot be represented as a one-line dotenv value."
	mkdir -p "$(dirname "$file")"
	temp_file="$(mktemp "${file}.tmp.XXXXXX")"
	chmod 600 "$temp_file"
	if [[ -f "$file" ]]; then
		while IFS= read -r line || [[ -n "$line" ]]; do
			if [[ "${line#export }" == "$wanted="* ]]; then
				if [[ "$found" == false ]]; then
					printf '%s=%s\n' "$wanted" "$value" >> "$temp_file"
					found=true
				fi
			else
				printf '%s\n' "$line" >> "$temp_file"
			fi
		done < "$file"
	fi
	if [[ "$found" == false ]]; then
		printf '\n%s=%s\n' "$wanted" "$value" >> "$temp_file"
	fi
	mv "$temp_file" "$file"
	chmod 600 "$file"
}

sync_dotenv_value() {
	local relative_file="$1"
	local key="$2"
	local expected="$3"
	local file="$repo_root/$relative_file"
	local current=""
	current="$(read_dotenv_value "$file" "$key" || true)"
	if [[ "$current" == "$expected" ]]; then
		printf 'ok      %s:%s\n' "$relative_file" "$key"
		return
	fi
	((drift_count += 1))
	if [[ "$mode" == "apply" ]]; then
		write_dotenv_value "$file" "$key" "$expected"
		printf 'updated %s:%s\n' "$relative_file" "$key"
	else
		printf 'drift   %s:%s\n' "$relative_file" "$key"
	fi
}

sync_dotenv_secret() {
	sync_dotenv_value "$1" "$2" "$(get_secret "$3")"
}

check_sourced_dotenv_secret() {
	local relative_file="$1"
	local key="$2"
	local secret_name="$3"
	local file="$repo_root/$relative_file"
	local current expected
	[[ -f "$file" ]] || fail "Required dotenv file is missing: $relative_file"
	set -a
	# shellcheck disable=SC1090
	source "$file"
	set +a
	current="${!key-}"
	expected="$(get_secret "$secret_name")"
	if [[ "$current" == "$expected" ]]; then
		printf 'ok      %s:%s\n' "$relative_file" "$key"
		return
	fi
	((drift_count += 1))
	printf 'drift   %s:%s (multiline value requires approved manual rotation)\n' "$relative_file" "$key"
}

derive_local_connection_string() {
	local relative_file="$1"
	local current password prefix suffix
	current="$(read_dotenv_value "$repo_root/$relative_file" ConnectionStrings__HualapaiValleyObservatory || true)"
	[[ "$current" == *Password=* ]] || fail "$relative_file has no local SQL Password segment to synchronize."
	password="$(get_secret obs-docker-mssql-sa-password)"
	prefix="${current%%Password=*}"
	suffix="${current#*Password=}"
	[[ "$suffix" == *';'* ]] || fail "$relative_file has a malformed local SQL connection string."
	suffix="${suffix#*;}"
	printf '%sPassword=%s;%s' "$prefix" "$password" "$suffix"
}

sync_secret_file() {
	local relative_file="$1"
	local secret_name="$2"
	local file="$repo_root/$relative_file"
	local expected current=""
	expected="$(get_secret "$secret_name")"
	[[ -f "$file" ]] && current="$(<"$file")"
	if [[ "$current" == "$expected" ]]; then
		printf 'ok      %s\n' "$relative_file"
		return
	fi
	((drift_count += 1))
	if [[ "$mode" == "apply" ]]; then
		mkdir -p "$(dirname "$file")"
		chmod 700 "$(dirname "$file")"
		umask 077
		printf '%s\n' "$expected" > "$file"
		chmod 600 "$file"
		printf 'updated %s\n' "$relative_file"
	else
		printf 'drift   %s\n' "$relative_file"
	fi
}

sync_yaml_secret() {
	local relative_file="$1"
	local key="$2"
	local secret_name="$3"
	local file="$repo_root/$relative_file"
	local expected current="" temp_file line found=false
	expected="$(get_secret "$secret_name")"
	if [[ -f "$file" ]]; then
		while IFS= read -r line || [[ -n "$line" ]]; do
			if [[ "$line" == "$key:"* ]]; then
				current="${line#*: }"
				current="${current#\"}"
				current="${current%\"}"
				break
			fi
		done < "$file"
	fi
	if [[ "$current" == "$expected" ]]; then
		printf 'ok      %s:%s\n' "$relative_file" "$key"
		return
	fi
	((drift_count += 1))
	if [[ "$mode" != "apply" ]]; then
		printf 'drift   %s:%s\n' "$relative_file" "$key"
		return
	fi
	[[ "$expected" != *$'\n'* && "$expected" != *$'\r'* && "$expected" != *'"'* ]] || fail "$secret_name cannot be safely represented in the ESPHome YAML file."
	mkdir -p "$(dirname "$file")"
	temp_file="$(mktemp "${file}.tmp.XXXXXX")"
	chmod 600 "$temp_file"
	if [[ -f "$file" ]]; then
		while IFS= read -r line || [[ -n "$line" ]]; do
			if [[ "$line" == "$key:"* ]]; then
				if [[ "$found" == false ]]; then
					printf '%s: "%s"\n' "$key" "$expected" >> "$temp_file"
					found=true
				fi
			else
				printf '%s\n' "$line" >> "$temp_file"
			fi
		done < "$file"
	fi
	if [[ "$found" == false ]]; then
		printf '%s: "%s"\n' "$key" "$expected" >> "$temp_file"
	fi
	mv "$temp_file" "$file"
	chmod 600 "$file"
	printf 'updated %s:%s\n' "$relative_file" "$key"
}

central_uri="https://${vault_name}.vault.azure.net/"

# Root devcontainer materialization. Non-secret deployment settings remain untouched.
sync_dotenv_secret .env TAILSCALE_AUTHKEY obs-tailscale-authkey
sync_dotenv_secret .env GH_PAT obs-github-token
check_sourced_dotenv_secret .env SSH_PRIVATE_KEY obs-ssh-private-key
sync_dotenv_secret .env AZURE_CLIENT_ID obs-azure-client-id
sync_dotenv_secret .env AZURE_CLIENT_SECRET obs-azure-client-secret
sync_dotenv_secret .env AZURE_TENANT_ID obs-azure-tenant-id
sync_dotenv_value .env AZURE_KEYVAULT_URI "$central_uri"
sync_dotenv_value .env HVO_WEBSITE_KEYVAULT_URI "$central_uri"
sync_dotenv_secret .env DAVIS_API_KEY Seeding--DavisApiKey
sync_dotenv_secret .env BMS_API_KEY Seeding--BmsApiKey
sync_dotenv_secret .env POWER_API_KEY Seeding--PowerApiKey
sync_dotenv_secret .env EG4_POWER_API_KEY Seeding--PowerApiKey
sync_dotenv_secret .env POWER_READ_API_KEY Seeding--PowerReadApiKey
sync_dotenv_secret .env WEATHER_READ_API_KEY Seeding--WeatherReadApiKey
sync_dotenv_secret .env SOLAR_ASSISTANT_REST_USERNAME obs-solarassistant-rest-username
sync_dotenv_secret .env SOLAR_ASSISTANT_REST_PASSWORD obs-solarassistant-rest-password
sync_dotenv_secret .env SOLAR_ASSISTANT_MQTT_USERNAME obs-solarassistant-mqtt-username
sync_dotenv_secret .env SOLAR_ASSISTANT_MQTT_PASSWORD obs-solarassistant-mqtt-password
sync_dotenv_secret .env HVO_CONTAINER_REGISTRY_USERNAME obs-registry-admin-username
sync_dotenv_secret .env HVO_CONTAINER_REGISTRY_PASSWORD obs-registry-admin-password
sync_dotenv_secret .env MSSQL_SA_PASSWORD obs-docker-mssql-sa-password
sync_dotenv_secret .env HOME_ASSISTANT_TOKEN HomeAssistant--Token
sync_dotenv_secret .env ESPHOME_HOME_PROXY_WIFI_PASSWORD obs-wifi-home-express-is-password
sync_dotenv_secret .env ESPHOME_HVO_PROXY_WIFI_PASSWORD obs-wifi-hvo-password
sync_dotenv_value .env ConnectionStrings__HualapaiValleyObservatory "$(derive_local_connection_string .env)"

# Website deployment and handoff materializations.
for env_file in deploy/hvo-docker/.env .env-handoff/hvo-docker.env; do
	if [[ ! -f "$repo_root/$env_file" ]]; then
		printf 'skipped %s (not materialized)\n' "$env_file"
		continue
	fi
	sync_dotenv_secret "$env_file" HVO_CONTAINER_REGISTRY_USERNAME obs-registry-admin-username
	sync_dotenv_secret "$env_file" HVO_CONTAINER_REGISTRY_PASSWORD obs-registry-admin-password
	sync_dotenv_secret "$env_file" AZURE_CLIENT_ID WebsiteRuntime--AzureClientId
	sync_dotenv_secret "$env_file" AZURE_CLIENT_SECRET WebsiteRuntime--AzureClientSecret
	sync_dotenv_secret "$env_file" AZURE_TENANT_ID WebsiteRuntime--AzureTenantId
	sync_dotenv_value "$env_file" KeyVault__Uri "$central_uri"
	sync_dotenv_secret "$env_file" Seeding__DavisApiKey Seeding--DavisApiKey
	sync_dotenv_secret "$env_file" Seeding__WeatherReadApiKey Seeding--WeatherReadApiKey
	sync_dotenv_secret "$env_file" Seeding__BmsApiKey Seeding--BmsApiKey
	sync_dotenv_secret "$env_file" Seeding__PowerApiKey Seeding--PowerApiKey
	sync_dotenv_secret "$env_file" Seeding__PowerReadApiKey Seeding--PowerReadApiKey
	sync_dotenv_secret "$env_file" PowerStatus__SolarAssistantGatewayUrl PowerStatus--SolarAssistantGatewayUrl
	sync_dotenv_value "$env_file" ConnectionStrings__HualapaiValleyObservatory "$(derive_local_connection_string "$env_file")"
done

# Existing environment-only gateways and handoff copies.
for env_file in deploy/pi-gateways/solarassistant/.env .env-handoff/solarassistant.env; do
	if [[ ! -f "$repo_root/$env_file" ]]; then
		printf 'skipped %s (not materialized)\n' "$env_file"
		continue
	fi
	sync_dotenv_secret "$env_file" SOLAR_ASSISTANT_REST_USERNAME obs-solarassistant-rest-username
	sync_dotenv_secret "$env_file" SOLAR_ASSISTANT_REST_PASSWORD obs-solarassistant-rest-password
	sync_dotenv_secret "$env_file" SOLAR_ASSISTANT_MQTT_USERNAME obs-solarassistant-mqtt-username
	sync_dotenv_secret "$env_file" SOLAR_ASSISTANT_MQTT_PASSWORD obs-solarassistant-mqtt-password
	sync_dotenv_secret "$env_file" POWER_API_KEY Seeding--PowerApiKey
done
for env_file in deploy/pi-gateways/tplink-kasa/.env .env-handoff/tplink-kasa.env; do
	if [[ ! -f "$repo_root/$env_file" ]]; then
		printf 'skipped %s (not materialized)\n' "$env_file"
		continue
	fi
	sync_dotenv_secret "$env_file" KASA_LOCAL_API_KEY Edge--TplinkKasa--LocalApiKey
	sync_dotenv_secret "$env_file" KASA_OUTBOX_APIKEY Seeding--PowerApiKey
done
for env_file in deploy/pi-gateways/davis/.env .env-handoff/davis.env; do
	if [[ ! -f "$repo_root/$env_file" ]]; then
		printf 'skipped %s (not materialized)\n' "$env_file"
		continue
	fi
	sync_dotenv_secret "$env_file" DAVIS_API_KEY Seeding--DavisApiKey
done
for env_file in deploy/pi-gateways/jkbms/.env .env-handoff/jkbms.env; do
	if [[ ! -f "$repo_root/$env_file" ]]; then
		printf 'skipped %s (not materialized)\n' "$env_file"
		continue
	fi
	sync_dotenv_secret "$env_file" BMS_API_KEY Seeding--BmsApiKey
done

# Current mounted edge secret contracts. Configuration files remain operator-managed.
for mapping in \
	'davis:Seeding--DavisApiKey:Edge--Davis--DiagnosticsApiKey' \
	'jkbms:Seeding--BmsApiKey:Edge--JkBms--DiagnosticsApiKey' \
	'eg4:Seeding--PowerApiKey:Edge--Eg4--DiagnosticsApiKey' \
	'smartshunt:Seeding--SmartShuntApiKey:Edge--SmartShunt--DiagnosticsApiKey'; do
	IFS=: read -r gateway ingest_secret diagnostics_secret <<< "$mapping"
	sync_secret_file "deploy/pi-gateways/$gateway/secrets/central-ingest-api-key" "$ingest_secret"
	sync_secret_file "deploy/pi-gateways/$gateway/secrets/diagnostics-api-key" "$diagnostics_secret"
	sync_secret_file "deploy/pi-gateways/$gateway/secrets/mqtt-username" HomeAssistant--MqttUsername
	sync_secret_file "deploy/pi-gateways/$gateway/secrets/mqtt-password" HomeAssistant--MqttPassword
done
sync_secret_file deploy/pi-gateways/home-assistant-exporter/secrets/home-assistant-token HomeAssistant--Token
sync_secret_file deploy/pi-gateways/home-assistant-exporter/secrets/diagnostics-api-key Edge--HomeAssistantExporter--DiagnosticsApiKey

# The exporter ingest key is intentionally not materialized until exact
# Seeding--HomeAssistantExporterSources--<index> claims are approved. Creating a
# broad key without source ownership would violate the central ingest contract.

# ESPHome management and Wi-Fi credentials. SSIDs remain local configuration.
esphome_secrets=deploy/home-assistant/configuration/esphome/secrets.yaml
if [[ -f "$repo_root/$esphome_secrets" ]]; then
	sync_yaml_secret "$esphome_secrets" hvo_wifi_password obs-wifi-hvo-password
	sync_yaml_secret "$esphome_secrets" home_wifi_password obs-wifi-home-express-is-password
	sync_yaml_secret "$esphome_secrets" hvo_bluetooth_proxy_api_encryption_key HomeAssistant--EspHomeProxyApiEncryptionKey
	sync_yaml_secret "$esphome_secrets" hvo_bluetooth_proxy_ota_password HomeAssistant--EspHomeProxyOtaPassword
	sync_yaml_secret "$esphome_secrets" hvo_bluetooth_proxy_fallback_password HomeAssistant--EspHomeProxyFallbackPassword
else
	printf 'skipped %s (SSIDs not materialized)\n' "$esphome_secrets"
fi

if [[ "$mode" == "check" && "$drift_count" -gt 0 ]]; then
	printf 'Detected %d Key Vault synchronization difference(s). Run %s --apply.\n' "$drift_count" "$(basename "$0")" >&2
	exit 1
fi

if [[ "$mode" == "apply" ]]; then
	printf 'Applied %d Key Vault synchronization update(s).\n' "$drift_count"
else
	printf 'All allowlisted local credentials match Key Vault %s.\n' "$vault_name"
fi
