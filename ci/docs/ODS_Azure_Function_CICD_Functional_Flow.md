# ODS Azure Function CI/CD -- Functional Flow, Template Rules and Variable Mandate

## 1. Purpose

This document describes the CI/CD implementation for the **ODS Azure
Function App** based on the supplied existing GitLab CI/CD template,
including:

-   Included CI/CD template and reusable rules
-   Mandatory project variables
-   NuGet authentication requirements
-   Azure deployment authentication requirements
-   Build and deployment functional flow
-   How the .NET build image is used
-   How the application deployment artifact is created
-   Current DEV deployment behavior
-   Important items to obtain from the DevOps/Azure team

The document is based on the currently supplied CI/CD configuration and
supporting template notes.

------------------------------------------------------------------------

## 2. Current CI/CD Template Integration

The project includes the shared .NET Core CI/CD template:

``` yaml
include:
  - project: "1/dotnet-core"
    ref: 13.3.0
    file: "main.yml"
```

The shared template provides reusable build, deployment, scan, utility,
rule and retry functionality.

The broader template structure also references reusable components for:

-   .NET build
-   Azure Function deployment
-   SAST
-   Policy scan
-   DAST
-   Aqua scan
-   Advanced Cluster Security
-   Unit testing
-   Linting
-   Artifactory
-   Retry rules
-   Pipeline scripts

The supplied template uses reusable `extends` and `!reference`
constructs rather than requiring each project to implement the complete
CI/CD logic itself.

------------------------------------------------------------------------

# 3. High-Level CI/CD Functional Flow

``` mermaid
flowchart LR
    A[Developer Commit / Merge] --> B[GitLab CI/CD Pipeline]

    B --> C[Shared .NET Core Template]

    C --> D[Build Job]

    D --> E[NuGet Authentication]
    E --> F[.NET 10 SDK Build Image]
    F --> G[dotnet publish]

    G --> H[Published Output]
    H --> I[ZIP Application Artifact]

    I --> J[Build Artifact]
    J --> K[Manual DEV Deployment]

    K --> L[Azure Service Principal Authentication]
    L --> M[Azure Subscription]
    M --> N[Azure Function App]

    N --> O[Running .NET 10 Azure Function]
```

------------------------------------------------------------------------

# 4. Project-Specific CI/CD Variables

The supplied project configuration contains the following important
variables.

## 4.1 General Variables

  -----------------------------------------------------------------------
  Variable                Current Value           Purpose
  ----------------------- ----------------------- -----------------------
  `PKG`                   `ods-fap`               Package/application
                                                  artifact name

  `APP_CODE_NAME`         `ODS`                   Application code name

  `APP_CODE`              `ODS`                   Application identifier

  `VERACODE_BUCKET`       `ODS-Function-App`      Veracode
                                                  application/bucket
                                                  identifier
  -----------------------------------------------------------------------

These values identify the application and are normally safe to maintain
in the project CI/CD YAML.

------------------------------------------------------------------------

# 5. Build Variables

  ----------------------------------------------------------------------------------------------------
  Variable                  Current Value                                      Purpose
  ------------------------- -------------------------------------------------- -----------------------
  `BUILD_OPTIONS`           `-c Release -r linux-x64 --self-contained false`   .NET publish options

  `SOLUTION_PATH`           `./AzureCode/ods-fap/ods-fap/ods-fap.csproj`       Project to publish

  `PROJECT_FILE`            `ods-fap.csproj`                                   Project file used by
                                                                               related tooling

  `OUTPUT_PATH`             `./publish`                                        Publish output
                                                                               directory

  `COMPILER_VERSION`        `mtb-ubi8-dotnet10-sdk:0.3.8`                      Approved .NET 10 SDK
                                                                               build image

  `BUILD_OUTPUT_ARTIFACT`   `output`                                           Build output identifier

  `NUGET_CONFIG_PATH`       `/tmp/DOTNET_CLI_HOME/.nuget/NuGet/NuGet.Config`   NuGet configuration
                                                                               destination

  `RUNNER_TAG`              `cicd-aks-dev`                                     GitLab runner selection
  ----------------------------------------------------------------------------------------------------

The current supplied configuration targets **Linux x64** and uses a
**framework-dependent deployment** because `--self-contained false` is
specified.

------------------------------------------------------------------------

# 6. Deployment Variables

The current DEV deployment job contains:

``` yaml
dev_deploy:
  extends: .deploy_azure_dev
```

with:

  --------------------------------------------------------------------------------------------
  Variable                        Current Value                        Purpose
  ------------------------------- ------------------------------------ -----------------------
  `AZURE_FUNCTIONS_ENVIRONMENT`   `Development`                        Azure Functions
                                                                       environment

  `DEPLOYMENT_SLOT`               `main`                               Function deployment
                                                                       slot

  `APP_TYPE`                      `functionapp`                        Identifies Azure
                                                                       Function App deployment

  `APP_SERVICE`                   Project-specific / to be supplied    Target Function App

  `RESOURCE_GROUP`                `-01` suffix currently shown         Target Azure resource
                                                                       group configuration

  `HEALTH_ENDPOINT`               Empty                                Optional health
                                                                       endpoint

  `TARGET_PATH`                   `D:\inetpub\wwwroot\${PKG}\dotnet`   Generic deployment
                                                                       target variable from
                                                                       template; applicability
                                                                       should be confirmed for
                                                                       Azure Function
                                                                       deployment
  --------------------------------------------------------------------------------------------

> **Important:** The supplied project file shows the `APP_SERVICE` value
> as blank and `RESOURCE_GROUP` as `-01`. These should be confirmed with
> the Azure/DevOps team rather than guessed.

------------------------------------------------------------------------

# 7. NuGet Authentication -- Mandatory

The shared build template performs:

``` bash
cp ${NUGET_AUTH} ${NUGET_CONFIG_PATH}
```

before:

``` bash
dotnet publish ${SOLUTION_PATH} ${BUILD_OPTIONS} -o ${OUTPUT_PATH}
```

Therefore the build requires:

``` text
NUGET_AUTH
NUGET_CONFIG_PATH
```

`NUGET_CONFIG_PATH` is already defined by the project:

``` text
/tmp/DOTNET_CLI_HOME/.nuget/NuGet/NuGet.Config
```

`NUGET_AUTH` must point to/provide the approved NuGet configuration used
by the build environment.

The NuGet configuration must contain the approved feed configuration and
authentication required to restore the project's packages.

The project should obtain the approved feed information from the
DevOps/NuGet administration team, including the appropriate
common/shared feed and any environment-specific feeds required for
DEV/CERT.

------------------------------------------------------------------------

# 8. Azure Deployment Authentication -- Mandatory

The existing migration/deployment template explicitly uses Azure
service-principal authentication:

``` bash
az login \
  --service-principal \
  --tenant "${ARM_TENANT_ID}" \
  --username "${ARM_CLIENT_ID}" \
  --password "${ARM_CLIENT_SECRET}"

az account set -s ${ARM_SUBSCRIPTION_ID}
```

Therefore the following variables are required for Azure authentication:

  -----------------------------------------------------------------------
  Variable                            Represents
  ----------------------------------- -----------------------------------
  `ARM_TENANT_ID`                     Microsoft Entra ID tenant

  `ARM_CLIENT_ID`                     Service principal / application
                                      client ID

  `ARM_CLIENT_SECRET`                 Service principal credential

  `ARM_SUBSCRIPTION_ID`               Azure subscription containing the
                                      target resource
  -----------------------------------------------------------------------

These values should be configured as protected/masked GitLab CI/CD
variables by the authorized DevOps/Azure team.

**Do not hard-code these values in `.gitlab-ci.yml`.**

------------------------------------------------------------------------

# 9. Where the Azure Credentials Come From

The project team should request the existing approved CI/CD
service-principal variables.

Recommended request:

> Please provide the existing GitLab CI/CD Azure service-principal
> credentials/variables required for deploying the ODS Function App to
> DEV:
>
> `ARM_TENANT_ID`, `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, and
> `ARM_SUBSCRIPTION_ID`.

Expected source:

``` text
Microsoft Entra ID
      |
      +-- Tenant ID
      |
      +-- App Registration
             |
             +-- Application / Client ID
             |
             +-- Client Secret
      |
Azure Subscription
      |
      +-- Subscription ID
```

------------------------------------------------------------------------

# 10. Build Image -- Important Clarification

The current CI/CD template uses:

``` yaml
image:
  name: ${BAR_REGISTRY}/mtb-docker/${COMPILER_VERSION}
  entrypoint: [""]
```

and:

``` text
COMPILER_VERSION = mtb-ubi8-dotnet10-sdk:0.3.8
```

Therefore the build runs inside an approved container image similar to:

``` text
artifactory-saas.mtb.com
        |
        +-- mtb-docker
              |
              +-- mtb-ubi8-dotnet10-sdk:0.3.8
```

### The important distinction

**The application is NOT being converted into a Docker image by this
build job.**

The `COMPILER_VERSION` image is the **build environment**.

It provides the required .NET SDK/tooling so that the pipeline can
execute:

``` text
dotnet publish
```

The actual application output is then packaged as a ZIP artifact.

------------------------------------------------------------------------

# 11. How the Build Image Is Used

The build flow is:

``` mermaid
flowchart TD
    A[GitLab Runner] --> B[Pull Approved Build Image]
    B --> C[mtb-ubi8-dotnet10-sdk:0.3.8]
    C --> D[Copy NuGet Config]
    D --> E[Restore Dependencies]
    E --> F[dotnet publish]
    F --> G[./publish]
    G --> H[Create ZIP Artifact]
```

The shared template defines the build image using:

``` text
${BAR_REGISTRY}/mtb-docker/${COMPILER_VERSION}
```

The project supplies:

``` text
COMPILER_VERSION=mtb-ubi8-dotnet10-sdk:0.3.8
```

So the pipeline **uses an existing approved build image**. It does not
build that image as part of the shown `build_package` job.

------------------------------------------------------------------------

# 12. How the Application Artifact Is Created

The shared build template performs:

``` bash
mkdir -p ${OUTPUT_PATH}

dotnet publish ${SOLUTION_PATH} ${BUILD_OPTIONS} -o ${OUTPUT_PATH}

cd ${OUTPUT_PATH} && zip -r ${LOCAL_ARTIFACT_NAME} .

mv "${CI_PROJECT_DIR}/${OUTPUT_PATH}/${LOCAL_ARTIFACT_NAME}" "${CI_PROJECT_DIR}/"
```

With the current values, the logical flow is:

``` text
.NET 10 Source Code
        |
        v
dotnet publish
        |
        v
./publish
        |
        v
ZIP
        |
        v
ods-fap-${CI_COMMIT_SHORT_SHA}.zip
```

The ZIP becomes the CI/CD build artifact.

------------------------------------------------------------------------

# 13. Artifact Naming

The shared template defines:

``` text
LOCAL_ARTIFACT_NAME =
${PKG}-${CI_COMMIT_SHORT_SHA}.zip
```

With:

``` text
PKG = ods-fap
```

the artifact will follow the pattern:

``` text
ods-fap-<commit-sha>.zip
```

This provides a commit-specific artifact that can be associated with the
exact source revision.

------------------------------------------------------------------------

# 14. Veracode Artifact

The build template also creates a separate Veracode package:

``` text
veracode-${PKG}-${CI_COMMIT_SHORT_SHA}.zip
```

The template uses the configured Veracode file list.

If the project does not define a specific file list, the shared template
indicates that the default can be the complete project content.

------------------------------------------------------------------------

# 15. Build Artifact Retention

The shared build template defines:

``` text
expire_in: 4 hour
```

for the normal build artifacts.

Therefore the generated build and Veracode artifacts are temporary CI/CD
artifacts unless they are subsequently published to the configured
artifact repository by the applicable shared pipeline jobs.

------------------------------------------------------------------------

# 16. DEV Deployment Functional Flow

The supplied project currently defines:

``` yaml
dev_deploy:
  extends: .deploy_azure_dev
```

and:

``` yaml
needs:
  - build_package
```

Therefore the deployment depends on a successful build.

The functional flow is:

``` mermaid
flowchart LR
    A[build_package] --> B[Successful Build]
    B --> C[ods-fap ZIP Artifact]
    C --> D[dev_deploy]
    D --> E[Azure Authentication]
    E --> F[Azure Subscription]
    F --> G[Target Resource Group]
    G --> H[Azure Function App]
```

The DEV deployment is currently configured as:

``` yaml
rules:
  - when: manual
```

Therefore **DEV deployment is manually triggered**, according to the
supplied project configuration.

------------------------------------------------------------------------

# 17. Complete CI/CD Flow

``` mermaid
flowchart TD
    A[Developer Commit] --> B[GitLab Pipeline]

    B --> C[Shared dotnet-core Template]

    C --> D[Build Package]

    D --> E[Select cicd Runner]
    E --> F[Start Approved .NET 10 SDK Build Image]

    F --> G[Copy NUGET_AUTH to NuGet.Config]
    G --> H[Restore NuGet Packages]
    H --> I[dotnet publish]

    I --> J[Publish Directory]
    J --> K[Create Application ZIP]
    K --> L[Build Artifact]

    L --> M[Security / Quality Pipeline Jobs]
    M --> N[Manual DEV Deploy]

    N --> O[Azure Service Principal Login]
    O --> P[Set Azure Subscription]
    P --> Q[Deploy Azure Function App]
    Q --> R[ODS Azure Function]
```

------------------------------------------------------------------------

# 18. Security and Credential Flow

Sensitive credentials should remain outside source control.

``` mermaid
flowchart LR
    A[GitLab Protected / Masked Variables]
    A --> B[NUGET_AUTH]
    A --> C[ARM_TENANT_ID]
    A --> D[ARM_CLIENT_ID]
    A --> E[ARM_CLIENT_SECRET]
    A --> F[ARM_SUBSCRIPTION_ID]

    B --> G[Build / NuGet Restore]
    C --> H[Azure Login]
    D --> H
    E --> H
    F --> I[Azure Subscription]
    H --> I
    I --> J[Azure Function Deployment]
```

------------------------------------------------------------------------

# 19. Existing Shared Template Rules

The supplied shared templates use reusable rules such as:

``` yaml
!reference [.all_environment_rules, rules]
```

and for migration/deployment examples:

``` yaml
!reference [.lower_environment_rules, rules]
```

``` yaml
!reference [.higher_environment_rules, rules]
```

The migration template shows DEV, CERT and PROD jobs using different
environment rule sets, with migration application configured as manual.

The project should continue to use the approved shared rules rather than
recreating environment-selection logic locally.

------------------------------------------------------------------------

# 20. Security/Quality Jobs

The supplied shared template contains reusable jobs for:

-   SAST
-   Policy scan
-   DAST
-   Aqua scan
-   Advanced Cluster Security
-   Veracode
-   Artifactory publishing

The project does not need to recreate the underlying implementation of
these jobs when the shared template already provides them.

The current project example explicitly defines several scan jobs through
`extends`.

For Aqua scan jobs, the supplied example sets:

``` yaml
REQUIRE_AQUA_SCAN: "false"
```

where applicable.

Any decision to enable/disable a security scan should follow the
enterprise CI/CD standard rather than being changed solely for this
application.

------------------------------------------------------------------------

# 21. Current DEV Pipeline -- Simplified

The project-specific portion can be understood as:

``` text
                    GitLab
                       |
                       v
              Shared CI/CD Template
                       |
                       v
                build_package
                       |
          +------------+------------+
          |                         |
          v                         v
   .NET 10 SDK Image          NuGet Config
          |                         |
          +------------+------------+
                       |
                       v
                dotnet publish
                       |
                       v
                ./publish
                       |
                       v
              ZIP Application
                       |
                       v
             Build Artifact
                       |
                       v
              dev_deploy
              [MANUAL]
                       |
                       v
             Azure Authentication
                       |
                       v
             Azure Function App
```

------------------------------------------------------------------------

# 22. Required Inputs / Information Still Needed

Before finalizing the production-ready CI/CD YAML, confirm the following
with the DevOps/Azure team:

### Azure deployment

-   `ARM_TENANT_ID`
-   `ARM_CLIENT_ID`
-   `ARM_CLIENT_SECRET`
-   `ARM_SUBSCRIPTION_ID`
-   Actual DEV `APP_SERVICE`
-   Actual DEV `RESOURCE_GROUP`

### NuGet

-   Approved `NUGET_AUTH`
-   Approved common/shared NuGet feed
-   DEV NuGet feed, if required
-   CERT NuGet feed, if required

### Application

-   Confirm `SOLUTION_PATH`
-   Confirm `PROJECT_FILE`
-   Confirm `PKG`
-   Confirm `APP_CODE`
-   Confirm `APP_CODE_NAME`
-   Confirm target Azure Function App name
-   Confirm deployment slot

### Environment deployment

The supplied project configuration currently shows DEV deployment.
Confirm the corresponding approved CERT and PROD deployment jobs/rules
before adding them.

------------------------------------------------------------------------

# 23. Important Observation -- Linux vs Windows

The current supplied ODS Function configuration uses:

``` text
BUILD_OPTIONS:
-c Release -r linux-x64 --self-contained false
```

Therefore the current target is a **Linux Azure Function deployment**.

This is different from the earlier generic Windows example where:

``` text
-c Release -r win-x64 --self-contained false
```

was suggested.

For the current ODS Azure Function configuration, **do not change to
`win-x64` unless the Azure Function App hosting plan/runtime is actually
Windows**.

The current project configuration should therefore retain:

``` text
linux-x64
```

unless the hosting target changes.

------------------------------------------------------------------------

# 24. Final Architecture View

The CI/CD architecture can be summarized as:

``` text
Developer
   |
   v
GitLab Repository
   |
   v
Shared Enterprise CI/CD Template
   |
   +-----------------------------+
   |                             |
   v                             v
Build                       Security / Quality
   |                             |
   v                             |
Approved .NET 10 SDK Image       |
   |                             |
   v                             |
NuGet Authentication             |
   |                             |
   v                             |
dotnet publish                   |
   |                             |
   v                             |
ZIP Application Artifact <-------+
   |
   v
Manual DEV Deployment
   |
   v
Azure Service Principal
   |
   v
Azure Subscription
   |
   v
Resource Group
   |
   v
Azure Function App
   |
   v
.NET 10 Application
```

## Key takeaway

There are **two different "artifacts/images" in this CI/CD design**:

1.  **Build Image**\
    `mtb-ubi8-dotnet10-sdk:0.3.8`\
    Used as the containerized build environment. The pipeline uses/pulls
    this approved image.

2.  **Application Artifact**\
    `ods-fap-<commit-sha>.zip`\
    Created by `dotnet publish` followed by ZIP packaging and then used
    for deployment.

The supplied `build_package` job therefore **does not build a Docker
image for the ODS Function App**. It uses an existing .NET SDK container
image to build the application and produces a ZIP deployment artifact.
