param(
    [Parameter(Mandatory=$true)][string]$ProofJson,
    [Parameter(Mandatory=$true)][string]$CapturePath,
    [Parameter(Mandatory=$true)][string]$DeviceIdentity,
    [Parameter(Mandatory=$true)][string]$ArsasCommit,
    [Parameter(Mandatory=$true)][string]$EngineCommit,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$TargetPath
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

$proofPath = Resolve-File $ProofJson "P0-5d proof JSON"
$capture = Resolve-File $CapturePath "physical capture"
Assert-Commit $ArsasCommit "ARSAS commit"
Assert-Commit $EngineCommit "Engine commit"
if ([string]::IsNullOrWhiteSpace($DeviceIdentity)) { throw "DeviceIdentity must be non-empty." }

$proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
if ($proof.Phase -ne 'P0-5d' -or $proof.Verdict -ne 'PASS') {
    throw "Golden budget can only be created from a P0-5d PASS proof."
}
$actual = $proof.ArsasCapture
if ($null -eq $actual) { throw "P0-5d proof does not contain ArsasCapture evidence." }
if (@($actual.RequestTcpStreams).Count -ne 1) { throw "Golden capture must contain exactly one MMS request TCP stream." }
if ((Get-IntProperty $actual 'DuplicateSemanticRequests') -ne 0 -or
    (Get-IntProperty $actual 'DuplicateGetNameListRequests') -ne 0 -or
    (Get-IntProperty $actual 'DuplicateGvaRequests') -ne 0 -or
    [bool]$actual.SecondGetNameListSweepDetected -or
    (Get-IntProperty $actual 'InvokeIdReuseWhileOutstanding') -ne 0 -or
    (Get-IntProperty $actual 'OrphanResponses') -ne 0 -or
    (Get-IntProperty $actual 'UnansweredRequestsAtCaptureEnd') -ne 0) {
    throw "Golden capture contains duplicate, second-sweep, invoke-ID, orphan, or incomplete-capture evidence."
}

$confirmedRequests = Get-IntProperty $actual 'ConfirmedRequests'
if ($confirmedRequests -le 0) { throw "Golden capture must contain at least one confirmed MMS request." }
$peakOutstanding = Get-IntProperty $actual 'PeakOutstandingRequests'
$negotiated = $null
if ($null -ne $actual.PSObject.Properties['NegotiatedMaxOutstandingCalling'] -and
    $null -ne $actual.NegotiatedMaxOutstandingCalling -and
    [string]$actual.NegotiatedMaxOutstandingCalling -match '^\d+$') {
    $negotiated = [int]$actual.NegotiatedMaxOutstandingCalling
    if ($peakOutstanding -gt $negotiated) { throw "Golden peak outstanding exceeds the negotiated calling limit." }
}

$serviceBudget = [ordered]@{}
if ($null -ne $actual.ServiceCounts) {
    foreach ($property in @($actual.ServiceCounts.PSObject.Properties | Sort-Object Name)) {
        $count = [int]$property.Value
        if ($count -lt 0) { throw "Invalid negative service count for '$($property.Name)'." }
        $serviceBudget[$property.Name] = $count
    }
}
if ($serviceBudget.Count -eq 0) { throw "Golden proof contains no MMS service budget." }

$target = $null
if (-not [string]::IsNullOrWhiteSpace($TargetPath)) {
    $resolvedTarget = Resolve-File $TargetPath "same-IED target"
    $target = Get-Content -LiteralPath $resolvedTarget -Raw | ConvertFrom-Json
    if ($target.DeviceIdentity -ne $DeviceIdentity) {
        throw "DeviceIdentity '$DeviceIdentity' does not match target '$($target.DeviceIdentity)'."
    }
    if ($target.EngineCommit -and $target.EngineCommit -ne $EngineCommit) {
        throw "Engine commit does not match the target baseline."
    }
}

$captureHash = (Get-FileHash -LiteralPath $capture -Algorithm SHA256).Hash.ToLowerInvariant()
$proofHash = (Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash.ToLowerInvariant()
$maxOutstanding = if ($null -ne $negotiated) { $negotiated } else { $peakOutstanding }

$lock = [ordered]@{
    SchemaVersion = 1
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
        ClientIp = $actual.ClientIp
        ServerIp = $actual.ServerIp
        RequestTcpStream = @($actual.RequestTcpStreams)[0]
        ObservedPeakOutstandingRequests = $peakOutstanding
        NegotiatedMaxOutstandingCalling = $negotiated
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
    SemanticTarget = if ($null -ne $target) { $target.SemanticTarget } else { $null }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$lock | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "P0-5e golden request-budget lock written: $OutputPath"
Write-Host "  capture SHA256: $captureHash"
Write-Host "  confirmed request hard max: $confirmedRequests"
Write-Host "  service hard max: $($serviceBudget | ConvertTo-Json -Compress)"
