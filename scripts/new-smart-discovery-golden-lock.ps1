param(
    [Parameter(Mandatory=$true)][string]$ProofJson,
    [Parameter(Mandatory=$true)][string]$CapturePath,
    [Parameter(Mandatory=$true)][string]$DeviceIdentity,
    [Parameter(Mandatory=$true)][string]$ArsasCommit,
    [Parameter(Mandatory=$true)][string]$EngineCommit,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$BuildManifestPath,
    [switch]$AllowFixtureEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw "$Label is not a file: $Path"
    }
    return $resolved.Path
}

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}

function Get-IntProperty($Object, [string]$Name) {
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

function Assert-EquivalentWireProof($Expected, $Observed) {
    foreach ($name in @(
        'ConfirmedRequests',
        'DuplicateSemanticRequests',
        'DuplicateGetNameListRequests',
        'DuplicateGvaRequests',
        'PeakOutstandingRequests',
        'InvokeIdReuseWhileOutstanding',
        'OrphanResponses',
        'UnansweredRequestsAtCaptureEnd')) {
        $a = Get-IntProperty $Expected $name
        $b = Get-IntProperty $Observed $name
        if ($a -ne $b) { throw "Proof/capture mismatch for ${name}: supplied=$a reverified=$b." }
    }

    if ([bool]$Expected.SecondGetNameListSweepDetected -ne [bool]$Observed.SecondGetNameListSweepDetected) {
        throw 'Proof/capture mismatch for second GetNameList sweep evidence.'
    }
    if ([string]$Expected.ClientIp -ne [string]$Observed.ClientIp -or [string]$Expected.ServerIp -ne [string]$Observed.ServerIp) {
        throw 'Proof/capture endpoint identity mismatch.'
    }

    $expectedServices = Get-ServiceMap $Expected.ServiceCounts
    $observedServices = Get-ServiceMap $Observed.ServiceCounts
    $allNames = @($expectedServices.Keys + $observedServices.Keys | Sort-Object -Unique)
    foreach ($name in $allNames) {
        $a = if ($expectedServices.Contains($name)) { [int]$expectedServices[$name] } else { 0 }
        $b = if ($observedServices.Contains($name)) { [int]$observedServices[$name] } else { 0 }
        if ($a -ne $b) { throw "Proof/capture service mismatch for '$name': supplied=$a reverified=$b." }
    }
}

$proofPath = Resolve-File $ProofJson 'P0-5d proof JSON'
$capture = Resolve-File $CapturePath 'physical capture'
$targetFile = Resolve-File $TargetPath 'same-IED target'
$manifestFile = Resolve-File $BuildManifestPath 'field build manifest'
Assert-Commit $ArsasCommit 'ARSAS commit'
Assert-Commit $EngineCommit 'Engine commit'
if ([string]::IsNullOrWhiteSpace($DeviceIdentity)) { throw 'DeviceIdentity must be non-empty.' }

$proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
if ($proof.Phase -ne 'P0-5d' -or $proof.Verdict -ne 'PASS') {
    throw 'Golden budget can only be created from a P0-5d PASS proof.'
}
$actual = $proof.ArsasCapture
if ($null -eq $actual) { throw 'P0-5d proof does not contain ArsasCapture evidence.' }
if (@($actual.RequestTcpStreams).Count -ne 1) { throw 'Golden capture must contain exactly one MMS request TCP stream.' }
if ((Get-IntProperty $actual 'DuplicateSemanticRequests') -ne 0 -or
    (Get-IntProperty $actual 'DuplicateGetNameListRequests') -ne 0 -or
    (Get-IntProperty $actual 'DuplicateGvaRequests') -ne 0 -or
    [bool]$actual.SecondGetNameListSweepDetected -or
    (Get-IntProperty $actual 'InvokeIdReuseWhileOutstanding') -ne 0 -or
    (Get-IntProperty $actual 'OrphanResponses') -ne 0 -or
    (Get-IntProperty $actual 'UnansweredRequestsAtCaptureEnd') -ne 0) {
    throw 'Golden capture contains duplicate, second-sweep, invoke-ID, orphan, or incomplete-capture evidence.'
}

$extension = [IO.Path]::GetExtension($capture).ToLowerInvariant()
if (-not $AllowFixtureEvidence -and $extension -notin @('.pcap', '.pcapng')) {
    throw 'Production golden lock requires a raw .pcap or .pcapng capture.'
}

# Production default: independently decode the raw capture again before locking it.
# This prevents a PASS proof from capture A being paired with unrelated capture B.
if (-not $AllowFixtureEvidence) {
    $wireVerifier = Join-Path $PSScriptRoot 'verify-smart-discovery-pcap.ps1'
    if (-not (Test-Path -LiteralPath $wireVerifier -PathType Leaf)) {
        throw 'P0-5d verifier is missing beside the P0-5e lock writer.'
    }
    $temporaryProof = Join-Path ([IO.Path]::GetTempPath()) ("p0-5e-reverify-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        & $wireVerifier -PcapPath $capture -OutputJson $temporaryProof -NoFailExit
        $reverified = Get-Content -LiteralPath $temporaryProof -Raw | ConvertFrom-Json
        if ($reverified.Phase -ne 'P0-5d' -or $reverified.Verdict -ne 'PASS') {
            throw 'Raw capture does not independently reproduce a P0-5d PASS.'
        }
        Assert-EquivalentWireProof $actual $reverified.ArsasCapture
    }
    finally {
        Remove-Item -LiteralPath $temporaryProof -Force -ErrorAction SilentlyContinue
    }
}

$manifestText = Get-Content -LiteralPath $manifestFile -Raw
$arsasMatch = [regex]::Match($manifestText, '(?im)^ARSAS commit:\s*([0-9a-f]{40})\s*$')
$engineMatch = [regex]::Match($manifestText, '(?im)^ARIEC61850 commit:\s*([0-9a-f]{40})\s*$')
if (-not $arsasMatch.Success -or -not $engineMatch.Success) {
    throw 'Build manifest does not contain exact ARSAS and ARIEC61850 commit identities.'
}
if ($arsasMatch.Groups[1].Value -ne $ArsasCommit.ToLowerInvariant()) {
    throw 'ARSAS commit argument does not match the field build manifest.'
}
if ($engineMatch.Groups[1].Value -ne $EngineCommit.ToLowerInvariant()) {
    throw 'Engine commit argument does not match the field build manifest.'
}

$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
if ($target.Phase -ne 'P0-5e') { throw 'Same-IED target is not a P0-5e target.' }
if ($target.DeviceIdentity -ne $DeviceIdentity) {
    throw "DeviceIdentity '$DeviceIdentity' does not match target '$($target.DeviceIdentity)'."
}
if ($target.EngineCommit -and $target.EngineCommit -ne $EngineCommit.ToLowerInvariant()) {
    throw 'Engine commit does not match the target baseline.'
}
if ($null -eq $target.SemanticTarget) { throw 'Same-IED target does not contain SemanticTarget authority.' }

$confirmedRequests = Get-IntProperty $actual 'ConfirmedRequests'
if ($confirmedRequests -le 0) { throw 'Golden capture must contain at least one confirmed MMS request.' }
$peakOutstanding = Get-IntProperty $actual 'PeakOutstandingRequests'
$negotiated = $null
if ($null -ne $actual.PSObject.Properties['NegotiatedMaxOutstandingCalling'] -and
    $null -ne $actual.NegotiatedMaxOutstandingCalling -and
    [string]$actual.NegotiatedMaxOutstandingCalling -match '^\d+$') {
    $negotiated = [int]$actual.NegotiatedMaxOutstandingCalling
    if ($peakOutstanding -gt $negotiated) { throw 'Golden peak outstanding exceeds the negotiated calling limit.' }
}

$serviceBudget = Get-ServiceMap $actual.ServiceCounts
if ($serviceBudget.Count -eq 0) { throw 'Golden proof contains no MMS service budget.' }
foreach ($entry in $serviceBudget.GetEnumerator()) {
    if ([int]$entry.Value -lt 0) { throw "Invalid negative service count for '$($entry.Key)'." }
}

$captureHash = (Get-FileHash -LiteralPath $capture -Algorithm SHA256).Hash.ToLowerInvariant()
$proofHash = (Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$maxOutstanding = if ($null -ne $negotiated) { $negotiated } else { $peakOutstanding }

$lock = [ordered]@{
    SchemaVersion = 2
    Phase = 'P0-5e'
    Status = 'locked'
    DeviceIdentity = $DeviceIdentity
    GoldenSource = [ordered]@{
        ArsasCommit = $ArsasCommit.ToLowerInvariant()
        EngineCommit = $EngineCommit.ToLowerInvariant()
        CaptureFileName = [IO.Path]::GetFileName($capture)
        CaptureSha256 = $captureHash
        ProofFileName = [IO.Path]::GetFileName($proofPath)
        ProofSha256 = $proofHash
        BuildManifestFileName = [IO.Path]::GetFileName($manifestFile)
        BuildManifestSha256 = $manifestHash
        SemanticTargetFileName = [IO.Path]::GetFileName($targetFile)
        SemanticTargetSha256 = $targetHash
        ClientIp = $actual.ClientIp
        ServerIp = $actual.ServerIp
        RequestTcpStream = @($actual.RequestTcpStreams)[0]
        ObservedPeakOutstandingRequests = $peakOutstanding
        NegotiatedMaxOutstandingCalling = $negotiated
        RawCaptureReverified = -not [bool]$AllowFixtureEvidence
    }
    HardRequestBudget = [ordered]@{
        MaxConfirmedRequests = $confirmedRequests
        MaxServiceRequests = $serviceBudget
        MaxDuplicateSemanticRequests = 0
        MaxDuplicateGetNameListRequests = 0
        MaxDuplicateGvaRequests = 0
        MaxInvokeIdReuseWhileOutstanding = 0
        MaxOrphanResponses = 0
        MaxUnansweredRequestsAtCaptureEnd = 0
        MaxPeakOutstandingRequests = $maxOutstanding
        ForbidSecondGetNameListSweep = $true
        ForbidUnexpectedServices = $true
    }
    SemanticTarget = $target.SemanticTarget
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$lock | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "P0-5e golden request-budget lock written: $OutputPath"
Write-Host "  capture SHA256: $captureHash"
Write-Host "  build manifest SHA256: $manifestHash"
Write-Host "  semantic target SHA256: $targetHash"
Write-Host "  confirmed request hard max: $confirmedRequests"
Write-Host "  service hard max: $($serviceBudget | ConvertTo-Json -Compress)"
