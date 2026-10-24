#Requires -Version 5.1
<#
.SYNOPSIS
Validate the immutable v1.6.41 operator-scoped release decision.
.DESCRIPTION
Does not grant R10 hardware qualification, complete SOE continuity or
permission to promote different engine pins.
#>
function Test-ExactOperatorReleaseAcceptance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Lock,
        [Parameter(Mandatory = $true)][string]$SourceRoot
    )
    $recordPath = Join-Path $SourceRoot 'evidence/v1.6.41-operator-release-acceptance.json'
    $versionPath = Join-Path $SourceRoot 'VERSION'
    if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        return $false
    }
    try {
        $proof = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
        $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
        return (
            $version -ceq '1.6.41' -and
            $proof.status -ceq 'operator-approved-limited-public-release' -and
            $proof.releaseTag -ceq 'v1.6.41' -and
            [bool]$proof.decision.releaseApproved -and
            $proof.installedBuild.appVersion -ceq $version -and
            $proof.installedBuild.appAssemblyRevision -ceq '44eacf200ab20c0453e62a88ad71f472821ec5a2' -and
            $proof.installedBuild.engineCommit -ceq '036e2c00d83a0815b6160147b14ea05975057965' -and
            $proof.installedBuild.engineCommit -ceq [string]$Lock.commit -and
            [int]$Lock.sourcePullRequest -eq 169 -and
            [bool]$proof.scope.legacyRcbDialogRetained -and
            [bool]$proof.scope.eventPropertiesIncluded -and
            [bool]$proof.scope.iedBrowserExcluded -and
            [bool]$proof.scope.newRcbExporterExcluded -and
            [bool]$proof.scope.staticDatasetReportOnly -and
            [bool]$proof.scope.cyclicMmsProcessPollingForbidden -and
            [bool]$proof.observations.continuity.bufferOverflowObserved -and
            [bool]$proof.observations.continuity.sequenceDiscontinuitiesObserved -and
            -not [bool]$proof.qualification.hardwarePhysicallyAttested -and
            -not [bool]$proof.qualification.completeSoeContinuityVerified -and
            [bool]$proof.qualification.scopedOperatorAcceptance
        )
    }
    catch {
        return $false
    }
}
