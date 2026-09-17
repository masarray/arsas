namespace ARSAS.Tests;

public sealed class SmartDiscoveryPhysicalCaptureProofRegressionTests
{
    [Fact]
    public void P05d_Verifier_FingerprintsSemanticRequestsAndRejectsDuplicateDiscoveryTraffic()
    {
        var verifier = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-pcap.ps1"));

        Assert.Contains("mms.confirmed_requestPDU", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.confirmed_responsePDU", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.confirmed_errorPDU", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.getNameList_element", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.getVariableAccessAttributes_element", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.getNamedVariableListAttributes_element", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.read_element", verifier, StringComparison.Ordinal);
        Assert.Contains("Get-RequestFingerprint", verifier, StringComparison.Ordinal);
        Assert.Contains("DuplicateSemanticRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("DuplicateGetNameListRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("DuplicateGvaRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("SecondGetNameListSweepDetected", verifier, StringComparison.Ordinal);

        var fingerprintStart = verifier.IndexOf("function Get-RequestFingerprint", StringComparison.Ordinal);
        var fingerprintEnd = verifier.IndexOf("function Decode-MmsRows", fingerprintStart, StringComparison.Ordinal);
        Assert.True(fingerprintStart >= 0 && fingerprintEnd > fingerprintStart);
        var fingerprint = verifier[fingerprintStart..fingerprintEnd];
        Assert.DoesNotContain("mms.invokeID", fingerprint, StringComparison.Ordinal);
        Assert.Contains("mms.domainId", fingerprint, StringComparison.Ordinal);
        Assert.Contains("mms.objectClass", fingerprint, StringComparison.Ordinal);
        Assert.Contains("mms.getNameList-Request_continueAfter", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void P05d_Verifier_ProvesOutstandingWindowAndCompleteAssociationCapture()
    {
        var verifier = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-pcap.ps1"));

        Assert.Contains("PeakOutstandingRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("mms.negociatedMaxServOutstandingCalling", verifier, StringComparison.Ordinal);
        Assert.Contains("InvokeIdReuseWhileOutstanding", verifier, StringComparison.Ordinal);
        Assert.Contains("OrphanResponses", verifier, StringComparison.Ordinal);
        Assert.Contains("UnansweredRequestsAtCaptureEnd", verifier, StringComparison.Ordinal);
        Assert.Contains("Expected exactly one MMS request TCP stream", verifier, StringComparison.Ordinal);
        Assert.Contains("Peak outstanding", verifier, StringComparison.Ordinal);
        Assert.Contains("exceeded negotiated maxOutstandingCalling", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void P05d_Verifier_SupportsSameIedReferenceAndExplicitBudgetGates()
    {
        var verifier = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-pcap.ps1"));
        var contract = File.ReadAllText(FindRepoFile("docs/P0-5D_PHYSICAL_CAPTURE_PROOF.md"));

        Assert.Contains("ReferencePcapPath", verifier, StringComparison.Ordinal);
        Assert.Contains("MaxConfirmedRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("MaxGvaRequests", verifier, StringComparison.Ordinal);
        Assert.Contains("RequireNoMoreRequestsThanReference", verifier, StringComparison.Ordinal);
        Assert.Contains("ConfirmedRequestDelta", verifier, StringComparison.Ordinal);
        Assert.Contains("ConfirmedRequestRatio", verifier, StringComparison.Ordinal);
        Assert.Contains("P0-5D-", verifier, StringComparison.Ordinal);
        Assert.Contains("The JSON is derived evidence; the PCAP remains authoritative", contract, StringComparison.Ordinal);
        Assert.Contains("Do not transfer an older R1/R2 capture result", contract, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
