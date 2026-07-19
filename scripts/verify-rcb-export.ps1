param(
  [string]$EngineRoot = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Read-RequiredFile([string]$relativePath) {
  $path = Join-Path $root $relativePath
  if (!(Test-Path $path)) {
    throw "Required RCB export file is missing: $relativePath"
  }
  return Get-Content $path -Raw
}

function Require-Text([string]$content, [string]$needle, [string]$message) {
  if ($content.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw $message
  }
}

function Reject-Text([string]$content, [string]$needle, [string]$message) {
  if ($content.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw $message
  }
}

$mainXaml = Read-RequiredFile "MainWindow.xaml"
$mainCode = Read-RequiredFile "MainWindow.RcbExport.cs"
$windowXaml = Read-RequiredFile "RcbExportFilterWindow.xaml"
$windowCode = Read-RequiredFile "RcbExportFilterWindow.xaml.cs"
$models = Read-RequiredFile "Models/RcbExportModels.cs"
$probe = Read-RequiredFile "Services/RcbAvailabilityProbeService.cs"
$edition = Read-RequiredFile "SaveSclWindow.xaml.cs"

Require-Text $mainXaml 'Click="IedEditRcb_Click"' "IED-card RCB Export Filter action is missing."
Require-Text $mainXaml 'legacy SAS CID import' "IED-card RCB action no longer explains the legacy SAS workflow."
Require-Text $mainCode 'SclReportControlFilter.InspectFile' "Source-backed RCB inventory is missing."
Require-Text $mainCode 'LegacySasSclExporter.WriteFiles' "Object-model legacy SAS CID export is missing."
Require-Text $mainCode 'SclReportControlFilter.FilterLiveModel' "Live-discovery one-RCB fallback is missing."
Require-Text $mainCode 'RemoveUnreferencedDataSets = false' "Safe default must preserve DataSets during selected-RCB export."

Require-Text $windowCode 'RcbExportWindowOptions' "Production RCB window options were removed."
Require-Text $windowCode 'SclSchemaProfile.Edition1V16' "Legacy SAS export must default to Edition 1."
Require-Text $windowCode 'SaveFileDialog' "Production CID destination selection is missing."
Require-Text $windowCode 'Configured IED Description (*.cid)|*.cid' "Legacy SAS selected-RCB output must remain CID."
Require-Text $windowCode 'RequiresConfirmation' "Unknown or ARSAS-active RCB selection must require confirmation."
Require-Text $windowCode 'The source SCL was not modified' "Source immutability must remain visible in failure/cancel handling."
Require-Text $windowXaml 'ToolTip="{Binding Reason}"' "RCB status evidence tooltip is missing."
Require-Text $windowXaml 'Original file remains unchanged' "Source immutability statement is missing from the RCB window."

Require-Text $models 'MmsRcbOperationalAvailability.Available' "Available RCB state is missing."
Require-Text $models 'MmsRcbOperationalAvailability.InUse' "In-use RCB state is missing."
Require-Text $models 'MmsRcbOperationalAvailability.Unknown' "Unknown RCB state is missing."
Require-Text $models 'DataSetUnreadable' "Unreadable DataSet state is missing."
Require-Text $models 'OrderByDescending(row => row.MemberCount > 0)' "Populated DataSets must remain at the top of the table."

Require-Text $probe 'No report attribute is written and no RCB is reserved or enabled.' "Read-only availability boundary is missing."
Require-Text $probe 'CallerOwnedRcbReferences = new HashSet<string>' "Caller ownership must not be inferred from signal metadata."
Reject-Text $probe '.Select(point => point.ReportControlReference)' "RCB ownership must not be inferred from point metadata."
Require-Text $edition 'selected-RCB output is saved as an Edition 1 CID file' "Edition selector no longer explains the Edition 1 CID output."

$temporaryWorkflows = @(
  ".github/workflows/full-source-snapshot-temp.yml",
  ".github/workflows/rcb-integration-diagnostic-temp.yml",
  ".github/workflows/apply-rcb-card-patch-temp.yml"
)
foreach ($temporary in $temporaryWorkflows) {
  if (Test-Path (Join-Path $root $temporary)) {
    throw "Temporary RCB implementation workflow must not remain in the final branch: $temporary"
  }
}

if (![string]::IsNullOrWhiteSpace($EngineRoot)) {
  $engine = [System.IO.Path]::GetFullPath($EngineRoot)
  $availabilityPath = Join-Path $engine "src/AR.Iec61850/Mms/MmsRcbAvailability.cs"
  $filterPath = Join-Path $engine "src/AR.Iec61850/Scl/Export/SclReportControlFilter.cs"
  $exportPath = Join-Path $engine "src/AR.Iec61850/Scl/Export/LegacySasSclExporter.cs"
  foreach ($path in @($availabilityPath, $filterPath, $exportPath)) {
    if (!(Test-Path $path)) { throw "Required ARIEC61850 RCB API is missing: $path" }
  }

  $availability = Get-Content $availabilityPath -Raw
  $filter = Get-Content $filterPath -Raw
  $export = Get-Content $exportPath -Raw
  Require-Text $availability 'does not expose enough reservation evidence to prove availability' "Edition 1 BRCB uncertainty guard is missing."
  Require-Text $filter 'var document = new XDocument(source);' "SCL filter must clone rather than mutate the source document."
  Require-Text $filter 'Legacy SAS export requires exactly one selected ReportControl.' "Exactly-one-RCB guard is missing."
  Require-Text $export 'Filtered SCL validation expected one ReportControl' "Post-export single-RCB validation is missing."
  Require-Text $export 'The original source file was not modified.' "Engine evidence no longer states source immutability."
}

Write-Host "Legacy SAS RCB export UX, read-only availability, Edition 1 CID default, and engine boundaries passed."
