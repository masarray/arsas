param(
    [Parameter(Mandatory=$true)][string]$LockPath,
    [Parameter(Mandatory=$true)][string]$ProofJson,
    [Parameter(Mandatory=$true)][string]$DeviceIdentity,
    [Parameter(Mandatory=$true)][string]$CandidateArsasCommit,
    [Parameter(Mandatory=$true)][string]$CandidateEngineCommit,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [switch]$AllowDifferentArsasCommit,
    [switch]$AllowDifferentEngineCommit,
    [string]$OutputJson,
    [switch]$NoFailExit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Get-IntProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return 0 }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return 0 }
    return [int]$property.Value
}

$lockFile = Resolve-File $LockPath 'P0-5e golden lock'
$proofFile = Resolve-File $ProofJson 'P0-5d proof JSON'
$targetFile = Resolve-File $TargetPath 'same-IED semantic target'
$lock = Get-Content -LiteralPath $lockFile -Raw | ConvertFrom-Json
$proof = Get-Content -LiteralPath $proofFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

if ($lock.Phase -ne 'P0-5e' -or $lock.Status -ne 'locked') { $failures.Add('Golden lock is not an active P0-5e locked contract.') }
if ($proof.Phase -ne 'P0-5d' -or $proof.Verdict -ne 'PASS') { $failures.Add('Candidate evidence must be a P0-5d PASS proof.') }
if ($lock.DeviceIdentity -ne $DeviceIdentity) { $failures.Add("Device identity mismatch: '$DeviceIdentity' != '$($lock.DeviceIdentity)'.") }
if ($target.Phase -ne 'P0-5e' -or $target.DeviceIdentity -ne $DeviceIdentity) { $failures.Add('Candidate semantic target identity does not match the golden device.') }

if ($CandidateArsasCommit -notmatch '^[0-9a-fA-F]{40}$') {
    $failures.Add('Candidate ARSAS commit is not a full 40-character SHA.')
} elseif (-not $AllowDifferentArsasCommit -and $CandidateArsasCommit.ToLowerInvariant() -ne [string]$lock.GoldenSource.ArsasCommit) {
    $failures.Add('Candidate ARSAS commit differs from the golden build baseline. Use -AllowDifferentArsasCommit only for an intentional regression comparison.')
}

if ($CandidateEngineCommit -notmatch '^[0-9a-fA-F]{40}$') {
    $failures.Add('Candidate engine commit is not a full 40-character SHA.')
} elseif (-not $AllowDifferentEngineCommit -and $CandidateEngineCommit.ToLowerInvariant() -ne [string]$lock.GoldenSource.EngineCommit) {
    $failures.Add('Candidate engine commit differs from the golden engine baseline. Use -AllowDifferentEngineCommit only for an intentional regression comparison.')
}

$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($null -eq $lock.GoldenSource.PSObject.Properties['SemanticTargetSha256']) {
    $failures.Add('Golden lock does not carry semantic-target provenance hash.')
} elseif ($targetHash -ne [string]$lock.GoldenSource.SemanticTargetSha256) {
    $failures.Add('Semantic target hash differs from the golden lock authority.')
}
if ($null -eq $lock.GoldenSource.PSObject.Properties['BuildManifestSha256']) {
    $failures.Add('Golden lock does not carry exact build-manifest provenance.')
}
if ($null -eq $lock.SemanticTarget) {
    $failures.Add('Golden lock does not carry same-IED semantic authority.')
} else {
    $lockedSemantic = $lock.SemanticTarget | ConvertTo-Json -Compress -Depth 8
    $targetSemantic = $target.SemanticTarget | ConvertTo-Json -Compress -Depth 8
    if ($lockedSemantic -ne $targetSemantic) { $failures.Add('Semantic target content differs from the golden lock.') }
}

$actual = $proof.ArsasCapture
$budget = $lock.HardRequestBudget
if ($null -eq $actual -or $null -eq $budget) {
    $failures.Add('Candidate proof or hard request budget is missing.')
} else {
    if ((Get-IntProperty $actual 'ConfirmedRequests') -gt [int]$budget.MaxConfirmedRequests) {
        $failures.Add("Confirmed request budget exceeded: $($actual.ConfirmedRequests) > $($budget.MaxConfirmedRequests).")
    }
    if ((Get-IntProperty $actual 'DuplicateSemanticRequests') -gt [int]$budget.MaxDuplicateSemanticRequests) { $failures.Add('Semantic duplicate budget exceeded.') }
    if ((Get-IntProperty $actual 'DuplicateGetNameListRequests') -gt [int]$budget.MaxDuplicateGetNameListRequests) { $failures.Add('GetNameList duplicate budget exceeded.') }
    if ((Get-IntProperty $actual 'DuplicateGvaRequests') -gt [int]$budget.MaxDuplicateGvaRequests) { $failures.Add('GVA duplicate budget exceeded.') }
    if ((Get-IntProperty $actual 'InvokeIdReuseWhileOutstanding') -gt [int]$budget.MaxInvokeIdReuseWhileOutstanding) { $failures.Add('Invoke-ID reuse budget exceeded.') }
    if ((Get-IntProperty $actual 'OrphanResponses') -gt [int]$budget.MaxOrphanResponses) { $failures.Add('Orphan response budget exceeded.') }
    if ((Get-IntProperty $actual 'UnansweredRequestsAtCaptureEnd') -gt [int]$budget.MaxUnansweredRequestsAtCaptureEnd) { $failures.Add('Unanswered request budget exceeded.') }
    if ((Get-IntProperty $actual 'PeakOutstandingRequests') -gt [int]$budget.MaxPeakOutstandingRequests) {
        $failures.Add("Peak outstanding budget exceeded: $($actual.PeakOutstandingRequests) > $($budget.MaxPeakOutstandingRequests).")
    }
    if ([bool]$budget.ForbidSecondGetNameListSweep -and [bool]$actual.SecondGetNameListSweepDetected) { $failures.Add('Second GetNameList sweep is forbidden by the golden lock.') }

    $allowed = $budget.MaxServiceRequests
    if ($null -eq $allowed) { $failures.Add('Golden lock has no service budget.') }
    else {
        $candidateProperties = if ($null -ne $actual.ServiceCounts) { @($actual.ServiceCounts.PSObject.Properties) } else { @() }
        foreach ($property in $candidateProperties) {
            $allowedProperty = $allowed.PSObject.Properties[$property.Name]
            if ($null -eq $allowedProperty) {
                if ([bool]$budget.ForbidUnexpectedServices -and [int]$property.Value -gt 0) {
                    $failures.Add("Unexpected MMS service '$($property.Name)' is not present in the golden budget.")
                }
                continue
            }
            if ([int]$property.Value -gt [int]$allowedProperty.Value) {
                $failures.Add("Service budget exceeded for '$($property.Name)': $($property.Value) > $($allowedProperty.Value).")
            }
        }
    }
}

$result = [ordered]@{
    SchemaVersion = 2
    Phase = 'P0-5e'
    Verdict = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    DeviceIdentity = $DeviceIdentity
    GoldenLock = [ordered]@{
        Path = $lockFile
        CaptureSha256 = $lock.GoldenSource.CaptureSha256
        ProofSha256 = $lock.GoldenSource.ProofSha256
        BuildManifestSha256 = $lock.GoldenSource.BuildManifestSha256
        SemanticTargetSha256 = $lock.GoldenSource.SemanticTargetSha256
        ArsasCommit = $lock.GoldenSource.ArsasCommit
        EngineCommit = $lock.GoldenSource.EngineCommit
        MaxConfirmedRequests = $lock.HardRequestBudget.MaxConfirmedRequests
        MaxServiceRequests = $lock.HardRequestBudget.MaxServiceRequests
    }
    Candidate = [ordered]@{
        ProofPath = $proofFile
        ArsasCommit = $CandidateArsasCommit
        EngineCommit = $CandidateEngineCommit
        SemanticTargetSha256 = $targetHash
        ConfirmedRequests = if ($null -ne $actual) { $actual.ConfirmedRequests } else { $null }
        ServiceCounts = if ($null -ne $actual) { $actual.ServiceCounts } else { $null }
        PeakOutstandingRequests = if ($null -ne $actual) { $actual.PeakOutstandingRequests } else { $null }
    }
    AcceptanceFailures = @($failures)
}

if ([string]::IsNullOrWhiteSpace($OutputJson)) {
    $base = [IO.Path]::GetFileNameWithoutExtension($proofFile)
    $OutputJson = Join-Path ([IO.Path]::GetDirectoryName($proofFile)) "P0-5E-$base-acceptance.json"
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputJson -Encoding utf8

Write-Host "P0-5e golden capture acceptance: $($result.Verdict)"
Write-Host "  device: $DeviceIdentity"
Write-Host "  candidate ARSAS: $CandidateArsasCommit"
Write-Host "  candidate engine: $CandidateEngineCommit"
Write-Host "  candidate requests: $($result.Candidate.ConfirmedRequests); hard max: $($result.GoldenLock.MaxConfirmedRequests)"
Write-Host "  acceptance JSON: $OutputJson"
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    if (-not $NoFailExit) { exit 1 }
}
