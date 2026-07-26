# Professional Technical Design

# Statement Compliance Reporting Solution -- Azure Data Factory

**Version:** 1.0\
**Document Type:** Technical Design Document (TDD)

------------------------------------------------------------------------

## 1. Overview

The Statement Compliance Reporting solution is a scheduled Azure Data
Factory (ADF) pipeline that executes a monthly compliance reporting
process. On the **20th of every month**, ADF invokes the stored
procedure **`ols.complience.spr_populate_statement_compliance`** in the
**`IFR_V2`** database to generate the Statement Compliance Report.

The generated report is stored in **Azure File Share**, while email
notifications are sent through an **Azure Function** that integrates
with **Microsoft Graph API**.

------------------------------------------------------------------------

## 2. Solution Architecture

> Reference the architecture diagram when publishing documentation.

``` md
![Statement Compliance Architecture](SCR.png)
```

### Components

  --------------------------------------------------------------------------
  Layer                 Responsibility
  --------------------- ----------------------------------------------------
  Trigger               Monthly ADF Schedule (20th of every month)

  Orchestration         Azure Data Factory Pipeline

  Database              Azure SQL Database (`IFR_V2`)

  Stored Procedure      `ols.complience.spr_populate_statement_compliance`

  Output                Azure File Share

  Notification          Azure Function + Microsoft Graph API

  Security              Managed Identity, Azure Key Vault, RBAC

  Monitoring            Azure Monitor, Log Analytics, Application Insights
  --------------------------------------------------------------------------

------------------------------------------------------------------------

## 3. Processing Workflow

  ----------------------------------------------------------------------------
                    Step Description
  ---------------------- -----------------------------------------------------
                       1 ADF pipeline starts on the 20th of every month.

                       2 Pipeline reads execution configuration.

                       3 Executes
                         `ols.complience.spr_populate_statement_compliance`.

                       4 Validates stored procedure execution status.

                       5 Saves generated Statement Compliance Report to Azure
                         File Share.

                       6 Invokes Azure Function to send email notification
                         using Microsoft Graph API.

                       7 Writes execution details to log tables.

                       8 Pipeline completes successfully or records failure
                         information.
  ----------------------------------------------------------------------------

------------------------------------------------------------------------

## 4. Database Design

**Database:** `IFR_V2`

Stored Procedure:

-   `ols.complience.spr_populate_statement_compliance`

Suggested operational tables:

-   ADF_CONFIGURATION
-   ADF_LOG
-   ADF_EMAIL_CONFIG

------------------------------------------------------------------------

## 5. Output Storage

Azure File Share

``` text
/StatementCompliance/
    StatementCompliance_YYYYMMDDHHMMSS.xlsx
    StatementCompliance_YYYYMMDDHHMMSS.csv
```

------------------------------------------------------------------------

## 6. Notifications

Email notifications are handled by an **Azure Function**.

The Azure Function uses **Microsoft Graph API** to:

-   Send successful execution notifications
-   Send failure notifications
-   Include report location or execution summary

------------------------------------------------------------------------

## 7. Security

-   Managed Identity
-   Azure Key Vault
-   RBAC
-   Private Endpoints

------------------------------------------------------------------------

## 8. Monitoring

-   Azure Monitor
-   Application Insights
-   Log Analytics Workspace
-   ADF Pipeline Monitoring

------------------------------------------------------------------------

## 9. Scheduling

  Property    Value
  ----------- -------------------------------------
  Trigger     Azure Data Factory Schedule Trigger
  Frequency   Monthly
  Day         20
  Time        Configurable

------------------------------------------------------------------------

## 10. Deployment

Azure DevOps Pipeline

``` text
Build
 ↓
Test
 ↓
Release
 ↓
Deploy ADF Pipeline
```

------------------------------------------------------------------------

## 11. Benefits

-   Fully automated monthly execution
-   Database-driven report generation
-   Secure report storage in Azure File Share
-   Azure Function based notification service
-   Centralized monitoring and logging
-   Low operational overhead

------------------------------------------------------------------------

## References

-   Azure Data Factory
-   Azure SQL Database
-   Azure Files
-   Azure Functions
-   Microsoft Graph API
-   Azure Monitor
