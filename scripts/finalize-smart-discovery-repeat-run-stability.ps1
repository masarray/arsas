param(
    [Parameter(Mandatory=$true)][string]$GoldenLockPath,
    [Parameter(Mandatory=$true)][string]$RepeatTargetPath,
    [Parameter(Mandatory=$true)][string[]]$RunBundlePaths,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [switch]$AllowFixtureEvidence,
    [switch]$NoFailExit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Get-Int($Object, [string]$Name) {
    if ($null -eq $Object) { return 0 }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return 0 }
    return [int]$property.Value
}

function Get-CanonicalServiceMap($Object) {
    $map = [ordered]@{}
    if ($null -ne $Object) {
        foreach ($property in @($Object.PSObject.Properties | Sort-Object Name)) { $map[$property.Name] = [int]$property.Value }
    }
    return ($map | ConvertTo-Json -Compress)
}

function Get-CanonicalObject($Object, [string[]]$Names) {
    $map = [ordered]@{}
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        $map[$name] = if ($null -eq $property) { $null } else { $property.Value }
    }
    return ($map | ConvertTo-Json -Compress -Depth 6)
}

$goldenLockFile = Resolve-File $GoldenLockPath 'P0-5e golden lock'
$repeatTargetFile = Resolve-File $RepeatTargetPath 'P0-5f repeat target'
$lock = Get-Content -LiteralPath $goldenLockFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $repeatTargetFile -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

if ($lock.Phase -ne 'P0-5e' -or $lock.Status -ne 'locked') { $failures.Add('P0-5f requires an active P0-5e golden lock.') }
if (-not $AllowFixtureEvidence -and -not [bool]$lock.GoldenSource.RawCaptureReverified) { $failures.Add('Production finalization rejects a fixture/non-reverified P0-5e lock.') }
if ($target.Phase -ne 'P0-5f') { $failures.Add('Repeat-run target is not P0-5f authority.') }
if ($target.DeviceIdentity -ne $lock.DeviceIdentity) { $failures.Add('Repeat target device differs from the golden lock.') }
if ($target.EngineCommit -ne $lock.GoldenSource.EngineCommit) { $failures.Add('Repeat target engine differs from the golden lock.') }

$minimumRuns = [int]$target.MinimumIndependentAssociations
if ($minimumRuns -lt 3) { $failures.Add('P0-5f target must require at least three independent associations.') }
if ($RunBundlePaths.Count -lt $minimumRuns) { $failures.Add("Insufficient independent repeat runs: $($RunBundlePaths.Count) < $minimumRuns.") }

$goldenHash = (Get-FileHash -LiteralPath $goldenLockFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = [string]$lock.GoldenSource.SemanticTargetSha256
$manifestHash = [string]$lock.GoldenSource.BuildManifestSha256
$expectedArsas = [string]$lock.GoldenSource.ArsasCommit
$expectedEngine = [string]$lock.GoldenSource.EngineCommit
$maxPeak = [int]$lock.HardRequestBudget.MaxPeakOutstandingRequests
$bundles = @()

foreach ($path in $RunBundlePaths) {
    $file = Resolve-File $path 'P0-5f run bundle'
    $bundle = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    $bundles += [pscustomobject]@{ Path = $file; Hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(); Evidence = $bundle }

    if ($bundle.Phase -ne 'P0-5f-run-bundle' -or $bundle.Verdict -ne 'PASS') { $failures.Add("Run bundle '$file' is not PASS P0-5f evidence.") }
    if (-not $AllowFixtureEvidence -and [bool]$bundle.FixtureEvidence) { $failures.Add("Production finalization rejects fixture run bundle '$file'.") }
    if ($bundle.DeviceIdentity -ne $lock.DeviceIdentity) { $failures.Add("Run bundle '$file' device identity mismatch.") }
    if ($bundle.ArsasCommit -ne $expectedArsas) { $failures.Add("Run bundle '$file' ARSAS commit drift.") }
    if ($bundle.EngineCommit -ne $expectedEngine) { $failures.Add("Run bundle '$file' engine commit drift.") }
    if ($bundle.GoldenLockSha256 -ne $goldenHash) { $failures.Add("Run bundle '$file' is bound to a different golden lock.") }
    if ($bundle.BuildManifestSha256 -ne $manifestHash) { $failures.Add("Run bundle '$file' build manifest drift.") }
    if ($bundle.SemanticTargetSha256 -ne $targetHash) { $failures.Add("Run bundle '$file' semantic target drift.") }
    if ((Get-Int $bundle.Wire 'DuplicateSemanticRequests') -ne 0 -or
        (Get-Int $bundle.Wire 'DuplicateGetNameListRequests') -ne 0 -or
        (Get-Int $bundle.Wire 'DuplicateGvaRequests') -ne 0 -or
        [bool]$bundle.Wire.SecondGetNameListSweepDetected) {
        $failures.Add("Run bundle '$file' contains duplicate/second-sweep wire work.")
    }
    if ((Get-Int $bundle.Runtime.SmartDiscoveryKpi 'DuplicateRequests') -ne 0 -or -not [bool]$bundle.Runtime.SmartDiscoveryKpi.WireAccountingComplete) {
        $failures.Add("Run bundle '$file' engine KPI duplicate/accounting invariant failed.")
    }
    if ((Get-Int $bundle.Wire 'PeakOutstandingRequests') -gt $maxPeak) { $failures.Add("Run bundle '$file' exceeded golden peak outstanding max $maxPeak.") }
}

if ($bundles.Count -gt 0) {
    $first = $bundles[0].Evidence
    $expectedRequests = Get-Int $first.Wire 'ConfirmedRequests'
    $expectedServices = Get-CanonicalServiceMap $first.Wire.ServiceCounts
    $expectedKpiSignature = [string]$first.Runtime.SmartDiscoveryKpi.DeterministicSignature
    $expectedDirectorySignature = [string]$first.Runtime.DirectoryModelSignature
    $expectedProjectionSignature = [string]$first.Runtime.ProjectionSignature
    $typeFields = @(
        'DirectoryPoints','SuppliedLogicalNodeCandidates','SuppressedNonLiveLogicalNodeCandidates',
        'LogicalNodeRequests','PointsCoveredByLogicalNode','DataObjectRequests','PointsCoveredByDataObject',
        'ExactLeafRequests','SuppressedExactRepeatRequests','PointsCoveredByExactLeaf',
        'RemainingUnresolvedPoints','TotalPlannedRequests')
    $modelFields = @('LogicalDevices','LogicalNodes','SemanticPoints','ProjectedSignals','DataSets','ReportControls','BufferedReportControls','UnbufferedReportControls')
    $expectedTypeBudget = Get-CanonicalObject $first.Runtime.TypeProbeBudget $typeFields
    $expectedModel = Get-CanonicalObject $first.Runtime.Model $modelFields

    foreach ($entry in $bundles) {
        $bundle = $entry.Evidence
        if ((Get-Int $bundle.Wire 'ConfirmedRequests') -ne $expectedRequests) { $failures.Add("Confirmed-request drift in '$($entry.Path)'.") }
        if ((Get-CanonicalServiceMap $bundle.Wire.ServiceCounts) -ne $expectedServices) { $failures.Add("MMS service-mix drift in '$($entry.Path)'.") }
        if ([string]$bundle.Runtime.SmartDiscoveryKpi.DeterministicSignature -ne $expectedKpiSignature) { $failures.Add("Engine KPI deterministic-signature drift in '$($entry.Path)'.") }
        if ([string]$bundle.Runtime.DirectoryModelSignature -ne $expectedDirectorySignature) { $failures.Add("Directory model signature drift in '$($entry.Path)'.") }
        if ([string]$bundle.Runtime.ProjectionSignature -ne $expectedProjectionSignature) { $failures.Add("ARSAS signal projection signature drift in '$($entry.Path)'.") }
        if ((Get-CanonicalObject $bundle.Runtime.TypeProbeBudget $typeFields) -ne $expectedTypeBudget) { $failures.Add("Hierarchy type-probe budget drift in '$($entry.Path)'.") }
        if ((Get-CanonicalObject $bundle.Runtime.Model $modelFields) -ne $expectedModel) { $failures.Add("Discovered model-count drift in '$($entry.Path)'.") }
    }

    # The repeat signature must also land on the reviewed same-IED semantic authority.
    $semantic = $target.SemanticTarget
    if ((Get-Int $first.Runtime.Model 'LogicalDevices') -ne [int]$semantic.LogicalDevices) { $failures.Add('Repeat model LogicalDevice count differs from same-IED semantic target.') }
    if ((Get-Int $first.Runtime.Model 'LogicalNodes') -ne [int]$semantic.LogicalNodes) { $failures.Add('Repeat model LogicalNode count differs from same-IED semantic target.') }
    if ((Get-Int $first.Runtime.Model 'SemanticPoints') -ne [int]$semantic.SemanticLeaves) { $failures.Add('Repeat model semantic point count differs from same-IED semantic target.') }
    if ((Get-Int $first.Runtime.Model 'DataSets') -ne [int]$semantic.DataSets) { $failures.Add('Repeat model DataSet count differs from same-IED semantic target.') }
    if ((Get-Int $first.Runtime.Model 'ReportControls') -ne [int]$semantic.RuntimeReportControlInstances) { $failures.Add('Repeat runtime ReportControl count differs from same-IED semantic target.') }
}

$distinctCaptureHashes = @($bundles | ForEach-Object { [string]$_.Evidence.Provenance.CaptureSha256 } | Sort-Object -Unique)
$distinctRuntimeHashes = @($bundles | ForEach-Object { [string]$_.Evidence.Provenance.RuntimeEvidenceSha256 } | Sort-Object -Unique)
if ($bundles.Count -gt 0 -and $distinctCaptureHashes.Count -ne $bundles.Count) { $failures.Add('Repeat set contains reused raw capture evidence; runs are not independent.') }
if ($bundles.Count -gt 0 -and $distinctRuntimeHashes.Count -ne $bundles.Count) { $failures.Add('Repeat set contains reused runtime evidence; runs are not independent.') }

$peaks = @($bundles | ForEach-Object { Get-Int $_.Evidence.Wire 'PeakOutstandingRequests' })
$result = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5f'
    Verdict = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    DeviceIdentity = $lock.DeviceIdentity
    RequiredIndependentAssociations = $minimumRuns
    ObservedRunBundles = $bundles.Count
    GoldenLockSha256 = $goldenHash
    ArsasCommit = $expectedArsas
    EngineCommit = $expectedEngine
    BuildManifestSha256 = $manifestHash
    SemanticTargetSha256 = $targetHash
    Consensus = if ($bundles.Count -gt 0) {
        [ordered]@{
            ConfirmedRequests = Get-Int $bundles[0].Evidence.Wire 'ConfirmedRequests'
            ServiceCounts = $bundles[0].Evidence.Wire.ServiceCounts
            EngineKpiDeterministicSignature = [string]$bundles[0].Evidence.Runtime.SmartDiscoveryKpi.DeterministicSignature
            DirectoryModelSignature = [string]$bundles[0].Evidence.Runtime.DirectoryModelSignature
            ProjectionSignature = [string]$bundles[0].Evidence.Runtime.ProjectionSignature
            TypeProbeBudget = $bundles[0].Evidence.Runtime.TypeProbeBudget
            Model = $bundles[0].Evidence.Runtime.Model
            PeakOutstandingMin = if ($peaks.Count -gt 0) { ($peaks | Measure-Object -Minimum).Minimum } else { $null }
            PeakOutstandingMax = if ($peaks.Count -gt 0) { ($peaks | Measure-Object -Maximum).Maximum } else { $null }
            GoldenPeakOutstandingMax = $maxPeak
        }
    } else { $null }
    Runs = @($bundles | ForEach-Object {
        [ordered]@{
            BundlePath = $_.Path
            BundleSha256 = $_.Hash
            CaptureSha256 = $_.Evidence.Provenance.CaptureSha256
            ProofSha256 = $_.Evidence.Provenance.ProofSha256
            RuntimeEvidenceSha256 = $_.Evidence.Provenance.RuntimeEvidenceSha256
            AssociationGeneration = $_.Evidence.Runtime.AssociationGeneration
            PeakOutstandingRequests = $_.Evidence.Wire.PeakOutstandingRequests
        }
    })
    AcceptanceFailures = @($failures)
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$result | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "P0-5f physical golden repeat-run finalization: $($result.Verdict)"
Write-Host "  runs: $($bundles.Count) / required $minimumRuns"
if ($result.Consensus) {
    Write-Host "  requests: $($result.Consensus.ConfirmedRequests)"
    Write-Host "  KPI signature: $($result.Consensus.EngineKpiDeterministicSignature)"
    Write-Host "  directory signature: $($result.Consensus.DirectoryModelSignature)"
    Write-Host "  projection signature: $($result.Consensus.ProjectionSignature)"
    Write-Host "  peak outstanding range: $($result.Consensus.PeakOutstandingMin)-$($result.Consensus.PeakOutstandingMax) / max $maxPeak"
}
Write-Host "  finalization JSON: $OutputPath"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    if (-not $NoFailExit) { exit 1 }
}
