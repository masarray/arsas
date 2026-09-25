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
Write-Host "Source-clean negative and positive fixture tests PASS." -ForegroundColor Green
