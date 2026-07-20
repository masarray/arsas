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
$capabilityBehavior = Read-RequiredFile "FaultRecordUxBehavior.cs"
$rcbBridge = Read-RequiredFile "MainWindow.RcbCapability.cs"

Require-Text $mainXaml 'Click="IedEditRcb_Click"' "IED-card source RCB action is missing before capability-row projection."
Require-Text $mainXaml 'legacy SAS CID import' "IED-card RCB action no longer explains the legacy SAS workflow."
Require-Text $mainCode 'SclReportControlFilter.InspectFile' "Source-backed RCB inventory is missing."
Require-Text $mainCode 'LegacySasSclExporter.WriteFiles' "Object-model legacy SAS CID export is missing."
Require-Text $mainCode 'SclReportControlFilter.FilterLiveModel' "Live-discovery one-RCB fallback is missing."
Require-Text $mainCode 'latestAvailability' "The latest read-only availability evidence is not retained for export."
Require-Text $mainCode 'LiveRcbDataSetEvidenceMerger.MergeSelectedDataSetDirectory' "Live FCDA directory evidence is not merged before selected-RCB export."
Require-Text $mainCode 'AuthoritativeLiveIedSclExporter.WriteFiles' "Live CID export must normalize the physical IED identity separately from MMS Logical Device domains."
Require-Text $mainCode 'IedNameOverride = device.Name' "The IED card identity must be passed as the authoritative SCL IED name."
Reject-Text $mainCode 'Task.Run(() => LiveIedSclExporter.WriteFiles(' "Live selected-RCB export must not bypass authoritative IED identity normalization."
Require-Text $mainXaml 'Adaptive five-slot action bar' "IED-card source action row is no longer protected from clipping."
Require-Text $mainXaml '<UniformGrid Rows="1" Columns="5"' "IED-card source actions must expose five slots before runtime separation."
Require-Text $mainCode 'RemoveUnreferencedDataSets = false' "Safe default must preserve DataSets during selected-RCB export."

# The runtime behavior separates the RCB engineering action from lifecycle controls.
# It must attach the capability row to CardContent, not to the nested lifecycle Grid.
Require-Text $capabilityBehavior 'actionPanel?.Parent is not Grid primaryActionGrid' "Capability behavior no longer identifies the nested lifecycle action Grid."
Require-Text $capabilityBehavior 'primaryActionGrid.Parent is not Grid cardGrid' "Capability row must attach to the outer CardContent grid."
Require-Text $capabilityBehavior 'primaryActionGrid.RowDefinitions.Clear()' "Nested action-grid overlap cleanup is missing."
Require-Text $capabilityBehavior 'actionPanel.Columns = 4' "Play, Stop, Edit and Save must retain four lifecycle slots."
Require-Text $capabilityBehavior 'Grid.SetColumnSpan(capabilityPanel, 2)' "Capability row must span the full IED card width."
Require-Text $capabilityBehavior 'MinWidth = 44' "Capability captions must retain readable rounded-rectangle width."
Require-Text $capabilityBehavior 'Height = 26' "Capability pill height changed unexpectedly."
Require-Text $capabilityBehavior 'TryFindResource("IedIconButton")' "Capability pills must use the compact rounded-rectangle card template."
Reject-Text $capabilityBehavior 'TryFindResource("SoftButton")' "Large SoftButton chrome must not be reused for narrow IED capability captions."
Require-Text $capabilityBehavior '"GOOSE"' "GOOSE capability caption is missing."
Require-Text $capabilityBehavior '"SMV"' "SMV capability caption is missing."
Require-Text $capabilityBehavior '"FILE"' "FILE capability caption is missing."
Require-Text $capabilityBehavior '"RCB"' "RCB capability caption is missing."
Require-Text $rcbBridge 'OpenRcbExportFilter' "Caption RCB action no longer routes to the production export workflow."

Require-Text $windowCode 'RcbExportWindowOptions' "Production RCB window options were removed."
Require-Text $windowCode 'SclSchemaProfile.Edition1V16' "Legacy SAS export must default to Edition 1."
Require-Text $windowCode 'SaveFileDialog' "Production CID destination selection is missing."
Require-Text $windowCode 'Configured IED Description (*.cid)|*.cid' "Legacy SAS selected-RCB output must remain CID."
Require-Text $windowCode 'RequiresConfirmation' "Unknown or ARSAS-active RCB selection must require confirmation."
Require-Text $windowCode 'The source SCL was not modified' "Source immutability must remain visible in failure/cancel handling."
Require-Text $windowCode 'RunAvailabilityCheckAsync(automatic: true)' "Opening the production RCB window must start a lazy read-only availability audit automatically."
Require-Text $windowCode 'IsIndeterminate = true' "Automatic availability audit must present a non-blocking progress animation."
Require-Text $windowCode 'Checking RCB availability' "The automatic audit overlay no longer explains the wait state."
Require-Text $windowCode 'Interval = TimeSpan.FromSeconds(3)' "RCB export success overlay must dismiss itself after three seconds."
Require-Text $windowCode 'ShowSuccessOverlay(completion)' "Successful export must use the in-window success overlay."
Require-Text $windowCode 'Legacy SAS CID exported' "The success overlay no longer communicates completion."
Reject-Text $windowCode 'RCB Export Complete' "Native RCB export success MessageBox must not return."
Reject-Text $windowCode 'Process.Start(new ProcessStartInfo("explorer.exe"' "Successful export must not steal focus by opening Explorer automatically."
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
  ".github/workflows/apply-rcb-card-patch-temp.yml",
  ".github/workflows/apply-ied-card-two-row-fix-temp.yml"
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
  $identityPath = Join-Path $engine "src/AR.Iec61850/Scl/Export/AuthoritativeLiveIedSclExporter.cs"
  foreach ($path in @($availabilityPath, $filterPath, $exportPath, $identityPath)) {
    if (!(Test-Path $path)) { throw "Required ARIEC61850 RCB API is missing: $path" }
  }

  $availability = Get-Content $availabilityPath -Raw
  $filter = Get-Content $filterPath -Raw
  $export = Get-Content $exportPath -Raw
  $identity = Get-Content $identityPath -Raw
  Require-Text $availability 'does not expose enough reservation evidence to prove availability' "Edition 1 BRCB uncertainty guard is missing."
  Require-Text $filter 'var document = new XDocument(source);' "SCL filter must clone rather than mutate the source document."
  Require-Text $filter 'Legacy SAS export requires exactly one selected ReportControl.' "Exactly-one-RCB guard is missing."
  Require-Text $export 'Filtered SCL validation expected one ReportControl' "Post-export single-RCB validation is missing."
  Require-Text $export 'The original source file was not modified.' "Engine evidence no longer states source immutability."
  Require-Text $identity '"ldName",' "Engine identity boundary must write explicit MMS Logical Device names through LDevice.ldName."
  Require-Text $identity 'ValidateIdentity' "Engine must validate the normalized IED and MMS-domain identity mapping."
  Require-Text $identity 'missingDomains' "Engine must reject loss of discovered MMS Logical Device domains."
}

Write-Host "Legacy SAS RCB export, authoritative IED identity, automatic read-only availability, transient success overlay, stable IED card layout, Edition 1 CID default, and engine boundaries passed."
