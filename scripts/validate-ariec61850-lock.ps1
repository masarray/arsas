param(
    [Parameter(Mandatory = $true)]
    [string]$LockPath,

    [Parameter(Mandatory = $true)]
    [string]$EngineProject,

    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

$projectRootFull = Resolve-FullPath -Path $ProjectRoot -BasePath (Get-Location).Path
$lockFull = Resolve-FullPath -Path $LockPath -BasePath $projectRootFull
$engineProjectFull = Resolve-FullPath -Path $EngineProject -BasePath $projectRootFull

if (-not (Test-Path -LiteralPath $lockFull -PathType Leaf)) {
    throw "ARIEC61850 integration lock was not found at '$lockFull'."
}

if (-not (Test-Path -LiteralPath $engineProjectFull -PathType Leaf)) {
    throw "ARIEC61850 engine project was not found at '$engineProjectFull'."
}

$lock = Get-Content -LiteralPath $lockFull -Raw | ConvertFrom-Json
$expected = ([string]$lock.commit).Trim().ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($expected) -or $expected.Length -lt 7) {
    throw "ARIEC61850 integration lock '$lockFull' does not contain a valid commit SHA."
}

$engineProjectDirectory = Split-Path -Parent $engineProjectFull
$repoRoot = (& git -C $engineProjectDirectory rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "ARIEC61850 project '$engineProjectFull' is not inside a readable Git checkout; ARSAS cannot prove which engine source is being compiled."
}
$repoRoot = $repoRoot.Trim()

$actual = (& git -C $repoRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($actual)) {
    throw "ARSAS could not read the ARIEC61850 Git revision from '$repoRoot'."
}
$actual = $actual.Trim().ToLowerInvariant()

if ($actual -ne $expected) {
    $message = @"
ARSAS refused to compile against an unpinned ARIEC61850 engine.
Expected by: $lockFull
Expected SHA: $expected
Actual SHA  : $actual
Engine repo : $repoRoot

Synchronize the engine checkout first, for example:
  git -C "$repoRoot" fetch origin
  git -C "$repoRoot" switch main
  git -C "$repoRoot" pull --ff-only
  git -C "$repoRoot" rev-parse HEAD

If main intentionally moved beyond the ARSAS lock, update engines/ARIEC61850.lock.json through a reviewed ARSAS change instead of compiling an arbitrary engine revision.
"@
    throw $message
}

Write-Host "ARIEC61850 lock verified: $actual"
