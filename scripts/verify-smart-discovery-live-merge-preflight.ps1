param(
    [Parameter(Mandatory=$true)][string]$MergeManifestPath,
    [Parameter(Mandatory=$true)][string]$ArsasRepositoryPath,
    [Parameter(Mandatory=$true)][string]$LiveArsasHeadCommit,
    [Parameter(Mandatory=$true)][string]$CurrentArsasBaseCommit,
    [Parameter(Mandatory=$true)][string]$LiveEngineHeadCommit,
    [Parameter(Mandatory=$true)][string]$CurrentEngineBaseCommit,
    [Parameter(Mandatory=$true)][string]$OutputJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}
function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}
function Resolve-Directory([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Container)) { throw "$Label is not a directory: $Path" }
    return $resolved.Path
}
function Test-GitAncestor([string]$RepositoryPath, [string]$Ancestor, [string]$Descendant) {
    & git -C $RepositoryPath merge-base --is-ancestor $Ancestor $Descendant 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}
function Get-GitChangedPaths([string]$RepositoryPath, [string]$BaseCommit, [string]$HeadCommit) {
    $lines = @(& git -C $RepositoryPath diff --name-only "$BaseCommit..$HeadCommit" -- 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git diff failed for $BaseCommit..$HeadCommit." }
    return @($lines | ForEach-Object { ([string]$_).Trim().Replace('\\','/') } | Where-Object { $_ } | Sort-Object -Unique)
}

$manifestFile = Resolve-File $MergeManifestPath 'P0-5h merge manifest'
$repo = Resolve-Directory $ArsasRepositoryPath 'ARSAS repository'
foreach ($entry in @(
    @{ Value = $LiveArsasHeadCommit; Label = 'live ARSAS head' },
    @{ Value = $CurrentArsasBaseCommit; Label = 'current ARSAS base' },
    @{ Value = $LiveEngineHeadCommit; Label = 'live engine head' },
    @{ Value = $CurrentEngineBaseCommit; Label = 'current engine base' })) { Assert-Commit $entry.Value $entry.Label }

$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.Phase -ne 'P0-5h-merge-manifest' -or $manifest.Status -ne 'authorized-for-ordered-merge') { throw 'Live merge preflight requires an authorized P0-5h merge manifest.' }
if ($manifest.MergeMethod -ne 'merge' -or (@($manifest.MergeOrder) -join ',') -ne 'engine,arsas') { throw 'Live merge preflight requires merge-commit method and engine-first order.' }

$validatedArsas = ([string]$manifest.Arsas.ValidatedHeadSha).ToLowerInvariant()
$liveArsas = $LiveArsasHeadCommit.ToLowerInvariant()
$currentArsasBase = $CurrentArsasBaseCommit.ToLowerInvariant()
$expectedEngine = ([string]$manifest.Engine.ExpectedHeadSha).ToLowerInvariant()
$liveEngine = $LiveEngineHeadCommit.ToLowerInvariant()
$currentEngineBase = $CurrentEngineBaseCommit.ToLowerInvariant()
Assert-Commit $validatedArsas 'manifest validated ARSAS head'
Assert-Commit $expectedEngine 'manifest engine head'

$failures = [System.Collections.Generic.List[string]]::new()
if (([string]$manifest.Arsas.BaseShaAtAuthorization).ToLowerInvariant() -ne $currentArsasBase) { $failures.Add('ARSAS base SHA changed after merge authorization.') }
if (([string]$manifest.Engine.BaseShaAtAuthorization).ToLowerInvariant() -ne $currentEngineBase) { $failures.Add('Engine base SHA changed after merge authorization.') }
if ($expectedEngine -ne $liveEngine) { $failures.Add('Engine PR head changed after merge authorization.') }
if (-not (Test-GitAncestor $repo $validatedArsas $liveArsas)) { $failures.Add('Live ARSAS PR head is not a descendant of the P0-5g validated head.') }

$changed = @()
if ($failures.Count -eq 0 -or (Test-GitAncestor $repo $validatedArsas $liveArsas)) {
    $changed = @(Get-GitChangedPaths $repo $validatedArsas $liveArsas)
    $allowed = @($manifest.Arsas.AllowedPostAuthorizationPaths | ForEach-Object { [string]$_ })
    $unexpected = @($changed | Where-Object { $allowed -notcontains $_ })
    if ($unexpected.Count -gt 0) { $failures.Add("ARSAS changed after P0-5g validation outside merge-manifest allowlist: $($unexpected -join ', ').") }
    if ($changed.Count -eq 0 -or $changed -notcontains 'evidence/smart-discovery-mainline-merge-manifest.json') { $failures.Add('Live ARSAS head does not contain the tracked P0-5h merge manifest change.') }
}

$result = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5h-live-preflight'
    Verdict = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    MergeMethod = 'merge'
    ValidatedArsasHead = $validatedArsas
    LiveArsasHead = $liveArsas
    ArsasBaseSha = $currentArsasBase
    ExpectedEngineHead = $expectedEngine
    LiveEngineHead = $liveEngine
    EngineBaseSha = $currentEngineBase
    ArsasPostAuthorizationChangedPaths = @($changed)
    AcceptanceFailures = @($failures)
}
$outputDirectory = Split-Path -Parent $OutputJson
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputJson -Encoding utf8
Write-Host "P0-5h live merge preflight: $($result.Verdict)"
Write-Host "  live engine head: $liveEngine"
Write-Host "  live ARSAS head: $liveArsas"
Write-Host "  changed paths after validation: $($changed -join ', ')"
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    exit 1
}
