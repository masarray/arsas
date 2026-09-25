param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$ManifestPath = Join-Path $RepoRoot "docs\asset-provenance-manifest.json"

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Asset provenance manifest was not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema_version -ne 1) {
    throw "Unsupported asset provenance manifest schema version: $($manifest.schema_version)"
}

$extensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(".png", ".jpg", ".jpeg", ".webp", ".ico", ".svg", ".ttf", ".otf", ".woff2", ".gif") |
    ForEach-Object { [void]$extensions.Add($_) }

$trackedAssets = @(
    & git -C $RepoRoot ls-files |
        ForEach-Object { $_.Replace("\", "/") } |
        Where-Object { $extensions.Contains([IO.Path]::GetExtension($_)) } |
        Sort-Object
)

if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed while validating asset provenance."
}

$manifestFiles = @($manifest.files)
if ($manifestFiles.Count -ne $trackedAssets.Count) {
    throw "Asset provenance manifest count mismatch. Manifest=$($manifestFiles.Count), tracked=$($trackedAssets.Count)."
}

$byPath = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
foreach ($entry in $manifestFiles) {
    $path = [string]$entry.path
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "Asset provenance manifest contains an entry without a path."
    }
    if ($byPath.ContainsKey($path)) {
        throw "Asset provenance manifest contains duplicate path: $path"
    }
    if ([string]::IsNullOrWhiteSpace([string]$entry.review_status)) {
        throw "Asset provenance manifest entry has no review status: $path"
    }
    $byPath.Add($path, $entry)
}

foreach ($relative in $trackedAssets) {
    if (-not $byPath.ContainsKey($relative)) {
        throw "Tracked asset is missing from provenance manifest: $relative"
    }

    $entry = $byPath[$relative]
    $actualSha = (& git -C $RepoRoot hash-object -- $relative).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($actualSha)) {
        throw "Unable to calculate Git blob SHA for tracked asset: $relative"
    }
    if (-not $actualSha.Equals([string]$entry.blob_sha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Asset provenance blob mismatch for $relative. Manifest=$($entry.blob_sha), actual=$actualSha"
    }

    $actualSizeText = (& git -C $RepoRoot cat-file -s $actualSha).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($actualSizeText)) {
        throw "Unable to read Git blob size for tracked asset: $relative"
    }
    $actualSize = [long]$actualSizeText
    if ([long]$entry.size_bytes -ne $actualSize) {
        throw "Asset provenance size mismatch for $relative. Manifest=$($entry.size_bytes), GitBlob=$actualSize"
    }

    $duplicateOf = [string]$entry.duplicate_of
    if (-not [string]::IsNullOrWhiteSpace($duplicateOf)) {
        if (-not $byPath.ContainsKey($duplicateOf)) {
            throw "Asset provenance duplicate_of target does not exist: $relative -> $duplicateOf"
        }
        if (-not ([string]$byPath[$duplicateOf].blob_sha).Equals([string]$entry.blob_sha, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Asset provenance duplicate_of target has a different blob: $relative -> $duplicateOf"
        }
    }
}

$uniqueBlobCount = @($manifestFiles | ForEach-Object { [string]$_.blob_sha } | Sort-Object -Unique).Count
if ([int]$manifest.tracked_asset_count -ne $trackedAssets.Count) {
    throw "tracked_asset_count does not match manifest entries."
}
if ([int]$manifest.unique_blob_count -ne $uniqueBlobCount) {
    throw "unique_blob_count does not match manifest entries."
}

Write-Host "Asset provenance manifest PASS: $($trackedAssets.Count) tracked paths, $uniqueBlobCount unique Git blobs." -ForegroundColor Green
