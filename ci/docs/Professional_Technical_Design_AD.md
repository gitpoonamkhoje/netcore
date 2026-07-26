# Professional Technical Design

# Process Advices Solution -- Azure Data Factory

**Version:** 1.0\
**Document Type:** Technical Design Document (TDD)

------------------------------------------------------------------------

## 1. Overview

The **Process Advices** solution modernizes the existing Windows
Scheduler process using **Azure Data Factory (ADF)**. The ADF pipeline
executes a SQL stored procedure in the **ODS** database (using
**ODREXT** when required), generates a **text file with no file
extension**, stores the output in **Azure File Share**, and makes it
available for review by the **Transmission Team**.

------------------------------------------------------------------------

## 2. Solution Architecture

Place the architecture diagram in the documentation repository.

``` md
![Process Advices Azure Architecture](AD.png)
```

### Architecture Components

  Layer           Responsibility
  --------------- -----------------------------------------
  Schedule        Windows Scheduler / Time-based Trigger
  Orchestration   Azure Data Factory Pipeline
  Databases       ODS (Primary), ODREXT (Supporting)
  Processing      Execute SQL Stored Procedure
  Output          Azure File Share
  Consumer        Transmission Team
  Security        Managed Identity, Azure Key Vault, RBAC
  Monitoring      Azure Monitor, Log Analytics

------------------------------------------------------------------------

## 3. End-to-End Workflow

  -----------------------------------------------------------------------
                    Step Description
  ---------------------- ------------------------------------------------
                       1 Scheduler starts the ADF pipeline.

                       2 ADF reads execution configuration.

                       3 Stored Procedure Activity executes in **ODS**.

                       4 Stored procedure reads **ODREXT** if additional
                         data is required.

                       5 A text output file is generated (no extension).

                       6 Output is copied to Azure File Share.

                       7 Transmission Team reviews the generated file.

                       8 Execution status and diagnostics are recorded.
  -----------------------------------------------------------------------

------------------------------------------------------------------------

## 4. Database Design

### Primary Database

-   **ODS**

### Supporting Database

-   **ODREXT**

The stored procedure generates the outbound advice file using ODS
business data and ODREXT reference data where applicable.

------------------------------------------------------------------------

## 5. Output Storage

**Azure File Share**

Example output files:

``` text
SEI_WTC_ADVICES
SEI_QA_ATCO_ADVICES
```

Example folder:

``` text
/ProcessAdvices/Output/
```

------------------------------------------------------------------------

## 6. Security

-   Managed Identity
-   Azure Key Vault
-   RBAC
-   Private Endpoints (optional)

------------------------------------------------------------------------

## 7. Monitoring

-   Azure Monitor
-   Log Analytics
-   ADF Pipeline Monitoring
-   Activity Run History

------------------------------------------------------------------------

## 8. Scheduling

  Property    Value
  ----------- -----------------------------------------
  Trigger     Time-based Scheduler
  Frequency   Daily / Weekly / Monthly (Configurable)
  Pipeline    ProcessAdvices

------------------------------------------------------------------------

## 9. Deployment

Azure DevOps

``` text
Build
 ↓
Publish
 ↓
Deploy ADF
```

------------------------------------------------------------------------

## 10. Solution Benefits

-   Automated advice generation
-   Centralized orchestration using ADF
-   Secure Azure File Share storage
-   Scalable and maintainable architecture
-   Centralized monitoring and diagnostics
-   Easy access for Transmission Team review

------------------------------------------------------------------------

## References

-   Azure Data Factory
-   Azure SQL Database
-   Azure Files
-   Azure Monitor
-   Azure Key Vault
