#!/usr/bin/env bash
set -euo pipefail

# Applies GitLab CI/CD variables to an Azure Function App at deploy time.
# Configure variables in GitLab: Settings -> CI/CD -> Variables (per environment).
#
# Required GitLab variables:
#   AZURE_FUNCTION_APP_NAME
#   AZURE_RESOURCE_GROUP_NAME
#
# Azure service principal (required for deploy jobs):
#   AZURE_CLIENT_ID
#   AZURE_CLIENT_SECRET
#   AZURE_TENANT_ID
#   AZURE_SUBSCRIPTION_ID

if [[ -z "${AZURE_FUNCTION_APP_NAME:-}" || -z "${AZURE_RESOURCE_GROUP_NAME:-}" ]]; then
  echo "AZURE_FUNCTION_APP_NAME and AZURE_RESOURCE_GROUP_NAME are required."
  exit 1
fi

settings=()

add_setting() {
  local key="$1"
  local value="${2:-}"
  if [[ -n "$value" ]]; then
    settings+=("${key}=${value}")
  fi
}

add_setting "FUNCTIONS_WORKER_RUNTIME" "${FUNCTIONS_WORKER_RUNTIME:-dotnet-isolated}"
add_setting "AZURE_FUNCTIONS_ENVIRONMENT" "${AZURE_FUNCTIONS_ENVIRONMENT:-}"
add_setting "AppEnvironmentCode" "${APP_ENVIRONMENT_CODE:-}"
add_setting "KeyVaultUri" "${KEY_VAULT_URI:-}"
add_setting "KeyVaultManagedIdentityClientId" "${KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-}"

add_setting "AzureWebJobsStorage" "${AZURE_WEBJOBS_STORAGE:-}"
add_setting "AppResources__SqlConnectionString" "${APPRESOURCES_SQL_CONNECTION_STRING:-}"
add_setting "AppResources__AzureWebJobsStorage" "${APPRESOURCES_AZURE_WEBJOBS_STORAGE:-}"
add_setting "AppResources__StorageAccountName" "${APPRESOURCES_STORAGE_ACCOUNT_NAME:-}"
add_setting "AppResources__FileShareName" "${APPRESOURCES_FILE_SHARE_NAME:-}"
add_setting "AppResources__DataFactorySubscriptionId" "${APPRESOURCES_DATA_FACTORY_SUBSCRIPTION_ID:-}"
add_setting "AppResources__DataFactoryResourceGroupName" "${APPRESOURCES_DATA_FACTORY_RESOURCE_GROUP_NAME:-}"
add_setting "AppResources__DataFactoryName" "${APPRESOURCES_DATA_FACTORY_NAME:-}"
add_setting "AppResources__AdfManagementApiVersion" "${APPRESOURCES_ADF_MANAGEMENT_API_VERSION:-}"
add_setting "AppResources__AdfTriggerUrl" "${APPRESOURCES_ADF_TRIGGER_URL:-}"
add_setting "AppResources__SqlAgentValidationConnectionString" "${APPRESOURCES_SQL_AGENT_VALIDATION_CONNECTION_STRING:-}"
add_setting "AppResources__AdfValidationConnectionString" "${APPRESOURCES_ADF_VALIDATION_CONNECTION_STRING:-}"
add_setting "AppResources__KeyVaultUri" "${APPRESOURCES_KEY_VAULT_URI:-${KEY_VAULT_URI:-}}"
add_setting "AppResources__KeyVaultManagedIdentityClientId" "${APPRESOURCES_KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-${KEY_VAULT_MANAGED_IDENTITY_CLIENT_ID:-}}"
add_setting "AppResources__AppEnvironmentCode" "${APPRESOURCES_APP_ENVIRONMENT_CODE:-${APP_ENVIRONMENT_CODE:-}}"

add_setting "AppResources__FtpSharePath" "${APPRESOURCES_FTP_SHARE_PATH:-}"
add_setting "AppResources__DdlSharePath" "${APPRESOURCES_DDL_SHARE_PATH:-}"
add_setting "AppResources__BackupSharePath" "${APPRESOURCES_BACKUP_SHARE_PATH:-}"
add_setting "AppResources__UnzipSharePath" "${APPRESOURCES_UNZIP_SHARE_PATH:-}"

add_setting "ADFTriggerUrl" "${ADF_TRIGGER_URL:-}"
add_setting "SendGridApiKey" "${SENDGRID_API_KEY:-}"
add_setting "PollNotifyEmailFrom" "${POLL_NOTIFY_EMAIL_FROM:-}"
add_setting "PollNotifyEmailTo" "${POLL_NOTIFY_EMAIL_TO:-}"

if [[ ${#settings[@]} -eq 0 ]]; then
  echo "No app settings to apply."
  exit 0
fi

echo "Applying ${#settings[@]} app setting(s) to ${AZURE_FUNCTION_APP_NAME}..."
az functionapp config appsettings set \
  --name "$AZURE_FUNCTION_APP_NAME" \
  --resource-group "$AZURE_RESOURCE_GROUP_NAME" \
  --settings "${settings[@]}" \
  --output none

echo "App settings applied successfully."
