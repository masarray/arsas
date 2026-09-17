param(
    [string]$PcapPath,
    [string]$DecodedRowsPath,
    [string]$ReferencePcapPath,
    [string]$TsharkPath = "tshark",
    [string]$ClientIp,
    [string]$ServerIp,
    [int]$MaxConfirmedRequests = 0,
    [int]$MaxGvaRequests = 0,
    [switch]$RequireNoMoreRequestsThanReference,
    [string]$OutputJson,
    [switch]$NoFailExit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-OptionalFile([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw "$Label is not a file: $Path"
    }
    return $resolved.Path
}

function Normalize-Cell([object]$Value) {
    if ($null -eq $Value) { return "" }
    return ([string]$Value).Trim()
}

function Get-RowValue($Row, [string]$Name) {
    if ($null -eq $Row) { return "" }
    $property = $Row.PSObject.Properties[$Name]
    if ($null -eq $property) { return "" }
    return Normalize-Cell $property.Value
}

function Get-EndpointSource($Row) {
    $ipv4 = Get-RowValue $Row "ip.src"
    if ($ipv4) { return $ipv4 }
    return Get-RowValue $Row "ipv6.src"
}

function Get-EndpointDestination($Row) {
    $ipv4 = Get-RowValue $Row "ip.dst"
    if ($ipv4) { return $ipv4 }
    return Get-RowValue $Row "ipv6.dst"
}

function Test-Present($Row, [string]$Field) {
    return -not [string]::IsNullOrWhiteSpace((Get-RowValue $Row $Field))
}

function Get-ServiceName($Row) {
    if (Test-Present $Row "mms.getNameList_element") { return "GetNameList" }
    if (Test-Present $Row "mms.getVariableAccessAttributes_element") { return "GetVariableAccessAttributes" }
    if (Test-Present $Row "mms.getNamedVariableListAttributes_element") { return "GetNamedVariableListAttributes" }
    if (Test-Present $Row "mms.read_element") { return "Read" }
    if (Test-Present $Row "mms.identify_element") { return "Identify" }
    if (Test-Present $Row "mms.write_element") { return "Write" }

    $service = Get-RowValue $Row "mms.confirmedServiceRequest"
    if ($service) { return "ConfirmedService:$service" }
    return "UnknownConfirmedService"
}

function Get-RequestFingerprint($Row) {
    # Invoke-ID is deliberately excluded. Reissuing the same logical request with a
    # different invoke-ID must still be detected as duplicate wire work.
    $parts = [ordered]@{
        service = Get-ServiceName $Row
        objectClass = Get-RowValue $Row "mms.objectClass"
        objectScope = Get-RowValue $Row "mms.objectScope"
        domainId = Get-RowValue $Row "mms.domainId"
        itemId = Get-RowValue $Row "mms.itemId"
        objectItemId = Get-RowValue $Row "mms.objectName_domain_specific_itemId"
        domainSpecific = Get-RowValue $Row "mms.domainSpecific"
        vmdSpecific = Get-RowValue $Row "mms.vmd_specific"
        variableListName = Get-RowValue $Row "mms.variableListName"
        continueAfter = Get-RowValue $Row "mms.continueAfter"
        getNameListContinueAfter = Get-RowValue $Row "mms.getNameList-Request_continueAfter"
        nameToStartAfter = Get-RowValue $Row "mms.nameToStartAfter"
    }
    return (($parts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "|")
}

function Get-TsharkFieldSet([string]$Executable) {
    $lines = & $Executable -G fields 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "TShark field discovery failed with exit code $LASTEXITCODE."
    }

    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $lines) {
        $parts = [string]$line -split "`t"
        if ($parts.Length -ge 3 -and $parts[0] -eq "F" -and $parts[2]) {
            [void]$set.Add($parts[2])
        }
    }
    return $set
}

function Decode-MmsRows([string]$Capture, [string]$Executable, [System.Collections.Generic.HashSet[string]]$AvailableFields) {
    $candidateFields = @(
        "frame.number", "frame.time_epoch", "ip.src", "ip.dst", "ipv6.src", "ipv6.dst", "tcp.stream",
        "mms.invokeID", "mms.confirmed_requestPDU", "mms.confirmed_responsePDU", "mms.confirmed_errorPDU",
        "mms.confirmedServiceRequest", "mms.getNameList_element", "mms.getVariableAccessAttributes_element",
        "mms.getNamedVariableListAttributes_element", "mms.read_element", "mms.identify_element", "mms.write_element",
        "mms.objectClass", "mms.objectScope", "mms.domainId", "mms.itemId", "mms.objectName_domain_specific_itemId",
        "mms.domainSpecific", "mms.vmd_specific", "mms.variableListName", "mms.continueAfter",
        "mms.getNameList-Request_continueAfter", "mms.nameToStartAfter",
        "mms.negociatedMaxServOutstandingCalling", "mms.negociatedMaxServOutstandingCalled"
    )

    $fields = @($candidateFields | Where-Object { $AvailableFields.Contains($_) })
    foreach ($required in @("frame.number", "frame.time_epoch", "tcp.stream", "mms.invokeID", "mms.confirmed_requestPDU", "mms.confirmed_responsePDU", "mms.confirmed_errorPDU")) {
        if ($fields -notcontains $required) { throw "Installed TShark does not expose required field '$required'." }
    }

    $args = @("-r", $Capture, "-Y", "mms", "-T", "fields", "-E", "header=y", "-E", "quote=d", "-E", "occurrence=a", "-E", "aggregator=,")
    foreach ($field in $fields) { $args += @("-e", $field) }

    $lines = & $Executable @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "TShark failed to decode '$Capture' with exit code $LASTEXITCODE. Output: $($lines -join [Environment]::NewLine)"
    }
    if (-not $lines -or $lines.Count -lt 2) {
        throw "No MMS rows were decoded from '$Capture'."
    }
    return @($lines | ConvertFrom-Csv -Delimiter "`t")
}

function Import-DecodedRows([string]$Path) {
    $rows = @(Import-Csv -LiteralPath $Path -Delimiter "`t")
    if ($rows.Count -eq 0) { throw "Decoded-row fixture '$Path' is empty." }
    return $rows
}

function Analyze-Rows($Rows, [string]$Label, [string]$RequestedClientIp, [string]$RequestedServerIp) {
    $requestRowsAll = @($Rows | Where-Object { Test-Present $_ "mms.confirmed_requestPDU" })
    if ($requestRowsAll.Count -eq 0) { throw "No MMS confirmed-request PDU was found in '$Label'." }

    $client = $RequestedClientIp
    $server = $RequestedServerIp
    if ([string]::IsNullOrWhiteSpace($client)) { $client = Get-EndpointSource $requestRowsAll[0] }
    if ([string]::IsNullOrWhiteSpace($server)) { $server = Get-EndpointDestination $requestRowsAll[0] }
    if (-not $client -or -not $server) { throw "Could not infer client/server endpoints for '$Label'." }

    $directionRows = @($Rows | Where-Object {
        $src = Get-EndpointSource $_
        $dst = Get-EndpointDestination $_
        (($src -eq $client -and $dst -eq $server) -or ($src -eq $server -and $dst -eq $client))
    })
    $requests = @($directionRows | Where-Object {
        (Get-EndpointSource $_) -eq $client -and (Get-EndpointDestination $_) -eq $server -and (Test-Present $_ "mms.confirmed_requestPDU")
    })
    $responses = @($directionRows | Where-Object {
        (Get-EndpointSource $_) -eq $server -and (Get-EndpointDestination $_) -eq $client -and
        ((Test-Present $_ "mms.confirmed_responsePDU") -or (Test-Present $_ "mms.confirmed_errorPDU"))
    })

    $requestRecords = @($requests | ForEach-Object {
        [pscustomobject]@{
            Frame = [int](Get-RowValue $_ "frame.number")
            TcpStream = Get-RowValue $_ "tcp.stream"
            InvokeId = Get-RowValue $_ "mms.invokeID"
            Service = Get-ServiceName $_
            Fingerprint = Get-RequestFingerprint $_
        }
    })

    $duplicateGroups = @($requestRecords | Group-Object Fingerprint | Where-Object Count -gt 1 |
        Sort-Object -Property @{ Expression = "Count"; Descending = $true }, @{ Expression = "Name"; Descending = $false })
    $duplicateDetails = @($duplicateGroups | ForEach-Object {
        $records = @($_.Group | Sort-Object Frame)
        [pscustomobject]@{
            Service = $records[0].Service
            DuplicateAttempts = $_.Count - 1
            Frames = @($records.Frame)
            Fingerprint = $_.Name
        }
    })
    $duplicateRequests = [int](($duplicateDetails | Measure-Object DuplicateAttempts -Sum).Sum)

    $serviceCounts = [ordered]@{}
    foreach ($group in ($requestRecords | Group-Object Service | Sort-Object Name)) { $serviceCounts[$group.Name] = $group.Count }

    $events = @()
    foreach ($row in $requests) {
        $events += [pscustomobject]@{ Frame = [int](Get-RowValue $row "frame.number"); Kind = "request"; InvokeId = Get-RowValue $row "mms.invokeID" }
    }
    foreach ($row in $responses) {
        $events += [pscustomobject]@{ Frame = [int](Get-RowValue $row "frame.number"); Kind = "response"; InvokeId = Get-RowValue $row "mms.invokeID" }
    }

    $outstanding = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $peakOutstanding = 0
    $invokeReuseWhileOutstanding = 0
    $orphanResponses = 0
    foreach ($event in ($events | Sort-Object Frame)) {
        if (-not $event.InvokeId) { continue }
        if ($event.Kind -eq "request") {
            if (-not $outstanding.Add($event.InvokeId)) { $invokeReuseWhileOutstanding++ }
            $peakOutstanding = [Math]::Max($peakOutstanding, $outstanding.Count)
        } elseif (-not $outstanding.Remove($event.InvokeId)) {
            $orphanResponses++
        }
    }

    $negotiated = @($directionRows | ForEach-Object { Get-RowValue $_ "mms.negociatedMaxServOutstandingCalling" } |
        Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
    $negotiatedCalling = if ($negotiated.Count -gt 0) { $negotiated[0] } else { $null }
    $requestStreams = @($requestRecords.TcpStream | Where-Object { $_ } | Sort-Object -Unique)
    $gnlDuplicates = @($duplicateDetails | Where-Object Service -eq "GetNameList")
    $gvaDuplicates = @($duplicateDetails | Where-Object Service -eq "GetVariableAccessAttributes")

    return [pscustomobject]@{
        Capture = $Label
        ClientIp = $client
        ServerIp = $server
        RequestTcpStreams = $requestStreams
        ConfirmedRequests = $requestRecords.Count
        ConfirmedResponsesOrErrors = $responses.Count
        ServiceCounts = [pscustomobject]$serviceCounts
        DuplicateSemanticRequests = $duplicateRequests
        DuplicateGetNameListRequests = [int](($gnlDuplicates | Measure-Object DuplicateAttempts -Sum).Sum)
        DuplicateGvaRequests = [int](($gvaDuplicates | Measure-Object DuplicateAttempts -Sum).Sum)
        DuplicateDetails = $duplicateDetails
        SecondGetNameListSweepDetected = $gnlDuplicates.Count -gt 0
        PeakOutstandingRequests = $peakOutstanding
        NegotiatedMaxOutstandingCalling = $negotiatedCalling
        InvokeIdReuseWhileOutstanding = $invokeReuseWhileOutstanding
        OrphanResponses = $orphanResponses
        UnansweredRequestsAtCaptureEnd = $outstanding.Count
    }
}

function Analyze-Pcap([string]$Capture, [string]$RequestedClientIp, [string]$RequestedServerIp) {
    $command = Get-Command $TsharkPath -ErrorAction Stop
    $fields = Get-TsharkFieldSet $command.Source
    $rows = Decode-MmsRows $Capture $command.Source $fields
    return Analyze-Rows $rows $Capture $RequestedClientIp $RequestedServerIp
}

$pcap = Resolve-OptionalFile $PcapPath "P0-5d capture"
$decoded = Resolve-OptionalFile $DecodedRowsPath "P0-5d decoded rows"
$reference = Resolve-OptionalFile $ReferencePcapPath "Reference capture"
if (($null -eq $pcap) -eq ($null -eq $decoded)) {
    throw "Supply exactly one of -PcapPath or -DecodedRowsPath."
}

$actual = if ($decoded) {
    Analyze-Rows (Import-DecodedRows $decoded) $decoded $ClientIp $ServerIp
} else {
    Analyze-Pcap $pcap $ClientIp $ServerIp
}
$referenceAnalysis = if ($reference) { Analyze-Pcap $reference "" "" } else { $null }

$failures = [System.Collections.Generic.List[string]]::new()
if ($actual.RequestTcpStreams.Count -ne 1) { $failures.Add("Expected exactly one MMS request TCP stream; observed $($actual.RequestTcpStreams.Count).") }
if ($actual.DuplicateSemanticRequests -ne 0) { $failures.Add("Duplicate semantic confirmed requests detected: $($actual.DuplicateSemanticRequests).") }
if ($actual.SecondGetNameListSweepDetected) { $failures.Add("A duplicate GetNameList semantic request was observed; this is evidence of a second/repeated naming sweep.") }
if ($actual.DuplicateGvaRequests -ne 0) { $failures.Add("Duplicate GetVariableAccessAttributes semantic requests detected: $($actual.DuplicateGvaRequests).") }
if ($actual.InvokeIdReuseWhileOutstanding -ne 0) { $failures.Add("Invoke-ID reuse while the previous request was still outstanding: $($actual.InvokeIdReuseWhileOutstanding).") }
if ($actual.OrphanResponses -ne 0) { $failures.Add("Responses/errors without an observed matching request: $($actual.OrphanResponses). Capture may be incomplete.") }
if ($actual.UnansweredRequestsAtCaptureEnd -ne 0) { $failures.Add("Confirmed requests still outstanding at capture end: $($actual.UnansweredRequestsAtCaptureEnd). Capture may have ended too early.") }
if ($null -ne $actual.NegotiatedMaxOutstandingCalling -and $actual.PeakOutstandingRequests -gt $actual.NegotiatedMaxOutstandingCalling) {
    $failures.Add("Peak outstanding $($actual.PeakOutstandingRequests) exceeded negotiated maxOutstandingCalling $($actual.NegotiatedMaxOutstandingCalling).")
}
if ($MaxConfirmedRequests -gt 0 -and $actual.ConfirmedRequests -gt $MaxConfirmedRequests) {
    $failures.Add("Confirmed request budget exceeded: $($actual.ConfirmedRequests) > $MaxConfirmedRequests.")
}
$gvaCount = 0
if ($actual.ServiceCounts.PSObject.Properties["GetVariableAccessAttributes"]) { $gvaCount = [int]$actual.ServiceCounts.GetVariableAccessAttributes }
if ($MaxGvaRequests -gt 0 -and $gvaCount -gt $MaxGvaRequests) { $failures.Add("GVA request budget exceeded: $gvaCount > $MaxGvaRequests.") }
if ($RequireNoMoreRequestsThanReference -and $referenceAnalysis -and $actual.ConfirmedRequests -gt $referenceAnalysis.ConfirmedRequests) {
    $failures.Add("ARSAS confirmed-request count $($actual.ConfirmedRequests) exceeds reference count $($referenceAnalysis.ConfirmedRequests).")
}

$comparison = if ($referenceAnalysis) {
    [pscustomobject]@{
        ReferenceCapture = $referenceAnalysis.Capture
        ArsasConfirmedRequests = $actual.ConfirmedRequests
        ReferenceConfirmedRequests = $referenceAnalysis.ConfirmedRequests
        ConfirmedRequestDelta = $actual.ConfirmedRequests - $referenceAnalysis.ConfirmedRequests
        ConfirmedRequestRatio = if ($referenceAnalysis.ConfirmedRequests -gt 0) { [Math]::Round($actual.ConfirmedRequests / $referenceAnalysis.ConfirmedRequests, 4) } else { $null }
        ArsasPeakOutstanding = $actual.PeakOutstandingRequests
        ReferencePeakOutstanding = $referenceAnalysis.PeakOutstandingRequests
        ArsasDuplicateSemanticRequests = $actual.DuplicateSemanticRequests
        ReferenceDuplicateSemanticRequests = $referenceAnalysis.DuplicateSemanticRequests
        ArsasServiceCounts = $actual.ServiceCounts
        ReferenceServiceCounts = $referenceAnalysis.ServiceCounts
    }
} else { $null }

$result = [pscustomobject]@{
    SchemaVersion = 1
    Phase = "P0-5d"
    Verdict = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
    AcceptanceFailures = @($failures)
    ArsasCapture = $actual
    ReferenceComparison = $comparison
    ProofContract = [pscustomobject]@{
        ExactlyOneMmsRequestStream = $true
        DuplicateSemanticRequests = 0
        DuplicateGetNameListRequests = 0
        DuplicateGvaRequests = 0
        InvokeIdReuseWhileOutstanding = 0
        OrphanResponses = 0
        UnansweredRequestsAtCaptureEnd = 0
        PeakOutstandingMustNotExceedNegotiatedCallingLimit = $true
        MaxConfirmedRequests = if ($MaxConfirmedRequests -gt 0) { $MaxConfirmedRequests } else { $null }
        MaxGvaRequests = if ($MaxGvaRequests -gt 0) { $MaxGvaRequests } else { $null }
        RequireNoMoreRequestsThanReference = [bool]$RequireNoMoreRequestsThanReference
    }
}

if ([string]::IsNullOrWhiteSpace($OutputJson)) {
    $sourcePath = if ($pcap) { $pcap } else { $decoded }
    $base = [IO.Path]::GetFileNameWithoutExtension($sourcePath)
    $OutputJson = Join-Path ([IO.Path]::GetDirectoryName($sourcePath)) "P0-5D-$base-proof.json"
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputJson -Encoding utf8

Write-Host "P0-5d physical capture proof: $($result.Verdict)"
Write-Host "  association: $($actual.ClientIp) -> $($actual.ServerIp); TCP stream(s): $($actual.RequestTcpStreams -join ', ')"
Write-Host "  confirmed requests: $($actual.ConfirmedRequests); peak outstanding: $($actual.PeakOutstandingRequests); negotiated calling: $($actual.NegotiatedMaxOutstandingCalling)"
Write-Host "  duplicates: semantic=$($actual.DuplicateSemanticRequests), GetNameList=$($actual.DuplicateGetNameListRequests), GVA=$($actual.DuplicateGvaRequests)"
Write-Host "  service budget: $($actual.ServiceCounts | ConvertTo-Json -Compress)"
if ($comparison) { Write-Host "  reference requests: $($comparison.ReferenceConfirmedRequests); delta=$($comparison.ConfirmedRequestDelta); ratio=$($comparison.ConfirmedRequestRatio)" }
Write-Host "  proof JSON: $OutputJson"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    if (-not $NoFailExit) { exit 1 }
}
