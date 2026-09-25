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
param(
    [string]$RepositoryRoot,
    [switch]$ScanOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$ForbiddenFilePatterns = @(
    "LICENSE-APACHE-2.0",
    "*EXTERNAL_IP_CLEANLINESS_AUDIT*",
    "*.dll", "*.exe", "*.pdb", "*.deps.json", "*.runtimeconfig.json",
    "*.nupkg", "*.snupkg", "*.pcap", "*.pcapng", "*.etl", "*.binlog",
    "*.log", "*.tmp", "*.cache", "*.suo", "*.user", "*.rsuser",
    "*.pdf", "*.chm", "*.hlp"
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

# No tracked path receives a whole-file external-identifier exemption. Historical
# comparison evidence is linked by immutable commit rather than copied into active files.
$Problems = New-Object System.Collections.Generic.List[string]

function Normalize-RelativePath {
    param([Parameter(Mandatory=$true)][string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

# Keep the one-way fingerprint policy; execute the exhaustive substring scan in
# compiled .NET code rather than millions of PowerShell pipeline/function calls.
# The matcher preserves the original four-token, exact-length and embedded-token rules.
$MatcherSource = @'
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ArsasSourceClean
{
    public sealed class IdentifierMatcher : IDisposable
    {
        private static readonly Regex Words = new Regex("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly HashSet<string> hashes;
        private readonly HashSet<int> lengths;
        private readonly Dictionary<string, bool> cache = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly SHA256 sha = SHA256.Create();

        public IdentifierMatcher(string[] forbiddenHashes, int[] candidateLengths)
        {
            hashes = new HashSet<string>(forbiddenHashes, StringComparer.OrdinalIgnoreCase);
            lengths = new HashSet<int>(candidateLengths);
        }

        private bool IsForbidden(string candidate)
        {
            if (!lengths.Contains(candidate.Length)) return false;
            bool found;
            if (cache.TryGetValue(candidate, out found)) return found;
            string digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(candidate))).Replace("-", "");
            found = hashes.Contains(digest);
            cache[candidate] = found;
            return found;
        }

        public bool ContainsForbiddenIdentifier(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            MatchCollection words = Words.Matches(text.ToLowerInvariant());
            for (int index = 0; index < words.Count; index++)
            {
                string word = words[index].Value;
                foreach (int length in lengths)
                {
                    for (int offset = 0; offset <= word.Length - length; offset++)
                    {
                        if (IsForbidden(word.Substring(offset, length))) return true;
                    }
                }

                string candidate = "";
                for (int count = 1; count <= 4 && index + count - 1 < words.Count; count++)
                {
                    candidate += words[index + count - 1].Value;
                    if (candidate.Length > 22) break;
                    if (IsForbidden(candidate)) return true;
                }
            }
            return false;
        }

        public void Dispose() { sha.Dispose(); }
    }
}
'@
Add-Type -TypeDefinition $MatcherSource -Language CSharp -ErrorAction Stop
$IdentifierMatcher = [ArsasSourceClean.IdentifierMatcher]::new(
    [string[]]@($ForbiddenIdentifierHashes), [int[]]@(7, 8, 12, 22))

function Test-ContainsForbiddenIdentifier {
    param([AllowEmptyString()][string]$Text)
    return $IdentifierMatcher.ContainsForbiddenIdentifier($Text)
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

if (-not $ScanOnly) {
    & (Join-Path $PSScriptRoot "verify-asset-provenance-manifest.ps1") -RepositoryRoot $RepoRoot
    & (Join-Path $PSScriptRoot "verify-fault-record-bindings.ps1")
    & (Join-Path $PSScriptRoot "verify-auto-update.ps1")
}

$IdentifierMatcher.Dispose()
Write-Host "All Git-tracked ARSAS content passed source and external-identifier checks." -ForegroundColor Green
