# Exact code-only successor of validated P0-C controls: discovery speed trial.
# The historical association/single-flight/SBO safety proof is recursively
# checked, never overwritten by the later RCB read-only scheduling patch.
function Test-P0SiebcuWindowRcbTrial {
    param([Parameter(Mandatory = $true)]$Lock)

    $trial = $Lock.p0SiebcuDiscoveryWindowTrial
    if ($null -eq $trial -or
        [string]$Lock.commit -cne 'd095b3ac9bdf6687f7ab9407f7e28eca9c168c94' -or
        [int]$Lock.sourcePullRequest -ne 186 -or
        [string]$trial.exactCommit -cne [string]$Lock.commit -or
        [string]$trial.baseEngineCommit -cne '9c7cb40364023a7b217e5ec31ab3105ba90142eb' -or
        [string]$trial.status -cne 'engine-ci-success-consumer-ci-pending-not-physical' -or
        [int]$trial.enginePullRequest -ne 186 -or
        [int]$trial.consumerPullRequest -ne 663 -or
        [long]$trial.engineCiRun -ne 39555379120 -or
        -not [bool]$trial.negotiatedWindowRequired -or
        -not [bool]$trial.readOnlyRcbEnrichment -or
        -not [bool]$trial.perRcbSequentialFallbackRequired -or
        -not [bool]$trial.noNewMmsRequestScope -or
        -not [bool]$trial.noControlOrRcbWrites -or
        -not [bool]$trial.noDynamicDatasetMutation -or
        -not [bool]$trial.noAdditionalAssociation -or
        -not [bool]$trial.mainlinePromotionForbidden -or
        -not [bool]$trial.physicalQualificationRequired) {
        return $false
    }

    $original = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $original.commit = $trial.baseEngineCommit
    $original.sourcePullRequest = 184
    return (Test-P0CTypedReadTrial -Lock $original)
}
