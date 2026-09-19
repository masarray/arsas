param(
    [Parameter(Mandatory=$true)][string]$GoldenLockPath,
    [Parameter(Mandatory=$true)][string]$ProofJson,
    [Parameter(Mandatory=$true)][string]$CapturePath,
    [Parameter(Mandatory=$true)][string]$RuntimeEvidenceJson,
    [Parameter(Mandatory=$true)][string]$BuildManifestPath,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$DeviceIdentity,
    [Parameter(Mandatory=$true)][string]$ArsasCommit,
    [Parameter(Mandatory=$true)][string]$EngineCommit,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [switch]$AllowFixtureEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}

function Get-Int($Object, [string]$Name) {
    if ($null -eq $Object) { return 0 }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return 0 }
    return [int]$property.Value
}

function Get-ServiceMap($Object) {
    $map = [ordered]@{}
    if ($null -eq $Object) { return $map }
    foreach ($property in @($Object.PSObject.Properties | Sort-Object Name)) {
        $map[$property.Name] = [int]$property.Value
    }
    return $map
}

function Assert-SameServiceMap($Expected, $Observed, [string]$Label) {
    $a = Get-ServiceMap $Expected
    $b = Get-ServiceMap $Observed
    $names = @($a.Keys + $b.Keys | Sort-Object -Unique)
    foreach ($name in $names) {
        $av = if ($a.Contains($name)) { [int]$a[$name] } else { 0 }
        $bv = if ($b.Contains($name)) { [int]$b[$name] } else { 0 }
        if ($av -ne $bv) { throw "${Label}: service '$name' differs: supplied=$av observed=$bv." }
    }
}

$goldenLockFile = Resolve-File $GoldenLockPath 'P0-5e golden lock'
$proofFile = Resolve-File $ProofJson 'P0-5d proof'
$captureFile = Resolve-File $CapturePath 'repeat raw capture'
$runtimeFile = Resolve-File $RuntimeEvidenceJson 'P0-5f runtime evidence'
$manifestFile = Resolve-File $BuildManifestPath 'field build manifest'
$targetFile = Resolve-File $TargetPath 'same-IED semantic target'
Assert-Commit $ArsasCommit 'ARSAS commit'
Assert-Commit $EngineCommit 'Engine commit'

$arsasCommitNormalized = $ArsasCommit.ToLowerInvariant()
$engineCommitNormalized = $EngineCommit.ToLowerInvariant()
$lock = Get-Content -LiteralPath $goldenLockFile -Raw | ConvertFrom-Json
$proof = Get-Content -LiteralPath $proofFile -Raw | ConvertFrom-Json
$runtime = Get-Content -LiteralPath $runtimeFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json

if ($lock.Phase -ne 'P0-5e' -or $lock.Status -ne 'locked') { throw 'P0-5f requires an active P0-5e golden lock.' }
if (-not $AllowFixtureEvidence -and -not [bool]$lock.GoldenSource.RawCaptureReverified) {
    throw 'Production P0-5f requires a P0-5e lock created from independently reverified physical capture evidence.'
}
if ($lock.DeviceIdentity -ne $DeviceIdentity -or $target.DeviceIdentity -ne $DeviceIdentity) {
    throw 'Repeat-run device identity does not match golden/target authority.'
}
if ([string]$lock.GoldenSource.ArsasCommit -ne $arsasCommitNormalized) { throw 'Repeat-run ARSAS commit differs from the golden lock.' }
if ([string]$lock.GoldenSource.EngineCommit -ne $engineCommitNormalized) { throw 'Repeat-run engine commit differs from the golden lock.' }
if ([string]$target.EngineCommit -ne $engineCommitNormalized) { throw 'Repeat-run engine commit differs from the same-IED target.' }

$manifestHash = (Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ([string]$lock.GoldenSource.BuildManifestSha256 -ne $manifestHash) { throw 'Field build manifest hash differs from the golden lock.' }
if ([string]$lock.GoldenSource.SemanticTargetSha256 -ne $targetHash) { throw 'Semantic target hash differs from the golden lock.' }

$manifestText = Get-Content -LiteralPath $manifestFile -Raw
$manifestArsas = [regex]::Match($manifestText, '(?im)^ARSAS commit:\s*([0-9a-f]{40})\s*$')
$manifestEngine = [regex]::Match($manifestText, '(?im)^ARIEC61850 commit:\s*([0-9a-f]{40})\s*$')
if (-not $manifestArsas.Success -or -not $manifestEngine.Success) { throw 'Build manifest is missing exact ARSAS/engine commit identity.' }
if ($manifestArsas.Groups[1].Value -ne $arsasCommitNormalized -or $manifestEngine.Groups[1].Value -ne $engineCommitNormalized) {
    throw 'Build manifest commit identity does not match repeat-run inputs.'
}

if ($proof.Phase -ne 'P0-5d' -or $proof.Verdict -ne 'PASS') { throw 'Repeat run requires a P0-5d PASS proof.' }
if ($runtime.Phase -ne 'P0-5f-run' -or $runtime.SchemaVersion -ne 1) { throw 'Runtime evidence is not a supported P0-5f-run snapshot.' }
if ([string]$runtime.DeviceIdentity -ne $DeviceIdentity) { throw 'Runtime evidence device identity mismatch.' }
if ([string]$runtime.EngineCommit -ne $engineCommitNormalized) { throw 'Runtime evidence engine commit mismatch.' }
if ($null -eq $runtime.SmartDiscoveryKpi -or -not [bool]$runtime.SmartDiscoveryKpi.WireAccountingComplete) {
    throw 'Runtime evidence does not have complete smart-discovery wire accounting.'
}
if ((Get-Int $runtime.SmartDiscoveryKpi 'DuplicateRequests') -ne 0) { throw 'Runtime engine KPI reports duplicate smart-discovery requests.' }
if ([string]::IsNullOrWhiteSpace([string]$runtime.SmartDiscoveryKpi.DeterministicSignature)) { throw 'Runtime engine KPI deterministic signature is missing.' }
if ([string]::IsNullOrWhiteSpace([string]$runtime.Model.DirectoryModelSignature) -or
    [string]::IsNullOrWhiteSpace([string]$runtime.Model.ProjectionSignature)) {
    throw 'Runtime model/projection signature is missing.'
}
if ($null -eq $runtime.TypeProbeBudget) { throw 'Runtime hierarchy type-probe budget is missing.' }

# Re-validate the P0-5d proof against the locked request budget and exact build identity.
$goldenVerifier = Join-Path $PSScriptRoot 'verify-smart-discovery-golden-lock.ps1'
if (-not (Test-Path -LiteralPath $goldenVerifier -PathType Leaf)) { throw 'P0-5e golden verifier is missing.' }
$goldenAcceptance = Join-Path ([IO.Path]::GetTempPath()) ("p0-5f-golden-{0}.json" -f [Guid]::NewGuid().ToString('N'))
try {
    & $goldenVerifier `
        -LockPath $goldenLockFile `
        -ProofJson $proofFile `
        -DeviceIdentity $DeviceIdentity `
        -CandidateArsasCommit $arsasCommitNormalized `
        -CandidateEngineCommit $engineCommitNormalized `
        -TargetPath $targetFile `
        -OutputJson $goldenAcceptance `
        -NoFailExit
    $accepted = Get-Content -LiteralPath $goldenAcceptance -Raw | ConvertFrom-Json
    if ($accepted.Verdict -ne 'PASS') {
        throw "Repeat run exceeds the P0-5e golden lock: $(@($accepted.AcceptanceFailures) -join '; ')"
    }
}
finally {
    Remove-Item -LiteralPath $goldenAcceptance -Force -ErrorAction SilentlyContinue
}

# Production default independently decodes the supplied raw capture again. This binds
# the run bundle to real wire evidence rather than trusting a detached proof JSON.
$extension = [IO.Path]::GetExtension($captureFile).ToLowerInvariant()
if (-not $AllowFixtureEvidence -and $extension -notin @('.pcap', '.pcapng')) { throw 'Production repeat evidence requires raw .pcap/.pcapng input.' }
if (-not $AllowFixtureEvidence) {
    $wireVerifier = Join-Path $PSScriptRoot 'verify-smart-discovery-pcap.ps1'
    $reproofPath = Join-Path ([IO.Path]::GetTempPath()) ("p0-5f-reproof-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        & $wireVerifier -PcapPath $captureFile -OutputJson $reproofPath -NoFailExit
        $reproof = Get-Content -LiteralPath $reproofPath -Raw | ConvertFrom-Json
        if ($reproof.Verdict -ne 'PASS') { throw 'Raw repeat capture does not independently reproduce a P0-5d PASS.' }
        foreach ($name in @('ConfirmedRequests','DuplicateSemanticRequests','DuplicateGetNameListRequests','DuplicateGvaRequests','PeakOutstandingRequests','InvokeIdReuseWhileOutstanding','OrphanResponses','UnansweredRequestsAtCaptureEnd')) {
            $a = Get-Int $proof.ArsasCapture $name
            $b = Get-Int $reproof.ArsasCapture $name
            if ($a -ne $b) { throw "Proof/raw-capture mismatch for ${name}: proof=$a redecoded=$b." }
        }
        Assert-SameServiceMap $proof.ArsasCapture.ServiceCounts $reproof.ArsasCapture.ServiceCounts 'Proof/raw-capture mismatch'
    }
    finally {
        Remove-Item -LiteralPath $reproofPath -Force -ErrorAction SilentlyContinue
    }
}

$wireRequests = Get-Int $proof.ArsasCapture 'ConfirmedRequests'
$engineRequests = Get-Int $runtime.SmartDiscoveryKpi 'TotalRequests'
if ($wireRequests -ne $engineRequests) {
    throw "Wire/engine request accounting mismatch: PCAP=$wireRequests engine=$engineRequests."
}
if ((Get-Int $proof.ArsasCapture 'DuplicateSemanticRequests') -ne 0 -or
    (Get-Int $proof.ArsasCapture 'DuplicateGetNameListRequests') -ne 0 -or
    (Get-Int $proof.ArsasCapture 'DuplicateGvaRequests') -ne 0 -or
    [bool]$proof.ArsasCapture.SecondGetNameListSweepDetected) {
    throw 'Repeat wire proof contains duplicate or second-sweep traffic.'
}

$captureHash = (Get-FileHash -LiteralPath $captureFile -Algorithm SHA256).Hash.ToLowerInvariant()
$proofHash = (Get-FileHash -LiteralPath $proofFile -Algorithm SHA256).Hash.ToLowerInvariant()
$runtimeHash = (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash.ToLowerInvariant()
$goldenHash = (Get-FileHash -LiteralPath $goldenLockFile -Algorithm SHA256).Hash.ToLowerInvariant()

$bundle = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5f-run-bundle'
    Verdict = 'PASS'
    DeviceIdentity = $DeviceIdentity
    ArsasCommit = $arsasCommitNormalized
    EngineCommit = $engineCommitNormalized
    GoldenLockSha256 = $goldenHash
    BuildManifestSha256 = $manifestHash
    SemanticTargetSha256 = $targetHash
    FixtureEvidence = [bool]$AllowFixtureEvidence
    Provenance = [ordered]@{
        CaptureFileName = [IO.Path]::GetFileName($captureFile)
        CaptureSha256 = $captureHash
        ProofFileName = [IO.Path]::GetFileName($proofFile)
        ProofSha256 = $proofHash
        RuntimeEvidenceFileName = [IO.Path]::GetFileName($runtimeFile)
        RuntimeEvidenceSha256 = $runtimeHash
    }
    Wire = [ordered]@{
        ClientIp = $proof.ArsasCapture.ClientIp
        ServerIp = $proof.ArsasCapture.ServerIp
        ConfirmedRequests = $wireRequests
        ServiceCounts = $proof.ArsasCapture.ServiceCounts
        PeakOutstandingRequests = Get-Int $proof.ArsasCapture 'PeakOutstandingRequests'
        NegotiatedMaxOutstandingCalling = $proof.ArsasCapture.NegotiatedMaxOutstandingCalling
        DuplicateSemanticRequests = Get-Int $proof.ArsasCapture 'DuplicateSemanticRequests'
        DuplicateGetNameListRequests = Get-Int $proof.ArsasCapture 'DuplicateGetNameListRequests'
        DuplicateGvaRequests = Get-Int $proof.ArsasCapture 'DuplicateGvaRequests'
        SecondGetNameListSweepDetected = [bool]$proof.ArsasCapture.SecondGetNameListSweepDetected
    }
    Runtime = [ordered]@{
        AssociationGeneration = [long]$runtime.AssociationGeneration
        DirectoryModelSignature = [string]$runtime.Model.DirectoryModelSignature
        ProjectionSignature = [string]$runtime.Model.ProjectionSignature
        Model = $runtime.Model
        SmartDiscoveryKpi = $runtime.SmartDiscoveryKpi
        TypeProbeBudget = $runtime.TypeProbeBudget
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$bundle | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "P0-5f repeat-run bundle: PASS"
Write-Host "  output: $OutputPath"
Write-Host "  requests: $wireRequests"
Write-Host "  KPI signature: $($runtime.SmartDiscoveryKpi.DeterministicSignature)"
Write-Host "  directory signature: $($runtime.Model.DirectoryModelSignature)"
Write-Host "  projection signature: $($runtime.Model.ProjectionSignature)"
