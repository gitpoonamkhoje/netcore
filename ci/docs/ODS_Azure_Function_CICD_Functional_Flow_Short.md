# ODS Azure Function -- CI/CD Functional Flow

## 1. CI/CD Template

The project uses the existing enterprise shared CI/CD template:

``` yaml
include:
  - project: "1/dotnet-core"
    ref: 13.3.0
    file: "main.yml"
```

The shared template provides reusable build, deployment, security-scan,
rules and utility jobs.

------------------------------------------------------------------------

## 2. Functional CI/CD Flow

``` mermaid
flowchart LR
    A[Developer Commit] --> B[GitLab Pipeline]
    B --> C[Shared CI/CD Template]
    C --> D[Build Package]
    D --> E[Approved .NET 10 SDK Image]
    E --> F[NuGet Authentication]
    F --> G[dotnet publish]
    G --> H[ZIP Application Artifact]
    H --> I[Security / Quality Jobs]
    I --> J[Manual DEV Deploy]
    J --> K[Azure Service Principal Login]
    K --> L[Azure Function App]
```

------------------------------------------------------------------------

## 3. Key Build Configuration

  ------------------------------------------------------------------------------------
  Variable                                         Purpose
  ------------------------------------------------ -----------------------------------
  `PKG=ods-fap`                                    Application/package name

  `SOLUTION_PATH`                                  .NET project to publish

  `BUILD_OPTIONS`                                  Release + `linux-x64` +
                                                   framework-dependent

  `COMPILER_VERSION=mtb-ubi8-dotnet10-sdk:0.3.8`   Approved .NET 10 build image

  `OUTPUT_PATH=./publish`                          Publish output

  `NUGET_CONFIG_PATH`                              NuGet configuration location

  `RUNNER_TAG=cicd-aks-dev`                        CI runner
  ------------------------------------------------------------------------------------

The build executes:

``` text
dotnet publish
      ↓
./publish
      ↓
ods-fap-<commit-sha>.zip
```

**Important:** The `.NET 10 SDK image` is only the build environment.
The application is deployed as a **ZIP artifact**, not as a Docker
image.

------------------------------------------------------------------------

## 4. Mandatory Authentication Variables

### NuGet

``` text
NUGET_AUTH
NUGET_CONFIG_PATH
```

`NUGET_AUTH` provides the approved NuGet configuration/feed
authentication required for package restore.

### Azure

``` text
ARM_TENANT_ID
ARM_CLIENT_ID
ARM_CLIENT_SECRET
ARM_SUBSCRIPTION_ID
```

These are used by the deployment template for Azure service-principal
authentication.

**Secrets must be maintained as protected/masked GitLab CI/CD variables
and must not be hard-coded.**

------------------------------------------------------------------------

## 5. DEV Deployment

The current project configuration uses:

``` yaml
dev_deploy:
  extends: .deploy_azure_dev
```

and:

``` yaml
rules:
  - when: manual
```

Therefore:

``` text
Successful Build
      ↓
Application ZIP
      ↓
Manual DEV Deployment
      ↓
Azure Authentication
      ↓
Azure Function App
```

The target `APP_SERVICE` and `RESOURCE_GROUP` values should be confirmed
with the DevOps/Azure team.

------------------------------------------------------------------------

## 6. Security and Quality

The shared pipeline provides reusable jobs for applicable:

-   SAST
-   Policy Scan
-   DAST
-   Aqua Scan
-   ACS
-   Veracode
-   Unit Testing / Linting

Project-specific configuration should use the enterprise shared
jobs/rules rather than duplicating their implementation.

------------------------------------------------------------------------

## 7. Items to Confirm

Before finalizing the pipeline:

-   `NUGET_AUTH` / approved NuGet feed
-   `ARM_*` Azure deployment variables
-   DEV `APP_SERVICE`
-   DEV `RESOURCE_GROUP`
-   CERT and PROD deployment jobs/rules
-   Final Azure Function hosting OS/runtime
