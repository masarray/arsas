param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$PublishedDirectory = "",
    [string]$OutputDirectory = "",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a semantic version such as 1.6.16 or v1.6.16."
}

$numericVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
if ([string]::IsNullOrWhiteSpace($PublishedDirectory)) {
    $PublishedDirectory = Join-Path $root "dist\ARSAS-$normalizedVersion-$Runtime"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "dist"
}

$PublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$installerDefinition = Join-Path $root "installer\ArIED61850.iss"

if (-not (Test-Path $PublishedDirectory -PathType Container)) {
    throw "Published application folder was not found: $PublishedDirectory"
}
if (-not (Test-Path $installerDefinition -PathType Leaf)) {
    throw "Inno Setup definition was not found: $installerDefinition"
}

$requiredFiles = @(
    "ARSAS.exe",
    "AR.Iec61850.Transports.Npcap.dll",
    "SharpPcap.dll",
    "PacketDotNet.dll",
    "LICENSE",
    "README.txt"
)
foreach ($file in $requiredFiles) {
    $candidate = Join-Path $PublishedDirectory $file
    if (-not (Test-Path $candidate -PathType Leaf)) {
        throw "Installer source is incomplete. Required file was not found: $candidate"
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $InnoCompiler = $command.Source
    }
    else {
        $candidates = [System.Collections.Generic.List[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"))
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $candidates.Add((Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
            $candidates.Add((Join-Path $env:ChocolateyInstall "bin\ISCC.exe"))
        }
        $InnoCompiler = $candidates | Where-Object { Test-Path $_ -PathType Leaf } | Select-Object -First 1
    }
}
if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup Compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompiler with its full path."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputBaseName = "ARSAS-$normalizedVersion-$Runtime-setup"
$expectedInstaller = Join-Path $OutputDirectory "$outputBaseName.exe"
if (Test-Path $expectedInstaller) {
    Remove-Item $expectedInstaller -Force
}

Write-Host "==> Building Windows installer $normalizedVersion"
Write-Host "    Source: $PublishedDirectory"
Write-Host "    Output: $expectedInstaller"

$arguments = @(
    "/DAppVersion=$normalizedVersion",
    "/DAppVersionNumeric=$numericVersion",
    "/DSourceDir=$PublishedDirectory",
    "/DOutputDir=$OutputDirectory",
    "/DOutputBaseFilename=$outputBaseName",
    $installerDefinition
)

& $InnoCompiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path $expectedInstaller -PathType Leaf)) {
    throw "Inno Setup completed without producing the expected installer: $expectedInstaller"
}

$hash = Get-FileHash -Path $expectedInstaller -Algorithm SHA256
Write-Host "==> Installer: $expectedInstaller"
Write-Host "==> SHA256: $($hash.Hash)"
Write-Output $expectedInstaller
