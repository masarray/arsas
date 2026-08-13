$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
    )

    $content = [System.IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Old)) {
        throw "Expected timestamp formatting pattern was not found in $Path"
    }

    $updated = $content.Replace($Old, $New)
    [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($false))
}

$standardReport = 'Services/IoTesting/IoFatReportLayoutEngine.cs'
Replace-Exact `
    -Path $standardReport `
    -Old 'return evidence.IedTimestamp.Value.ToString("yyyy-MM-dd\nHH:mm:ss.fff", CultureInfo.InvariantCulture);' `
    -New 'return global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.IedTimestamp.Value, "yyyy-MM-dd\nHH:mm:ss.fff");'

$executiveReport = 'Services/IoTesting/IoFatExecutiveReportLayoutEngine.cs'
Replace-Exact `
    -Path $executiveReport `
    -Old '=> evidence?.IedTimestamp?.ToString("yyyy-MM-dd\nHH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "-";' `
    -New '=> global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence?.IedTimestamp, "yyyy-MM-dd\nHH:mm:ss.fff", "-");'

$persistence = 'Services/IoTesting/IoTestProjectPersistenceService.cs'
Replace-Exact `
    -Path $persistence `
    -Old 'var iedTime = evidence.IedTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "not supplied";' `
    -New 'var iedTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.IedTimestamp, "yyyy-MM-dd HH:mm:ss.fff zzz", "not supplied");'

$oldReturn = 'return Html($"IED {iedTime}\nARSAS {evidence.CapturedAt:yyyy-MM-dd HH:mm:ss.fff zzz}\n{evidence.RawValue} · {evidence.Quality} · {evidence.AcquisitionSource}\n{evidence.Verdict}")'
$newReturn = 'var arsasTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.CapturedAt, "yyyy-MM-dd HH:mm:ss.fff zzz");' + [Environment]::NewLine + '        return Html($"IED {iedTime}\nARSAS {arsasTime}\n{evidence.RawValue} · {evidence.Quality} · {evidence.AcquisitionSource}\n{evidence.Verdict}")'
Replace-Exact -Path $persistence -Old $oldReturn -New $newReturn

# Keep the PR clean: remove this one-shot patch machinery in the same generated commit.
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add $standardReport $executiveReport $persistence
git rm -- '.github/workflows/apply-timestamp-rounding-fix.yml' 'scripts/apply-timestamp-rounding-fix.ps1'
git commit -m 'Align FAT reports with nearest-millisecond timestamp presentation'
git push origin HEAD:fix/fat-timestamp-rounding
