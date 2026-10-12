#Requires -Version 7.0
# Pure synthetic resolver tests. Never contacts a network endpoint or alters repo locks.
$ErrorActionPreference = 'Stop'
$resolver = Join-Path $PSScriptRoot 'resolve-ariec-lock.ps1'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("arsas-ci-p1-resolver-" + [guid]::NewGuid().ToString('N'))
New-Item -Path $temp -ItemType Directory -Force | Out-Null
$checks = 0

function Assert-Resolves {
    param([hashtable]$Data)
    $script:checks++
    $path = Join-Path $temp ("valid-$script:checks.json")
    ConvertTo-Json -InputObject $Data -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    $result = & $resolver -LockPath $path
    if ([string]$result.commit -cne [string]$Data.commit) {
        throw "CI-P1 resolver valid fixture did not preserve integration SHA."
    }
}

function Assert-Rejects {
    param([hashtable]$Data, [string]$Expected)
    $script:checks++
    $path = Join-Path $temp ("invalid-$script:checks.json")
    ConvertTo-Json -InputObject $Data -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    $rejected = $false
    try {
        $null = & $resolver -LockPath $path
    }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") {
            throw "CI-P1 resolver returned an unexpected error: $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw "CI-P1 resolver accepted a forbidden fixture: $Expected"
    }
}

try {
    $stable = @{
        schemaVersion = 1
        repository = 'masarray/ARIEC61850'
        ref = 'main'
        commit = ('a' * 40)
        physicalTestedCommit = ('b' * 40)
        mergedMainCommit = ('a' * 40)
    }
    Assert-Resolves $stable

    $candidate = $stable.Clone()
    $candidate.commit = ('c' * 40)
    $candidate.previousStablePin = @{ commit = ('a' * 40) }
    $candidate.sclAssociationInteroperability = @{
        testedEngineCommit = ('c' * 40)
        mergedEngineCommit = ('d' * 40)
    }
    Assert-Resolves $candidate

    $wrongRepository = $stable.Clone()
    $wrongRepository.repository = 'untrusted/ARIEC61850'
    Assert-Rejects $wrongRepository 'repository'

    $wrongSha = $stable.Clone()
    $wrongSha.commit = 'main'
    Assert-Rejects $wrongSha 'immutable SHA'

    $wrongRef = $stable.Clone()
    $wrongRef.ref = 'feature'
    Assert-Rejects $wrongRef 'ref'

    $wrongHistorical = $candidate.Clone()
    $wrongHistorical.previousStablePin = @{ commit = ('f' * 40) }
    Assert-Rejects $wrongHistorical 'historical'

    $wrongAssociation = $candidate.Clone()
    $wrongAssociation.sclAssociationInteroperability = @{
        testedEngineCommit = ('f' * 40)
    }
    Assert-Rejects $wrongAssociation 'physical-tested'

    # Exact P10 lineage can be resolved as CODE TRIAL without converting
    # the older physically accepted association head into a false runtime pin.
    function New-TrackedP10Fixture {
        $tracked = Join-Path $PSScriptRoot '..\engines\ARIEC61850.lock.json'
        $source = Get-Content -LiteralPath $tracked -Raw -Encoding UTF8 |
            ConvertFrom-Json -AsHashtable -Depth 40
        $copy = @{}
        foreach ($key in $source.Keys) { $copy[$key] = $source[$key] }
        return $copy
    }

    Assert-Resolves (New-TrackedP10Fixture)

    $wrongCanonicalCi = New-TrackedP10Fixture
    $wrongCanonicalCi.canonicalReportIngressIntegrityTrial.engineCiRun = 0
    Assert-Rejects $wrongCanonicalCi 'physical-tested'

    $wrongCanonicalParent = New-TrackedP10Fixture
    $wrongCanonicalParent.canonicalReportIngressIntegrityTrial.baseEngineCommit = ('f' * 40)
    Assert-Rejects $wrongCanonicalParent 'physical-tested'

    $wrongCanonicalSafety = New-TrackedP10Fixture
    $wrongCanonicalSafety.canonicalReportIngressIntegrityTrial.mainlinePromotionForbidden = $false
    Assert-Rejects $wrongCanonicalSafety 'physical-tested'

    $wrongZivSha = New-TrackedP10Fixture
    $wrongZivSha.zivSclBscInteroperabilityTrial.exactCommit = ('f' * 40)
    Assert-Rejects $wrongZivSha 'physical-tested'

    $wrongZivCi = New-TrackedP10Fixture
    $wrongZivCi.zivSclBscInteroperabilityTrial.engineCiRun = 0
    Assert-Rejects $wrongZivCi 'physical-tested'

    $wrongZivBase = New-TrackedP10Fixture
    $wrongZivBase.zivSclBscInteroperabilityTrial.baseEngineCommit = ('f' * 40)
    Assert-Rejects $wrongZivBase 'physical-tested'

    $wrongZivScl = New-TrackedP10Fixture
    $wrongZivScl.zivSclBscInteroperabilityTrial.sclApTitlePackedPrefixOnly = $false
    Assert-Rejects $wrongZivScl 'physical-tested'

    $wrongP10Parent = New-TrackedP10Fixture
    $wrongP10Parent.p10IdentityTrial.baseEngineCommit = ('f' * 40)
    Assert-Rejects $wrongP10Parent 'physical-tested'

    $wrongP10Source = New-TrackedP10Fixture
    $wrongP10Source.sourcePullRequest = 160
    Assert-Rejects $wrongP10Source 'physical-tested'

    $wrongP10Polling = New-TrackedP10Fixture
    $wrongP10Polling.p10IdentityTrial.cyclicMmsProcessPollingForbidden = $false
    Assert-Rejects $wrongP10Polling 'physical-tested'

    $wrongP10History = New-TrackedP10Fixture
    $wrongP10History.sclAssociationInteroperability.testedEngineCommit = ('f' * 40)
    Assert-Rejects $wrongP10History 'physical-tested'

    $wrongP10Pin = New-TrackedP10Fixture
    $wrongP10Pin.commit = ('f' * 40)
    Assert-Rejects $wrongP10Pin 'physical-tested'

    Write-Host "CI-P1 immutable engine resolver: $checks synthetic cases PASS."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
