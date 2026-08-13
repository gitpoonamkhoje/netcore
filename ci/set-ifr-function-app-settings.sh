#!/usr/bin/env bash
set -euo pipefail

# Applies GitLab CI/CD Variables to the IfrFunction Azure Function App (DEV).
# Runs after deploy. Fail the job if required target names are missing.
#
# GitLab → Settings → CI/CD → Variables (environment scope = dev)

if [[ -z "${AZURE_FUNCTION_APP_NAME:-${IFR_FUNCTION_APP_NAME:-}}" || -z "${AZURE_RESOURCE_GROUP_NAME:-}" ]]; then
  echo "AZURE_FUNCTION_APP_NAME (or IFR_FUNCTION_APP_NAME) and AZURE_RESOURCE_GROUP_NAME are required."
  exit 1
fi

app_name="${AZURE_FUNCTION_APP_NAME:-$IFR_FUNCTION_APP_NAME}"
resource_group="${AZURE_RESOURCE_GROUP_NAME}"

settings=()

add_setting() {
  local key="$1"
  local value="${2:-}"
  if [[ -n "$value" ]]; then
    settings+=("${key}=${value}")
  fi
}

# --- Runtime (mandatory) ---
add_setting "FUNCTIONS_WORKER_RUNTIME" "${FUNCTIONS_WORKER_RUNTIME:-dotnet-isolated}"
add_setting "AZURE_FUNCTIONS_ENVIRONMENT" "${AZURE_FUNCTIONS_ENVIRONMENT:-Development}"
add_setting "AppEnvironmentCode" "${APP_ENVIRONMENT_CODE:-Dev}"

# --- Key Vault ---
add_setting "KeyVaultUri" "${KEY_VAULT_URI:-}"
add_setting "KeyVaultManagedIdentityClientId" "${KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-}"
add_setting "AppResources__KeyVaultUri" "${APPRESOURCES_KEY_VAULT_URI:-${KEY_VAULT_URI:-}}"
add_setting "AppResources__KeyVaultManagedIdentityClientId" "${APPRESOURCES_KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-${KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-}}"
add_setting "AppResources__AppEnvironmentCode" "${APPRESOURCES_APP_ENVIRONMENT_CODE:-${APP_ENVIRONMENT_CODE:-Dev}}"

# --- Storage / SQL (mandatory unless provided via Key Vault) ---
add_setting "AzureWebJobsStorage" "${AZURE_WEBJOBS_STORAGE:-}"
add_setting "AppResources__AzureWebJobsStorage" "${APPRESOURCES_AZURE_WEBJOBS_STORAGE:-${AZURE_WEBJOBS_STORAGE:-}}"
add_setting "AppResources__IfrSqlConnectionString" "${APPRESOURCES_IFR_SQL_CONNECTION_STRING:-}"
add_setting "AppResources__FileShareName" "${APPRESOURCES_FILE_SHARE_NAME:-}"

# --- APT / SPECTR paths ---
add_setting "AppResources__AptInputSharePath" "${APPRESOURCES_APT_INPUT_SHARE_PATH:-ifr/apt/inbound}"
add_setting "AppResources__SpectrInputSharePath" "${APPRESOURCES_SPECTR_INPUT_SHARE_PATH:-ifr/spectr/inbound}"
add_setting "AppResources__ArchiveSharePath" "${APPRESOURCES_ARCHIVE_SHARE_PATH:-ifr/archive}"
add_setting "AppResources__AptProcessedSharePath" "${APPRESOURCES_APT_PROCESSED_SHARE_PATH:-ifr/apt/processed}"
add_setting "AppResources__SpectrProcessedSharePath" "${APPRESOURCES_SPECTR_PROCESSED_SHARE_PATH:-ifr/spectr/processed}"
add_setting "AppResources__LogoSharePath" "${APPRESOURCES_LOGO_SHARE_PATH:-ifr/assets/logo.png}"
add_setting "AppResources__BlobContainerName" "${APPRESOURCES_BLOB_CONTAINER_NAME:-ifr}"

# --- APT-to-PDF pipeline (architecture diagram) ---
add_setting "AppResources__AptToPdfEnabled" "${APPRESOURCES_APT_TO_PDF_ENABLED:-true}"
add_setting "AppResources__AptToPdfInputContainer" "${APPRESOURCES_APT_TO_PDF_INPUT_CONTAINER:-input}"
add_setting "AppResources__AptToPdfProcessingContainer" "${APPRESOURCES_APT_TO_PDF_PROCESSING_CONTAINER:-processing}"
add_setting "AppResources__AptToPdfOutputContainer" "${APPRESOURCES_APT_TO_PDF_OUTPUT_CONTAINER:-output}"
add_setting "AppResources__AptToPdfArchiveContainer" "${APPRESOURCES_APT_TO_PDF_ARCHIVE_CONTAINER:-archive}"
add_setting "AppResources__AptToPdfFailedContainer" "${APPRESOURCES_APT_TO_PDF_FAILED_CONTAINER:-failed}"
add_setting "AppResources__AptToPdfLogsContainer" "${APPRESOURCES_APT_TO_PDF_LOGS_CONTAINER:-logs}"
add_setting "AppResources__AptToPdfUseDatabaseScheduler" "${APPRESOURCES_APT_TO_PDF_USE_DATABASE_SCHEDULER:-true}"
add_setting "AppResources__AptToPdfSchedulerCron" "${APPRESOURCES_APT_TO_PDF_SCHEDULER_CRON:-0 */5 * * * *}"
add_setting "AppResources__AptToPdfMaxRetries" "${APPRESOURCES_APT_TO_PDF_MAX_RETRIES:-3}"
add_setting "AppResources__LegacyAptPdfExePath" "${APPRESOURCES_LEGACY_APT_PDF_EXE_PATH:-}"
add_setting "AppResources__LegacyAptPdfExeArguments" "${APPRESOURCES_LEGACY_APT_PDF_EXE_ARGUMENTS:-}"
add_setting "AppResources__EnsureMetadataTables" "${APPRESOURCES_ENSURE_METADATA_TABLES:-true}"

# --- APT/SPECTR behaviour ---
add_setting "AppResources__AptSpectrPollingEnabled" "${APPRESOURCES_APT_SPECTR_POLLING_ENABLED:-false}"
add_setting "AppResources__AptApplyTransformations" "${APPRESOURCES_APT_APPLY_TRANSFORMATIONS:-false}"
add_setting "AppResources__SpectrApplyTransformations" "${APPRESOURCES_SPECTR_APPLY_TRANSFORMATIONS:-true}"
add_setting "AppResources__AptSpectrMaxRetryAttempts" "${APPRESOURCES_APT_SPECTR_MAX_RETRY_ATTEMPTS:-3}"

if [[ ${#settings[@]} -eq 0 ]]; then
  echo "ERROR: No Function App settings to apply. Set GitLab CI/CD Variables first."
  exit 1
fi

echo "Applying ${#settings[@]} app setting(s) to ${app_name} (RG=${resource_group})..."
az functionapp config appsettings set \
  --name "$app_name" \
  --resource-group "$resource_group" \
  --settings "${settings[@]}" \
  --output none

echo "IfrFunction DEV app settings applied successfully."
