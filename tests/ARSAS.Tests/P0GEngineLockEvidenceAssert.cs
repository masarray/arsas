using System.Text.Json;

namespace ARSAS.Tests;

/// <summary>
/// Keep the historical P0-C predecessor fixed even when an explicitly tested
/// descendant is the active code-only engine lock. Never accept a generic
/// different SHA merely because the physical-history metadata still exists.
/// </summary>
internal static class P0GEngineLockEvidenceAssert
{
    private const string P0C = "ba21d882f67de22c10d3155b449aac77aa8b4899";
    private const string P0G = "ded5036029706db1a752bb045d1fbc43b67fe9c6";

    internal static void AssertAuthorizedPinnedEngine(
        JsonElement lockRoot, JsonElement typedReadTrial)
    {
        Assert.Equal(P0C, typedReadTrial.GetProperty("exactCommit").GetString());
        Assert.Equal(181, typedReadTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.True(typedReadTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());

        var actual = lockRoot.GetProperty("commit").GetString();
        if (string.Equals(actual, P0C, StringComparison.Ordinal))
        {
            Assert.Equal(181, lockRoot.GetProperty("sourcePullRequest").GetInt32());
            return;
        }

        Assert.Equal(P0G, actual);
        Assert.Equal(182, lockRoot.GetProperty("sourcePullRequest").GetInt32());
        var successor = lockRoot.GetProperty("p0GSclEditionTrial");
        Assert.Equal("code-trial-not-physical", successor.GetProperty("status").GetString());
        Assert.Equal(P0C, successor.GetProperty("baseEngineCommit").GetString());
        Assert.Equal(P0G, successor.GetProperty("exactCommit").GetString());
        Assert.Equal(182, successor.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(39368865409L, successor.GetProperty("engineCiRun").GetInt64());
        foreach (var required in new[]
        {
            "syntheticEdition1_2_21_22MatrixRequired",
            "preserveHistoricalSclNamespaceRequired",
            "unknownReleaseCannotDowngrade",
            "existingEditionEnumOrdinalsPreserved",
            "mmsEngineUnchanged",
            "physicalQualificationRequired",
            "mainlinePromotionForbidden",
            "vendorBranchingForbidden",
            "cyclicMmsProcessPollingForbidden",
            "controlAndRcbWritesForbidden"
        })
            Assert.True(successor.GetProperty(required).GetBoolean(),
                "P0-G ancestry/safety predicate missing: " + required);
    }
}
