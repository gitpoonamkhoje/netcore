#!/usr/bin/env bash
set -euo pipefail

# Validates MANDATORY GitLab CI/CD variables for IfrFunction DEV deploy.
# Configure in GitLab: Settings → CI/CD → Variables (environment scope = dev).
# See ci/gitlab-variables-ifr-dev.env.example

missing=()

require() {
  local name="$1"
  local value="${2:-}"
  if [[ -z "$value" ]]; then
    missing+=("$name")
  fi
}

echo "Validating mandatory GitLab CI/CD variables for IfrFunction DEV..."

# --- Azure service principal (template uses ARM_*; this repo also accepts AZURE_*) ---
require "ARM_TENANT_ID or AZURE_TENANT_ID" "${ARM_TENANT_ID:-${AZURE_TENANT_ID:-}}"
require "ARM_CLIENT_ID or AZURE_CLIENT_ID" "${ARM_CLIENT_ID:-${AZURE_CLIENT_ID:-}}"
require "ARM_CLIENT_SECRET or AZURE_CLIENT_SECRET" "${ARM_CLIENT_SECRET:-${AZURE_CLIENT_SECRET:-}}"
require "ARM_SUBSCRIPTION_ID or AZURE_SUBSCRIPTION_ID" "${ARM_SUBSCRIPTION_ID:-${AZURE_SUBSCRIPTION_ID:-}}"

# --- Function App target ---
require "IFR_FUNCTION_APP_NAME" "${IFR_FUNCTION_APP_NAME:-}"
require "AZURE_RESOURCE_GROUP_NAME" "${AZURE_RESOURCE_GROUP_NAME:-}"

# --- Build ---
require "NUGET_AUTH" "${NUGET_AUTH:-}"

# --- Runtime (always required) ---
require "AZURE_FUNCTIONS_ENVIRONMENT" "${AZURE_FUNCTIONS_ENVIRONMENT:-}"
require "APP_ENVIRONMENT_CODE" "${APP_ENVIRONMENT_CODE:-}"
require "FUNCTIONS_WORKER_RUNTIME" "${FUNCTIONS_WORKER_RUNTIME:-}"

# --- Secrets: Key Vault OR explicit AppResources ---
key_vault="${KEY_VAULT_URI:-${APPRESOURCES_KEY_VAULT_URI:-}}"
sql="${APPRESOURCES_IFR_SQL_CONNECTION_STRING:-}"
storage="${AZURE_WEBJOBS_STORAGE:-${APPRESOURCES_AZURE_WEBJOBS_STORAGE:-}}"
share="${APPRESOURCES_FILE_SHARE_NAME:-}"

if [[ -z "$key_vault" ]]; then
  require "APPRESOURCES_IFR_SQL_CONNECTION_STRING (or KEY_VAULT_URI)" "$sql"
  require "AZURE_WEBJOBS_STORAGE or APPRESOURCES_AZURE_WEBJOBS_STORAGE (or KEY_VAULT_URI)" "$storage"
  require "APPRESOURCES_FILE_SHARE_NAME (or KEY_VAULT_URI)" "$share"
else
  echo "KEY_VAULT_URI is set; AppResources secrets can come from Key Vault."
fi

if [[ ${#missing[@]} -gt 0 ]]; then
  echo ""
  echo "ERROR: Mandatory GitLab CI/CD variables are missing:"
  for name in "${missing[@]}"; do
    echo "  - $name"
  done
  echo ""
  echo "Set them in GitLab → Settings → CI/CD → Variables"
  echo "  Environment scope: dev"
  echo "  Mark secrets as Masked + Protected"
  echo "Reference: ci/gitlab-variables-ifr-dev.env.example"
  exit 1
fi

echo "All mandatory CI/CD variables are present."
