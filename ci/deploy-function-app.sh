#!/usr/bin/env bash
set -euo pipefail

# Deploy a published Azure Functions package and apply GitLab CI/CD variables.
#
# Usage:
#   PUBLISH_DIR=path/to/publish bash ci/deploy-function-app.sh

if [[ -z "${PUBLISH_DIR:-}" ]]; then
  echo "PUBLISH_DIR is required."
  exit 1
fi

if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "Publish directory not found: $PUBLISH_DIR"
  exit 1
fi

zip_file="$(mktemp /tmp/function-app.XXXXXX.zip)"
trap 'rm -f "$zip_file"' EXIT

(
  cd "$PUBLISH_DIR"
  zip -r "$zip_file" . >/dev/null
)

az functionapp deployment source config-zip \
  --name "$AZURE_FUNCTION_APP_NAME" \
  --resource-group "$AZURE_RESOURCE_GROUP_NAME" \
  --src "$zip_file" \
  --output none

bash "$(dirname "$0")/set-function-app-settings.sh"
