using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportQualificationProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arsas-g2-profile-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsIdentityCompatibleEnvelopeProfile()
    {
        var store = new DynamicReportQualificationProfileStore(_root);
        var profile = Profile(Identity());

        await store.SaveAsync(profile);
        var loaded = await store.LoadAsync(Identity());

        Assert.True(loaded.Exists);
        Assert.True(loaded.IsValid);
        Assert.NotNull(loaded.Profile);
        Assert.Equal(ArMms.MmsDynamicReportQualificationState.EnvelopeQualified, loaded.Profile!.State);
        Assert.Equal(8, loaded.Profile.ProvenSafeMemberCount);
        Assert.Equal(profile.Identity.ModelFingerprint, loaded.Profile.Identity.ModelFingerprint);
    }

    [Fact]
    public void ProfilePath_IsDeterministicCaseInsensitiveAndStaysInsideRoot()
    {
        var store = new DynamicReportQualificationProfileStore(_root);
        var a = store.GetProfilePath(Identity() with { StableIdentityKey = "IED:Station-A/Q0" });
        var b = store.GetProfilePath(Identity() with { StableIdentityKey = "ied:station-a/q0" });
        var hostile = store.GetProfilePath(Identity() with { StableIdentityKey = "../../outside/device" });

        Assert.Equal(a, b);
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(hostile));
        Assert.EndsWith(".json", hostile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", Path.GetFileName(hostile));
    }

    [Fact]
    public async Task DifferentStableIdentities_UseDifferentFiles()
    {
        var store = new DynamicReportQualificationProfileStore(_root);

        var a = store.GetProfilePath(Identity() with { StableIdentityKey = "ied:a" });
        var b = store.GetProfilePath(Identity() with { StableIdentityKey = "ied:b" });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task CorruptJson_FailsClosedWithoutThrowing()
    {
        var store = new DynamicReportQualificationProfileStore(_root);
        Directory.CreateDirectory(_root);
        var path = store.GetProfilePath(Identity());
        await File.WriteAllTextAsync(path, "{ not-json");

        var loaded = await store.LoadAsync(Identity());

        Assert.True(loaded.Exists);
        Assert.False(loaded.IsValid);
        Assert.Null(loaded.Profile);
        Assert.Contains("unreadable", loaded.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameStableIdentityButChangedFingerprint_IsInvalidated()
    {
        var store = new DynamicReportQualificationProfileStore(_root);
        var profile = Profile(Identity());
        await store.SaveAsync(profile);

        var current = Identity() with { ModelFingerprint = "sha256:changed" };
        var loaded = await store.LoadAsync(current);

        Assert.True(loaded.Exists);
        Assert.False(loaded.IsValid);
        Assert.NotNull(loaded.Profile);
        Assert.Equal(
            ArMms.MmsDynamicReportProfileCompatibilityStatus.ModelFingerprintMismatch,
            loaded.Compatibility!.Status);
    }

    [Fact]
    public async Task MissingProfile_IsReportedWithoutCreatingAnything()
    {
        var store = new DynamicReportQualificationProfileStore(_root);

        var loaded = await store.LoadAsync(Identity());

        Assert.False(loaded.Exists);
        Assert.False(loaded.IsValid);
        Assert.False(File.Exists(loaded.FilePath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private static ArMms.MmsDynamicReportQualificationProfile Profile(ArMms.MmsDynamicReportIedIdentity identity)
    {
        var refs = Enumerable.Range(1, 8)
            .Select(index => $"LD0/GGIO1$ST$Ind{index}$stVal")
            .ToArray();
        var assessment = ArMms.MmsDynamicDataSetQualificationLadder.Assess(
        [
            Attempt("q1", refs[..1], 96),
            Attempt("q8", refs, 384)
        ]);
        var envelope = ArMms.MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(assessment, "q8");

        return ArMms.MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            identity,
            envelope,
            assessment,
            capacityEvidence: null,
            sourceEvidenceId: "field-q-1",
            nowUtc: DateTimeOffset.Parse("2026-08-19T12:00:00Z"));
    }

    private static ArMms.MmsDynamicDataSetQualificationAttemptEvidence Attempt(
        string id,
        IReadOnlyList<string> members,
        int bytes)
        => new()
        {
            AttemptId = id,
            ObservedAtUtc = DateTimeOffset.Parse("2026-08-19T11:00:00Z").AddMinutes(members.Count),
            DataSetReference = "LD0/LLN0.AR_G2Q",
            MemberReferences = members.ToArray(),
            DefineRequestByteCount = bytes,
            NegotiatedMaxMmsPduSize = 65000,
            RequestWithinKnownNegotiatedPdu = true,
            IsSuccess = true,
            FailureStage = ArMms.MmsDynamicDataSetQualificationFailureStage.None,
            DynamicMutationAttempted = true,
            AssociationSurvived = true,
            CleanupSucceeded = true
        };

    private static ArMms.MmsDynamicReportIedIdentity Identity()
        => new()
        {
            StableIdentityKey = "ied:station-a:q0",
            ModelFingerprint = "sha256:model-001",
            Model = "SIPROTEC-Q0",
            FirmwareRevision = "1.2.3",
            ProfileRevision = "cfg-42"
        };
}
