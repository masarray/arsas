param(
    [Parameter(Mandatory = $true)]
    [string]$PcapPath,

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

function Resolve-CapturePath([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw "$Label is not a file: $Path"
    }
    return $resolved.Path
}

function Get-TsharkFieldSet {
    param([string]$Executable)

    $lines = & $Executable -G fields 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "TShark field discovery failed with exit code $LASTEXITCODE. Output: $($lines -join [Environment]::NewLine)"
    }

    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $lines) {
        $parts = [string]$line -split "`t"
        if ($parts.Length -ge 3 -and $parts[0] -eq "F" -and -not [string]::IsNullOrWhiteSpace($parts[2])) {
            [void]$set.Add($parts[2])
        }
    }
    return $set
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
    $service = Get-ServiceName $Row
    $parts = [ordered]@{
        service = $service
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

function Decode-MmsRows {
    param(
        [string]$Capture,
        [string]$Executable,
        [System.Collections.Generic.HashSet[string]]$AvailableFields
    )

    $candidateFields = @(
        "frame.number",
        "frame.time_epoch",
        "ip.src",
        "ip.dst",
        "ipv6.src",
        "ipv6.dst",
        "tcp.stream",
        "mms.invokeID",
        "mms.confirmed_requestPDU",
        "mms.confirmed_responsePDU",
        "mms.confirmed_errorPDU",
        "mms.confirmedServiceRequest",
        "mms.getNameList_element",
        "mms.getVariableAccessAttributes_element",
        "mms.getNamedVariableListAttributes_element",
        "mms.read_element",
        "mms.identify_element",
        "mms.write_element",
        "mms.objectClass",
        "mms.objectScope",
        "mms.domainId",
        "mms.itemId",
        "mms.objectName_domain_specific_itemId",
        "mms.domainSpecific",
        "mms.vmd_specific",
        "mms.variableListName",
        "mms.continueAfter",
        "mms.getNameList-Request_continueAfter",
        "mms.nameToStartAfter",
        "mms.negociatedMaxServOutstandingCalling",
        "mms.negociatedMaxServOutstandingCalled"
    )

    $fields = @($candidateFields | Where-Object { $AvailableFields.Contains($_) })
    foreach ($required in @("frame.number", "frame.time_epoch", "tcp.stream", "mms.invokeID", "mms.confirmed_requestPDU", "mms.confirmed_responsePDU", "mms.confirmed_errorPDU")) {
        if ($fields -notcontains $required) {
            throw "Installed TShark does not expose required field '$required'."
        }
    }
    if (($fields -notcontains "ip.src") -and ($fields -notcontains "ipv6.src")) {
        throw "Installed TShark exposes neither IPv4 nor IPv6 source fields."
    }

    $args = @(
        "-r", $Capture,
        "-Y", "mms",
        "-T", "fields",
        "-E", "header=y",
        "-E", "quote=d",
        "-E", "occurrence=a",
        "-E", "aggregator=,"
    )
    foreach ($field in $fields) {
        $args += @("-e", $field)
    }

    $csvLines = & $Executable @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "TShark failed to decode '$Capture' with exit code $LASTEXITCODE. Output: $($csvLines -join [Environment]::NewLine)"
    }
    if (-not $csvLines -or $csvLines.Count -lt 2) {
        throw "No MMS rows were decoded from '$Capture'. Capture must include the full MMS association and discovery interval."
    }

    return @($csvLines | ConvertFrom-Csv -Delimiter "`t")
}

function Analyze-Capture {
    param(
        [string]$Capture,
        [string]$Executable,
        [System.Collections.Generic.HashSet[string]]$AvailableFields,
        [string]$RequestedClientIp,
        [string]$RequestedServerIp
    )

    $rows = Decode-MmsRows -Capture $Capture -Executable $Executable -AvailableFields $AvailableFields
    $requestRowsAll = @($rows | Where-Object { Test-Present $_ "mms.confirmed_requestPDU" })
    if ($requestRowsAll.Count -eq 0) {
        throw "No MMS confirmed-request PDU was found in '$Capture'."
    }

    $client = $RequestedClientIp
    $server = $RequestedServerIp
    if ([string]::IsNullOrWhiteSpace($client)) { $client = Get-EndpointSource $requestRowsAll[0] }
    if ([string]::IsNullOrWhiteSpace($server)) { $server = Get-EndpointDestination $requestRowsAll[0] }
    if ([string]::IsNullOrWhiteSpace($client) -or [string]::IsNullOrWhiteSpace($server)) {
        throw "Could not infer client/server IP endpoints from the first confirmed MMS request. Supply -ClientIp and -ServerIp explicitly."
    }

    $directionRows = @($rows | Where-Object {
        $src = Get-EndpointSource $_
        $dst = Get-EndpointDestination $_
        (($src -eq $client -and $dst -eq $server) -or ($src -eq $server -and $dst -eq $client))
    })

    $requests = @($directionRows | Where-Object {
        (Get-EndpointSource $_) -eq $client -and
        (Get-EndpointDestination $_) -eq $server -and
        (Test-Present $_ "mms.confirmed_requestPDU")
    })
    $responses = @($directionRows | Where-Object {
        (Get-EndpointSource $_) -eq $server -and
        (Get-EndpointDestination $_) -eq $client -and
        ((Test-Present $_ "mms.confirmed_responsePDU") -or (Test-Present $_ "mms.confirmed_errorPDU"))
    })

    $requestRecords = foreach ($row in $requests) {
        [pscustomobject]@{
            Frame = [int](Get-RowValue $row "frame.number")
            Time = [double](Get-RowValue $row "frame.time_epoch")
            TcpStream = Get-RowValue $row "tcp.stream"
            InvokeId = Get-RowValue $row "mms.invokeID"
            Service = Get-ServiceName $row
            Fingerprint = Get-RequestFingerprint $row
        }
    }

    $duplicateGroups = @($requestRecords |
        Group-Object Fingerprint |
        Where-Object Count -gt 1 |
        Sort-Object Count -Descending, Name)
    $duplicateRequests = [int](($duplicateGroups | ForEach-Object { $_.Count - 1 } | Measure-Object -Sum).Sum)

    $duplicateDetails = @($duplicateGroups | ForEach-Object {
        $records = @($_.Group | Sort-Object Frame)
        [pscustomobject]@{
            Service = $records[0].Service
            DuplicateAttempts = $_.Count - 1
            Frames = @($records.Frame)
            Fingerprint = $_.Name
        }
    })

    $serviceCounts = [ordered]@{}
    foreach ($group in ($requestRecords | Group-Object Service | Sort-Object Name)) {
        $serviceCounts[$group.Name] = $group.Count
    }

    $events = @()
    foreach ($row in $requests) {
        $events += [pscustomobject]@{
            Frame = [int](Get-RowValue $row "frame.number")
            Kind = "request"
            InvokeId = Get-RowValue $row "mms.invokeID"
        }
    }
    foreach ($row in $responses) {
        $events += [pscustomobject]@{
            Frame = [int](Get-RowValue $row "frame.number")
            Kind = "response"
            InvokeId = Get-RowValue $row "mms.invokeID"
        }
    }

    $outstanding = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $peakOutstanding = 0
    $invokeReuseWhileOutstanding = 0
    $orphanResponses = 0
    foreach ($event in ($events | Sort-Object Frame)) {
        if ([string]::IsNullOrWhiteSpace($event.InvokeId)) { continue }
        if ($event.Kind -eq "request") {
            if (-not $outstanding.Add($event.InvokeId)) {
                $invokeReuseWhileOutstanding++
            }
            $peakOutstanding = [Math]::Max($peakOutstanding, $outstanding.Count)
        } else {
            if (-not $outstanding.Remove($event.InvokeId)) {
                $orphanResponses++
            }
        }
    }

    $negotiatedCandidates = @($directionRows | ForEach-Object {
        Get-RowValue $_ "mms.negociatedMaxServOutstandingCalling"
    } | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
    $negotiatedCalling = if ($negotiatedCandidates.Count -gt 0) { $negotiatedCandidates[0] } else { $null }

    $requestStreams = @($requestRecords.TcpStream | Where-Object { $_ } | Sort-Object -Unique)
    $getNameListDuplicates = @($duplicateDetails | Where-Object Service -eq "GetNameList")
    $gvaDuplicates = @($duplicateDetails | Where-Object Service -eq "GetVariableAccessAttributes")

    return [pscustomobject]@{
        Capture = $Capture
        ClientIp = $client
        ServerIp = $server
        RequestTcpStreams = $requestStreams
        ConfirmedRequests = $requestRecords.Count
        ConfirmedResponsesOrErrors = $responses.Count
        ServiceCounts = [pscustomobject]$serviceCounts
        DuplicateSemanticRequests = $duplicateRequests
        DuplicateGetNameListRequests = [int](($getNameListDuplicates | ForEach-Object DuplicateAttempts | Measure-Object -Sum).Sum)
        DuplicateGvaRequests = [int](($gvaDuplicates | ForEach-Object DuplicateAttempts | Measure-Object -Sum).Sum)
        DuplicateDetails = $duplicateDetails
        SecondGetNameListSweepDetected = $getNameListDuplicates.Count -gt 0
        PeakOutstandingRequests = $peakOutstanding
        NegotiatedMaxOutstandingCalling = $negotiatedCalling
        InvokeIdReuseWhileOutstanding = $invokeReuseWhileOutstanding
        OrphanResponses = $orphanResponses
        UnansweredRequestsAtCaptureEnd = $outstanding.Count
    }
}

$pcap = Resolve-CapturePath $PcapPath "P0-5d capture"
$reference = Resolve-CapturePath $ReferencePcapPath "Reference capture"

try {
    $tsharkCommand = Get-Command $TsharkPath -ErrorAction Stop
} catch {
    throw "TShark was not found. Install Wireshark/TShark or supply -TsharkPath. $($_.Exception.Message)"
}

$fieldSet = Get-TsharkFieldSet -Executable $tsharkCommand.Source
$actual = Analyze-Capture -Capture $pcap -Executable $tsharkCommand.Source -AvailableFields $fieldSet -RequestedClientIp $ClientIp -RequestedServerIp $ServerIp
$referenceAnalysis = $null
if ($reference) {
    $referenceAnalysis = Analyze-Capture -Capture $reference -Executable $tsharkCommand.Source -AvailableFields $fieldSet -RequestedClientIp "" -RequestedServerIp ""
}

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
if ($actual.ServiceCounts.PSObject.Properties["GetVariableAccessAttributes"]) {
    $gvaCount = [int]$actual.ServiceCounts.GetVariableAccessAttributes
}
if ($MaxGvaRequests -gt 0 -and $gvaCount -gt $MaxGvaRequests) {
    $failures.Add("GVA request budget exceeded: $gvaCount > $MaxGvaRequests.")
}
if ($RequireNoMoreRequestsThanReference -and $referenceAnalysis -and $actual.ConfirmedRequests -gt $referenceAnalysis.ConfirmedRequests) {
    $failures.Add("ARSAS confirmed-request count $($actual.ConfirmedRequests) exceeds reference count $($referenceAnalysis.ConfirmedRequests).")
}

$comparison = $null
if ($referenceAnalysis) {
    $comparison = [pscustomobject]@{
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
}

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
    $base = [IO.Path]::GetFileNameWithoutExtension($pcap)
    $OutputJson = Join-Path ([IO.Path]::GetDirectoryName($pcap)) "P0-5D-$base-proof.json"
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputJson -Encoding utf8

Write-Host "P0-5d physical capture proof: $($result.Verdict)"
Write-Host "  association: $($actual.ClientIp) -> $($actual.ServerIp); TCP stream(s): $($actual.RequestTcpStreams -join ', ')"
Write-Host "  confirmed requests: $($actual.ConfirmedRequests); peak outstanding: $($actual.PeakOutstandingRequests); negotiated calling: $($actual.NegotiatedMaxOutstandingCalling)"
Write-Host "  duplicates: semantic=$($actual.DuplicateSemanticRequests), GetNameList=$($actual.DuplicateGetNameListRequests), GVA=$($actual.DuplicateGvaRequests)"
Write-Host "  service budget: $($actual.ServiceCounts | ConvertTo-Json -Compress)"
if ($referenceAnalysis) {
    Write-Host "  reference requests: $($referenceAnalysis.ConfirmedRequests); delta=$($comparison.ConfirmedRequestDelta); ratio=$($comparison.ConfirmedRequestRatio)"
}
Write-Host "  proof JSON: $OutputJson"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    if (-not $NoFailExit) { exit 1 }
}
