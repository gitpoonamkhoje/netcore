APT to PDF Azure Modernization\
High Level Design (HLD)
===============================

## Revision History

| Version | Date  | Author       | Remarks     |
|---------|-------|--------------|-------------|
| 0.1     | Draft | Poonam Khoje | Initial HLD |

## 1. Executive Summary

This HLD describes the Azure modernization of the existing APT-to-PDF
and SPECTR-to-PDF solution while preserving the existing business
workflow.

## 2. Current Workflow (AS-IS)

The following diagram represents the current production workflow and is
retained without functional changes.

<img src="images/media/image2.png" style="width:6in;height:3in" />

## 3. Future (TO-BE) Process workflow

<img src="images/media/image3.png"
style="width:7.02083in;height:4.65625in" />

## 5. Technology Stack

Presentation : Azure Functions (.NET 10)\
Storage : Azure File Share, Blob Storage\
Database : Azure SQL Database\
Security : Managed Identity, Key Vault\
Monitoring : Application Insights, Log Analytics\
CI/CD : Azure DevOps\
Source : Azure DevOps Git

## 6. Existing Technology & Dependency Matrix

| Category | Current | Future | Third Party | Remarks |
|----|----|----|----|----|
| Runtime | .NET | .NET 10 Azure Functions |  | Modernized |
| PDF Library | PDFSharp/TargetStream | Retain or Replace | PDFSharp, TargetStream | TBD |
| Storage | OLS/File | Azure File Share + Blob |  | Azure Native |
| Database | IFR, MTB_APT, MTB_SPECTR | Azure SQL |  | Migration |
| Scheduler | Windows Scheduler | DB Driven Timer |  | No redeployment |
| Source Control | Git | Azure DevOps Git |  | Recommended |
| Monitoring | Legacy | App Insights + Log Analytics |  | Recommended |
