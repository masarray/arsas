param(
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\Services\NativeIec61850Client.cs'
$sourcePath = [System.IO.Path]::GetFullPath($sourcePath)
$text = [System.IO.File]::ReadAllText($sourcePath)

$required = @(
    'if (SmartDiscoveryCaptureModeEnabled)',
    'return await DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress).ConfigureAwait(false);',
    '__P0_5C_CONNECT_RESET__',
    '__P0_5C_DISPOSE_RESET__',
    '_lastDiscovery.Snapshot.DomainVariables'
)

$missing = @($required | Where-Object {
    $text.IndexOf($_, [System.StringComparison]::Ordinal) -lt 0
})

if ($missing.Count -eq 0) {
    Write-Host 'Production smart discovery route verification passed: tracked source matches the physical-proven R7 route contract.'
    exit 0
}

if ($VerifyOnly) {
    throw "Production smart discovery route is incomplete. Missing tracked contract marker(s): $($missing -join '; ')"
}

throw @"
The physical-proven Smart Discovery route is no longer permitted to be inserted only at build time.
Repair Services\NativeIec61850Client.cs in tracked source and commit the exact R7 route before building.
Missing marker(s): $($missing -join '; ')
"@
