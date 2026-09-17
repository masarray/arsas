$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\Services\NativeIec61850Client.cs'
$sourcePath = [System.IO.Path]::GetFullPath($sourcePath)
$text = [System.IO.File]::ReadAllText($sourcePath)
$changed = $false

$routeMarker = 'DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress)'
if ($text.IndexOf($routeMarker, [System.StringComparison]::Ordinal) -lt 0) {
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

    $text = $text.Insert($match.Index + $match.Length, $injection)
    $changed = $true
    Write-Host 'Installed PR #134 smart discovery capture route into NativeIec61850Client.DiscoverSignalsAsync.'
}
else {
    Write-Host 'Smart discovery capture route already installed.'
}

$resetMarker = 'ResetSmartDiscoveryAuthority();'
if ($text.IndexOf($resetMarker, [System.StringComparison]::Ordinal) -lt 0) {
    $resetAnchor = "        _lastDiscovery = null;`r`n        _liveModel = null;"
    $anchorIndex = $text.IndexOf($resetAnchor, [System.StringComparison]::Ordinal)
    if ($anchorIndex -lt 0) {
        $resetAnchor = "        _lastDiscovery = null;`n        _liveModel = null;"
        $anchorIndex = $text.IndexOf($resetAnchor, [System.StringComparison]::Ordinal)
    }
    if ($anchorIndex -lt 0) {
        throw 'Could not locate ConnectAsync discovery reset anchor for smart authority invalidation.'
    }
    if ($text.IndexOf($resetAnchor, $anchorIndex + $resetAnchor.Length, [System.StringComparison]::Ordinal) -ge 0) {
        throw 'ConnectAsync discovery reset anchor is not unique; refusing ambiguous smart authority patch.'
    }

    $resetInjection = $resetAnchor + "`r`n        ResetSmartDiscoveryAuthority();"
    if ($resetAnchor.Contains("`n") -and -not $resetAnchor.Contains("`r`n")) {
        $resetInjection = $resetAnchor + "`n        ResetSmartDiscoveryAuthority();"
    }
    $text = $text.Remove($anchorIndex, $resetAnchor.Length).Insert($anchorIndex, $resetInjection)
    $changed = $true
    Write-Host 'Installed explicit smart discovery authority reset into ConnectAsync.'
}
else {
    Write-Host 'Smart discovery authority reset already installed.'
}

$controlAuthorityMarker = '_lastDiscovery.Snapshot.DomainVariables'
if ($text.IndexOf($controlAuthorityMarker, [System.StringComparison]::Ordinal) -lt 0) {
    $controlPattern = '\(\) => service\.OpenAsync\(_session, signal\.ObjectReference, cancellationToken\)'
    $controlMatches = [regex]::Matches($text, $controlPattern)
    if ($controlMatches.Count -ne 1) {
        throw "Expected exactly one control OpenAsync discovery call, found $($controlMatches.Count); refusing ambiguous authority patch."
    }

    $controlReplacement = @'
() => _lastDiscovery != null
                    ? service.OpenAsync(_session, signal.ObjectReference, _lastDiscovery.Snapshot.DomainVariables, cancellationToken)
                    : service.OpenAsync(_session, signal.ObjectReference, cancellationToken)
'@
    $text = [regex]::Replace($text, $controlPattern, $controlReplacement, 1)
    $changed = $true
    Write-Host 'Installed authoritative smart domain inventory reuse into control inspection.'
}
else {
    Write-Host 'Control inspection already reuses authoritative smart domain inventory.'
}

if ($changed) {
    [System.IO.File]::WriteAllText($sourcePath, $text, (New-Object System.Text.UTF8Encoding($false)))
}
