# Professional Technical Design

# APT-to-PDF Solution -- Azure (.NET 10)

**Version:** 1.0\
**Document Type:** Technical Design Document (TDD)

------------------------------------------------------------------------

## 1. Overview

The APT-to-PDF solution modernizes the existing document conversion
process using Azure-native services and Azure Functions (.NET 10). Files
are received from Azure File Storage, converted to PDF, tracked in Azure
SQL Database, archived, monitored, and secured using Azure platform
services.

------------------------------------------------------------------------

## 2. Solution Architecture

> Add the architecture diagram below when publishing to GitHub or Azure
> DevOps.

``` md
![APT-to-PDF Architecture](AP.png)
```

### Architecture Components

  Layer           Responsibility
  --------------- -----------------------------------------------------
  Input Source    Azure File Storage (Input Share)
  Compute Layer   Azure Function (.NET 10)
  Storage         Input, Processing, Output, Archive, Failed and Logs
  Database        Azure SQL Database (IFR_V2)
  Monitoring      Application Insights & Log Analytics
  Security        Managed Identity, Key Vault & RBAC

------------------------------------------------------------------------

## 3. Processing Workflow

    Step Activity
  ------ ----------------------------------
       1 Read files from Input Share
       2 Download locally
       3 Validate input file
       4 Convert document to PDF
       5 Upload PDF to Output Share
       6 Archive source file
       7 Log successful execution
       8 Update metadata in Azure SQL
       9 Delete working file
      10 Move failed files to Error Share

------------------------------------------------------------------------

## 4. Azure File Storage

  Share        Purpose
  ------------ -------------------------------------
  Input        Incoming documents
  Processing   Temporary working files
  Output       Generated PDF files
  Archive      Successfully processed source files
  Failed       Failed processing
  Logs         Function and audit logs

------------------------------------------------------------------------

## 5. Database Design

**Database:** `IFR_V2`

Primary Business Table

-   MTB_APT

Operational Tables

-   JOB_MASTER
-   JOB_SCHEDULE
-   PROCESS_LOG
-   FILE_METADATA
-   ERROR_LOG

------------------------------------------------------------------------

## 6. Security

-   Managed Identity
-   Azure Key Vault
-   RBAC
-   Private Endpoints

------------------------------------------------------------------------

## 7. Monitoring

-   Application Insights
-   Log Analytics Workspace
-   Azure Monitor Dashboards

Key operational metrics:

-   Execution duration
-   Success / Failure count
-   Processing throughput
-   Error trends

------------------------------------------------------------------------

## 8. Configuration

Scheduling is maintained in Azure SQL Database.

Benefits:

-   No code deployment for schedule changes
-   Dynamic schedule updates
-   Centralized job configuration

------------------------------------------------------------------------

## 9. Deployment

Azure DevOps Pipeline

``` text
Restore
   ↓
Build
   ↓
Unit Test
   ↓
Publish
   ↓
Deploy
```

------------------------------------------------------------------------

## 10. Solution Benefits

-   Azure-native architecture
-   Scalable and resilient processing
-   Centralized monitoring
-   Secure authentication
-   Simplified operations
-   Database-driven scheduling

------------------------------------------------------------------------

## 11. References

-   Azure Functions (.NET 10)
-   Azure Files
-   Azure SQL Database
-   Azure Key Vault
-   Application Insights
-   Azure DevOps
