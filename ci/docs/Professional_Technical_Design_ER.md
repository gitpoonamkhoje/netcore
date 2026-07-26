# Professional Technical Design

## EBR File Processing Solution -- Azure (.NET 10)

**Version:** 1.0

------------------------------------------------------------------------

## 1. Overview

The EBR File Processing Solution modernizes the existing Windows
Scheduler application into Azure Functions (.NET 10). Files are received
from a File Share, processed, tracked in Azure SQL Database, archived,
logged, and notifications are sent through Microsoft Graph API.

## 2. Architecture

``` md
![EBR Azure Architecture](ER_V1.png)
```

### Components

  Layer           Description
  --------------- -------------------------------------
  Input           File Share
  Compute         Azure Function (.NET 10)
  Storage         Azure File Storage
  Database        Azure SQL Database (IFR_V2)
  Security        Managed Identity, Key Vault, RBAC
  Monitoring      Application Insights, Log Analytics
  Notifications   Microsoft Graph API

## 3. Processing Workflow

1.  Read New Files
2.  Create Batch Run
3.  Validate File
4.  Strip Records (`StripFile.cs`)
5.  Combine Records (`CombineFile.cs`)
6.  Create Recipients
7.  Update Batch Statistics
8.  Create Trigger Files
9.  Logging (`Logging.cs`)
10. Error Handling (`RRDErrors.cs`)
11. Send Email Notification (Microsoft Graph API)

## 4. Storage Design

-   Input File Share
-   Working File Share
-   Output File Share
-   Archive File Share
-   Error File Share

## 5. Database

Database: **IFR_V2**

Primary Table: - MTB_APT

Operational Tables: - JOB_MASTER - JOB_SCHEDULE - PROCESS_LOG -
FILE_METADATA - ERROR_LOG

## 6. Security

-   Managed Identity
-   Azure Key Vault
-   RBAC
-   Private Endpoints

## 7. Monitoring

-   Application Insights
-   Log Analytics Workspace
-   Azure Monitor

## 8. Deployment

Azure DevOps Pipeline

Restore → Build → Test → Publish → Deploy

## 9. Benefits

-   Azure-native
-   Centralized logging
-   Secure authentication
-   Scalable architecture
-   Automated deployment

## 10. Out of Scope

-   TNO file movement
-   Baltimore PDF generation
-   IFD_Conv processing changes
