# P0-A identity is a code-only descendant of the immutable M2.17-K engine.
# This verifier deliberately never changes historical physical acceptance.
function Test-P0AIdentityTrial {
    param([Parameter(Mandatory = $true)]$Lock)

    $trial = $Lock.p0AIdentityTrial
    if ($null -eq $trial) { return $false }

    . (Join-Path $PSScriptRoot 'test-m217k-typed-status-trial.ps1')
    $parent = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $parent.commit = '51c2eefd61d6bcbe1116fda6b781b74bb9cf06da'
    $parent.sourcePullRequest = 173
    $parent.purpose = 'M2.17-K isolated C264 acceptance trial'
    if (-not (Test-M217KTypedStatusTrial -Lock $parent)) { return $false }

    return (
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.commit -ceq 'ccf31f60095ea65f9095514f0fa0102060962049' -and
        [int]$Lock.sourcePullRequest -eq 177 -and
        [string]$trial.status -ceq 'code-verified-engine-consumer-trial-not-physical' -and
        [int]$trial.enginePullRequest -eq 177 -and
        [int]$trial.consumerPullRequest -eq 625 -and
        [string]$trial.baseEngineCommit -ceq '51c2eefd61d6bcbe1116fda6b781b74bb9cf06da' -and
        [string]$trial.exactCommit -ceq $Lock.commit -and
        [long]$trial.engineCiRun -eq 39228439974 -and
        [bool]$trial.fullDomainSetRequired -and
        [bool]$trial.sclSha256Required -and
        [bool]$trial.endpointAndAccessPointRequired -and
        [bool]$trial.provisionalHeuristicOnly -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden
    )
}
