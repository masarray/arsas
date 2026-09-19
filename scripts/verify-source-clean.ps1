# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Verifies that every Git-tracked ARSAS path is free from prohibited binaries,
  captures, confidential evidence, external-product identifiers, proprietary assets,
  obsolete license files, and superseded public wording.

.DESCRIPTION
  The gate scans every Git-tracked file. Disallowed external identifiers are
  represented only by one-way fingerprints so the repository itself does not
  publish or repeat unrelated product and company names.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$ForbiddenFilePatterns = @(
    "LICENSE-APACHE-2.0",
    "*EXTERNAL_IP_CLEANLINESS_AUDIT*",
    "*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json",
    "*.nupkg", "*.snupkg", "*.pcap", "*.pcapng", "*.etl", "*.binlog",
    "*.log", "*.tmp", "*.cache", "*.suo", "*.user", "*.rsuser",
    "*.pdf", "*.chm", "*.hlp"
    "30e363d3e8c59f2c1319f8d73d48e3ad26db5e087951a4d7ab809c6f5401aea8",
    "43d7a9de7c6a018c3dfb8a0de38ae060237b942c300db67147e1b56a953b122c",
    "630ba09448af522154f38ef7685ef1f44b0f3e9430f80829a03ce24f400f3754",
    "e5812a10ae5b2cb03cd875c187e116bec38348c6687df3d7ae88736f8d9ad66a",
    "c378ee41c2783d081fb4f6db9fb854a46caafe455d3b64143e2239ef2cecd045",
    "8813d73ee94207a67d5ef2605eb6aebc9108ab61f311e4c76f988a0c24b4ea82",
    "020dae383219bf785518e1af41f1a8f5ed14db6eeb5fa8219d806846cb1129f2",
    "63bbfcfa5a6df7b03a5fd45c6526f66b13fc5a46494a1ed66e4ea70d436a222a",
    "c5f975b35c72cfe2da72009c9866a7ce5eafadce57af395c9c1b6c94b383ed2c",
    "0e357968efdabb541e3e79fac718a1d4fd05bed3b95ccde6995589316afb250c",
    "fa9ab9821527495b9c58ac84f4c00c6a9f82c01c96bfa18809a72b9d91dff228",
    "8fb642282c29a0c6bf16259283ae675b892cf995485215a6ae0d5f1cfa04d2d9",
    "7f92427e2c816b6d22084fd490da8fed3423cc1b9f62407f41c75a0b9a5dfcd9",
    "93161c7754014286c483f69f9c603e906f471b6e3b0d2a0ab950d3db239fdc41",
    "7e175017f49e9d14104795cda400d40eb26bec6a0fd7af54dfe46c67b6352132",
    "c8ee81d53def32998740908ca3f9b45b74eadb61bbf7f46a13f4d6935b5a096c",
    "5c373ed1848509788e445369163f3db4bc550336840f5708079068c46ab1b2e3",
    "32b97c36b6a25a163cb5c420d302c382d99bb277993f42afb8449c91f42084c1",
    "b00fc048863503cada26b5a56f16d6720165f2e7ab3e0cac2c6769675b2a8a4b",
    "76222e0a49d24865c3be20e5516a792fe820050da090b68ced1dc991b2bdf4f7",
    "a41dd0408dfd76082a0ae17338117007b217e22d6c5428654284f3077f809336",
    "ceaec657ec812681d45ba17bb58814b410d4212afa9ba97cefc67179656db96b",
    "1de2ae12bdc9a7e1e269321868f7f655bd166f9c4183a47b88f6b637fa23e673",
    "36e26473a4e8b5ce603a8cb3458b59c39d05fdb87fff30b18b65bad35ed9640e",
    "95bad119f2292fb436837f2bfe73245627cd692a5e16e6f2e73b7e16b5ce7f6d",

)

$ForbiddenIdentifierHashes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    "dee5292b6aa3319833a7fb015d79494b0f1b69c3dc90258b39c042db388ccd71",
    "1343b354d479ded45dde0d7f4ea01daddf1d411a669724f9f3e3de78db038ffc",
    "439003d0d54d022f61a705da700bff916414fdf8308f0cae6a5b9e5903e86fdf",
    "bec65696741e77e0dd0de446b99fe3c069edb3a8f5c81a9939f9813b33e595ea",
    "4ed56753cb552f928aca8147069753f0f3741e28598c56533d8cddcd79fa574e",
    "bbfd365f0891c3e0205503f5d2a1678a0a6ea60d68f3dcc174ed4f60dd87e708",
    "d6a2feb71892b018d0ffec8d3cd438dabe599369d5a1921c7044137146107230",
    "048832a53880fe4fc5feeee9fa0ae445b143c99a956356bee231d3faadbb7af0",
    "0e443fe512c39ce723fc1be519b8e2a13a4ba75916989123078b59308480b2f8"
) | ForEach-Object { [void]$ForbiddenIdentifierHashes.Add($_) }

$CandidateLengths = [System.Collections.Generic.HashSet[int]]::new()
@(5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 17, 22) | ForEach-Object { [void]$CandidateLengths.Add($_) }

$ForbiddenTextPatterns = @(
    "C:\Users\",
    "C:\Program Files\dotnet\sdk",
    "blocked in the current sandbox",
    "_wpftmp",
    "External IP Cleanliness Audit"
)

$TextExtensions = @(
    ".md", ".cs", ".xml", ".xaml", ".ps1", ".py", ".cmd", ".yml", ".yaml",
    ".html", ".css", ".js", ".json", ".webmanifest", ".svg",
    ".props", ".targets", ".sln", ".slnx", ".txt"
)

$Problems = New-Object System.Collections.Generic.List[string]

function Normalize-RelativePath {
    param([Parameter(Mandatory=$true)][string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-Sha256Hex {
    param([Parameter(Mandatory=$true)][string]$Value)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return -join ($algorithm.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $algorithm.Dispose()
    }
}

function Test-ContainsForbiddenIdentifier {
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    $words = @([regex]::Matches($Text.ToLowerInvariant(), '[a-z0-9]+') | ForEach-Object { $_.Value })

    for ($index = 0; $index -lt $words.Count; $index++) {
        $candidate = ""
        for ($count = 1; $count -le 4 -and ($index + $count - 1) -lt $words.Count; $count++) {
            $candidate += $words[$index + $count - 1]
            if ($candidate.Length -gt 22) { break }
            if ($CandidateLengths.Contains($candidate.Length) -and $ForbiddenIdentifierHashes.Contains((Get-Sha256Hex $candidate))) {
                return $true
            }
        }
    }

    return $false
}

function Get-TrackedRelativePaths {
    $paths = @(& git -C $RepoRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate Git-tracked files for clean-room verification."
    }

    return @(
        $paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Normalize-RelativePath $_ }
    )
}

foreach ($relative in (Get-TrackedRelativePaths)) {
    $platformRelative = $relative.Replace([char]'/', [IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $RepoRoot $platformRelative

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $Problems.Add("Tracked path is missing from the worktree: $relative")
        continue
    }

    foreach ($pattern in $ForbiddenFilePatterns) {
        if ($relative -like $pattern) {
            $Problems.Add("Forbidden tracked file: $relative")
            break
        }
    }

    if (Test-ContainsForbiddenIdentifier $relative) {
        $Problems.Add("Forbidden external identifier in path: $relative")
    }

    if ($relative -eq "scripts/verify-source-clean.ps1") { continue }
    if ($TextExtensions -notcontains [IO.Path]::GetExtension($relative).ToLowerInvariant()) { continue }

    $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction SilentlyContinue
    if (Test-ContainsForbiddenIdentifier $content) {
        $Problems.Add("Forbidden external identifier in text: $relative")
    }

    foreach ($pattern in $ForbiddenTextPatterns) {
        if ($content -match [regex]::Escape($pattern)) {
            $Problems.Add("Forbidden internal-release text: $relative")
        }
    }
}

if ($Problems.Count -gt 0) {
    foreach ($problem in ($Problems | Sort-Object -Unique)) {
        Write-Host "ERROR: $problem" -ForegroundColor Red
    }
    throw "ARSAS source tree failed clean-room validation with $($Problems.Count) problem(s)."
}

& (Join-Path $PSScriptRoot "verify-fault-record-bindings.ps1")
& (Join-Path $PSScriptRoot "verify-auto-update.ps1")

Write-Host "All Git-tracked ARSAS content passed source, website, external-IP, current-license, binding, and updater checks." -ForegroundColor Green
