# Generates docs/IFR-Happy-Paths.docx — standalone IFR project (no cross-repo references)
$ErrorActionPreference = "Stop"
$outPath = Join-Path $PSScriptRoot "IFR-Happy-Paths.docx"

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Add()
$selection = $word.Selection

function Add-Title($text) { $selection.Style = "Title"; $selection.TypeText($text); $selection.TypeParagraph() }
function Add-H1($text) { $selection.Style = "Heading 1"; $selection.TypeText($text); $selection.TypeParagraph() }
function Add-H2($text) { $selection.Style = "Heading 2"; $selection.TypeText($text); $selection.TypeParagraph() }
function Add-Body($text) { $selection.Style = "Normal"; $selection.TypeText($text); $selection.TypeParagraph() }
function Add-Bullet($text) { $selection.Style = "Normal"; $selection.Range.ListFormat.ApplyBulletDefault(); $selection.TypeText($text); $selection.TypeParagraph(); $selection.Range.ListFormat.RemoveNumbers() }
function Add-Number($text) { $selection.Style = "Normal"; $selection.Range.ListFormat.ApplyNumberDefault(); $selection.TypeText($text); $selection.TypeParagraph(); $selection.Range.ListFormat.RemoveNumbers() }

Add-Title "IFR: Investment Financial Reporting - Happy Paths"
Add-Body "Source: IFR Current State process flow diagrams. Standalone IFR Azure project."
Add-Body "Success paths only. Quadient and physical print NOT IN SCOPE."
Add-Body ""

Add-H1 "1. Technology Overview (IFR project)"
Add-Bullet "Azure Data Factory - Replaces Windows Scheduler"
Add-Bullet "Azure Functions - EBRProcess, ProcessEBRSchedule, ProcessAdvices, APT/SPECTR"
Add-Bullet "Azure Files - Trigger files, flat files, extracts"
Add-Bullet "Azure SQL - IFR, ODREXT, IFD_Conv, OLS/IFR_V2"
Add-Bullet "PDFSharp - APT/SPECTR PDF"
Add-Bullet "SEI ODT API - SRDE path"
Add-Bullet "Bulk load / BCP - TABLOAD and TABDUMP patterns"
Add-Body ""

Add-H1 "2. APT and SPECTR"
Add-Number "ADF trigger starts workflow."
Add-Number "Read APT/SPECTR from Azure File Share."
Add-Number "PDFSharp conversion."
Add-Number "Metadata to MTB_APT / MTB_SPECTR."
Add-Number "Archive and complete."
Add-Body ""

Add-H1 "3. EBR"
Add-Number "ADF schedule (Tue-Sat)."
Add-Number "EBRProcess monitors trigger file."
Add-Number "Line Data Input to IFD_Conv."
Add-Number "Complete successfully."
Add-Body ""

Add-H1 "4. SRDE"
Add-H2 "API path"
Add-Number "SEI ODT API to ProcessEBRSchedule to ODREXT."
Add-H2 "TABLOAD path"
Add-Number "SRDE file to IFD_Conv via bulk load."
Add-Body ""

Add-H1 "5. Advices"
Add-Number "ProcessAdvices runs SP in ODREXT."
Add-Number "Text file to Azure Files."
Add-Body ""

Add-H1 "6. Statement Compliance"
Add-Number "Daily: spr_Stmt_Non_Compliant_Daily_Refresh."
Add-Number "TABDUMP to COM11."
Add-Number "Monthly: replaces StmtComplianceRpt.exe on 20th."
Add-Body ""

Add-H1 "7. Out of Scope"
Add-Bullet "Quadient / RR Donnelley FTP"
Add-Bullet "Physical print for Advices"

if (Test-Path $outPath) { Remove-Item $outPath -Force }
$doc.SaveAs2($outPath)
$doc.Close()
$word.Quit()
Write-Output "Created: $outPath"
