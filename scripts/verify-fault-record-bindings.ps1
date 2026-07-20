# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $root "FaultRecordWindow.xaml"
$xaml = Get-Content -LiteralPath $xamlPath -Raw

$requiredOneTimeBindings = @("DeviceName", "EndpointText")
foreach ($property in $requiredOneTimeBindings) {
    $pattern = "\{Binding\s+$([regex]::Escape($property))\s*,[^}]*Mode=OneTime[^}]*\}"
    if ($xaml -notmatch $pattern) {
        throw "FaultRecordWindow immutable binding '$property' must explicitly use Mode=OneTime."
    }
}

$requiredOneWayBindings = @(
    "IsNotBusy",
    "SelectionSummary",
    "Records",
    "RecordName",
    "ModifiedText",
    "SizeText",
    "FilesText",
    "Completeness",
    "Status",
    "StatusText",
    "ProgressValue",
    "IsIndeterminate",
    "IsBusy",
    "CanDownload",
    "CanSelectForDownload"
)
foreach ($property in $requiredOneWayBindings) {
    $pattern = "\{Binding\s+$([regex]::Escape($property))\s*,[^}]*Mode=OneWay[^}]*\}"
    if ($xaml -notmatch $pattern) {
        throw "FaultRecordWindow display binding '$property' must explicitly use Mode=OneWay."
    }
}

# RemoteDirectory is intentionally not exposed by the compact fast workflow. The
# window always starts discovery at the relay file-store root and keeps manual
# refresh in the header.
$requiredTwoWayBindings = @("DestinationDirectory", "IsSelected")
foreach ($property in $requiredTwoWayBindings) {
    $pattern = "\{Binding\s+$([regex]::Escape($property))\s*,[^}]*Mode=TwoWay[^}]*\}"
    if ($xaml -notmatch $pattern) {
        throw "FaultRecordWindow editable binding '$property' must explicitly use Mode=TwoWay."
    }
}

if ($xaml -match 'Text="Remote directory"' -or $xaml -match 'Header="Remote directory"') {
    throw "FaultRecordWindow compact workflow must not expose the remote-directory input or grid column."
}

Write-Host "Fault record binding modes and compact workflow are explicit and valid." -ForegroundColor Green
