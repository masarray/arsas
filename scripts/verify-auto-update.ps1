# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -LiteralPath (Join-Path $root "AppUpdateService.cs") -Raw
$coordinator = Get-Content -LiteralPath (Join-Path $root "AppUpdateCoordinator.cs") -Raw
$app = Get-Content -LiteralPath (Join-Path $root "App.xaml.cs") -Raw
$prompt = Get-Content -LiteralPath (Join-Path $root "UpdatePromptWindow.xaml.cs") -Raw
$manifest = Get-Content -LiteralPath (Join-Path $root "landing/latest.json") -Raw | ConvertFrom-Json

$requiredServiceMarkers = @(
    'https://masarray.github.io/arsas/latest.json',
    'https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe',
    'SHA256.HashDataAsync',
    'CryptographicOperations.FixedTimeEquals',
    'manifest.Installer.SizeBytes',
    'Uri.UriSchemeHttps',
    '/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART',
    'HttpCompletionOption.ResponseHeadersRead'
)
foreach ($marker in $requiredServiceMarkers) {
    if ($service.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) {
        throw "ARSAS updater security marker is missing: $marker"
    }
}

if ($service -match 'api\.github\.com/repos/.+/releases') {
    throw "The desktop updater must use the landing-domain manifest instead of GitHub release discovery."
}
if ($service -notmatch 'TimeSpan\.FromHours\(12\)') {
    throw "The lazy updater must throttle successful checks."
}
if ($coordinator -notmatch 'TimeSpan\.FromSeconds\(15\)') {
    throw "The updater must remain delayed and non-blocking after startup."
}
if ($coordinator -notmatch 'catch \(Exception exception\)') {
    throw "Update availability failures must remain isolated from application startup."
}
if ($app -notmatch 'DispatcherPriority\.ApplicationIdle') {
    throw "The updater must start only after the WPF UI reaches application idle."
}
if ($prompt -notmatch 'DownloadInstallerAsync' -or $prompt -notmatch 'LaunchInstaller') {
    throw "The update prompt must download, verify, and launch the installer through AppUpdateService."
}

if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'ARSAS' -or $manifest.channel -ne 'stable') {
    throw "landing/latest.json must be a stable ARSAS schema version 1 manifest."
}
if ($manifest.installer.name -ne 'ARSAS-Windows-x64-Setup.exe') {
    throw "landing/latest.json must name the fixed stable installer asset."
}
if ($manifest.installer.url -ne 'https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe') {
    throw "landing/latest.json must use the approved direct installer URL."
}
if ($manifest.installer.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "landing/latest.json must contain a valid installer SHA-256."
}
if ([long]$manifest.installer.sizeBytes -lt 1000000) {
    throw "landing/latest.json must contain the verified installer size."
}

Write-Host "Lazy ARSAS updater lifecycle, trusted URL, size, and SHA-256 guards passed." -ForegroundColor Green
