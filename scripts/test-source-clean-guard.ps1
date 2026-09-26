# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
<#
Tests the complete source-clean scanner against temporary Git-tracked fixtures.
Construct the known test identifier from code points so this test file itself
does not need an exemption from the same clean-room gate.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scanner = Join-Path $PSScriptRoot "verify-source-clean.ps1"
$identifier = -join (@(73, 69, 68, 83, 99, 111, 117, 116) | ForEach-Object { [char]$_ })
$cases = @(
    @{ Path = "Services/Fixture.cs"; Text = "public sealed class ${identifier}Fixture {}"; Expected = "text" },
    @{ Path = "docs/reference.md"; Text = "# $identifier"; Expected = "text" },
    @{ Path = "evidence/fixture.json"; Text = "{`"reference`": `"$identifier`"}"; Expected = "text" },
    @{ Path = ".github/workflows/smart-discovery-post-merge-production.yml"; Text = "name: $identifier"; Expected = "text" },
    @{ Path = "NOTICE"; Text = "Policy marker: $identifier"; Expected = "text" },
    @{ Path = ".gitignore"; Text = "# $identifier"; Expected = "text" },
    @{ Path = "tests/ARSAS.Tests/SyntheticFixture.cs"; Text = "// $identifier"; Expected = "text" },
    @{ Path = "docs/${identifier}-fixture.md"; Text = "# independently generated fixture"; Expected = "path" },
    @{ Path = "docs/split-name.md"; Text = $identifier.Substring(0, 3) + " " + $identifier.Substring(3); Expected = "text" },
    @{ Path = "docs/mixed-case.md"; Text = $identifier.ToUpperInvariant(); Expected = "text" }
)

function Invoke-Case {
    param(
        [Parameter(Mandatory=$true)][string]$RelativePath,
        [Parameter(Mandatory=$true)][string]$Content,
        [Parameter(Mandatory=$true)][bool]$MustReject,
        [string]$Expected = "text"
    )
    $root = Join-Path ([IO.Path]::GetTempPath()) ("arsas-clean-room-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw "Fixture Git initialization failed." }
        $file = Join-Path $root ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
        [IO.File]::WriteAllText($file, $Content, [Text.UTF8Encoding]::new($false))
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw "Fixture Git staging failed." }

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = "powershell.exe"
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true

        $quotedScanner = '"' + $scanner.Replace('"', '\"') + '"'
        $quotedRoot = '"' + $root.Replace('"', '\"') + '"'
        $startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File $quotedScanner -RepositoryRoot $quotedRoot -ScanOnly"

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Source-clean fixture scanner failed to start."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        $output = @($stdout, $stderr) -join [Environment]::NewLine
        $process.Dispose()
        if ($MustReject) {
            if ($exitCode -eq 0 -or $output -notmatch ("Forbidden external identifier in " + $Expected)) {
                throw "Source-clean unexpectedly accepted a forbidden $Expected fixture: $RelativePath; exit=$exitCode; output=$output"
            }
        }
        elseif ($exitCode -ne 0) {
            throw "Source-clean rejected a neutral fixture: $RelativePath; exit=$exitCode; output=$output"
        }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

foreach ($case in $cases) {
    Invoke-Case -RelativePath $case.Path -Content $case.Text -MustReject $true -Expected $case.Expected
}
Invoke-Case -RelativePath "docs/synthetic-reference.md" -Content "# ARSAS independent IEC 61850 synthetic evidence" -MustReject $false
# Verify the GitHub PR event gate without placing any disallowed identifier
# literally in this repository, and without changing the existing file fixtures.
function Invoke-PrMetadataCase {
    param(
        [Parameter(Mandatory=$true)][string]$Field,
        [Parameter(Mandatory=$true)][string]$Value,
        [Parameter(Mandatory=$true)][bool]$MustReject
    )
    $root = Join-Path ([IO.Path]::GetTempPath()) ("arsas-pr-metadata-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw "Metadata fixture Git initialization failed." }
        [IO.File]::WriteAllText(
            (Join-Path $root "README.md"), "# Synthetic IEC 61850 metadata fixture",
            [Text.UTF8Encoding]::new($false))
        & git -C $root add README.md
        if ($LASTEXITCODE -ne 0) { throw "Metadata fixture Git staging failed." }

        $event = @{ pull_request = @{ title = "Synthetic ARSAS update"; body = "Independent engineering change" } }
        $event.pull_request[$Field] = $Value
        $eventPath = Join-Path $root "event.json"
        [IO.File]::WriteAllText($eventPath, ($event | ConvertTo-Json -Depth 4),
            [Text.UTF8Encoding]::new($false))

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = "powershell.exe"
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.EnvironmentVariables["GITHUB_EVENT_NAME"] = "pull_request"
        $startInfo.EnvironmentVariables["GITHUB_EVENT_PATH"] = $eventPath
        $startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scanner`" -RepositoryRoot `"$root`" -ScanOnly"

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw "PR metadata fixture scanner failed to start." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = @($stdoutTask.GetAwaiter().GetResult(), $stderrTask.GetAwaiter().GetResult()) -join [Environment]::NewLine
        $exitCode = $process.ExitCode
        $process.Dispose()
        if ($MustReject) {
            if ($exitCode -eq 0 -or $output -notmatch ("Forbidden external identifier in PR " + $Field)) {
                throw "Source-clean accepted a forbidden PR metadata fixture: $Field; exit=$exitCode; output=$output"
            }
        }
        elseif ($exitCode -ne 0) {
            throw "Source-clean rejected neutral PR metadata: $Field; exit=$exitCode; output=$output"
        }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-PrMetadataCase -Field "title" -Value $identifier -MustReject $true
Invoke-PrMetadataCase -Field "body" -Value ("Protocol evidence from " + $identifier) -MustReject $true
Invoke-PrMetadataCase -Field "title" -Value "Neutral engineering update" -MustReject $false
Write-Host "Source-clean negative and positive fixture tests PASS." -ForegroundColor Green
