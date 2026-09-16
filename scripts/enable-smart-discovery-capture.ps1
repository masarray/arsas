$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\Services\NativeIec61850Client.cs'
$sourcePath = [System.IO.Path]::GetFullPath($sourcePath)
$text = [System.IO.File]::ReadAllText($sourcePath)
$marker = 'DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress)'

if ($text.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
    Write-Host 'Smart discovery capture route already installed.'
    exit 0
}

$pattern = '(public async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsAsync\(CancellationToken cancellationToken, IProgress<IedDiscoveryProgress>\? progress = null\)\s*\{)'
$match = [regex]::Match($text, $pattern)
if (-not $match.Success) {
    throw 'Could not locate NativeIec61850Client.DiscoverSignalsAsync entrypoint.'
}
if ([regex]::Matches($text, $pattern).Count -ne 1) {
    throw 'DiscoverSignalsAsync entrypoint is not unique; refusing ambiguous build-time patch.'
}

$injection = @'

        if (SmartDiscoveryCaptureModeEnabled)
            return await DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress).ConfigureAwait(false);
'@

$patched = $text.Insert($match.Index + $match.Length, $injection)
[System.IO.File]::WriteAllText($sourcePath, $patched, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Installed PR #134 smart discovery capture route into NativeIec61850Client.DiscoverSignalsAsync.'
