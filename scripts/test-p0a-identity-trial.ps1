# Exact CODE-only P0-A identity trial; never grants physical or mainline release authority.
function Test-P0AIdentityTrial {
    param([Parameter(Mandatory = $true)]$Lock)

    $trial = $Lock.p0AIdentityTrial
    $prior = $Lock.priorTrialPin
    if ($null -eq $trial -or $null -eq $prior) { return $false }

    # Recheck the original typed-report trial instead of replacing its guards.
    . (Join-Path $PSScriptRoot 'test-m217k-typed-status-trial.ps1')
    $historical = $Lock | Select-Object *
    $historical.commit = $prior.commit
    $historical.sourcePullRequest = $prior.sourcePullRequest
    $historical.purpose = $prior.purpose
    if (-not (Test-M217KTypedStatusTrial -Lock $historical)) { return $false }

    return (
        [string]$prior.commit -ceq '51c2eefd61d6bcbe1116fda6b781b74bb9cf06da' -and
        [int]$prior.sourcePullRequest -eq 173 -and
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.purpose -match 'P0-A identity authority trial' -and
        [string]$Lock.commit -ceq 'ccf31f60095ea65f9095514f0fa0102060962049' -and
        [int]$Lock.sourcePullRequest -eq 177 -and
        [string]$trial.status -ceq 'engine-ci-passed-consumer-ci-pending-not-physical' -and
        [string]$trial.baseEngineCommit -ceq $prior.commit -and
        [string]$trial.exactCommit -ceq $Lock.commit -and
        [int]$trial.enginePullRequest -eq 177 -and
        [int]$trial.consumerPullRequest -eq 623 -and
        [long]$trial.engineCiRun -eq 39228439974 -and
        [string]$trial.identityMode -ceq 'verified-scl-or-provisional-mms' -and
        [bool]$trial.strictSourceSha256Required -and
        [bool]$trial.uniqueAccessPointRequired -and
        [bool]$trial.exactEndpointRequired -and
        [bool]$trial.exactMmsDomainSetRequired -and
        [bool]$trial.caseSensitiveDomainPreservation -and
        [bool]$trial.heuristicConfidenceNeverHigh -and
        [bool]$trial.controlAndReportingWritePathsUntouched -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden
    )
}
