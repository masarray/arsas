using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatSclWorkspaceBootstrapRegressionTests
{
    [Fact]
    public async Task SiemensLikeStaticDataSet_All58MembershipsBecomeIncludedFatRows()
    {
        using var temp = new TempDirectory();
        var sclPath = Path.Combine(temp.Path, "siemens-like.cid");
        BuildSiemensLikeFixture("IED_58", "S1").Save(sclPath);
        var originalBytes = await File.ReadAllBytesAsync(sclPath);

        var result = await new FatSclWorkspaceBootstrapService().OpenAsync(
            new[] { sclPath },
            Path.Combine(temp.Path, "projects"));

        Assert.Equal(58, result.Project.Signals.Count);
        Assert.Equal(36, result.Project.Signals.Count(signal => signal.SignalKind == FatSignalKind.Discrete));
        Assert.Equal(22, result.Project.Signals.Count(signal => signal.SignalKind == FatSignalKind.Analog));
        Assert.DoesNotContain(result.Project.Signals, signal => signal.SignalKind == FatSignalKind.Other);
        Assert.All(result.Project.Signals, signal => Assert.True(signal.IsIncludedInFat));
        Assert.Single(result.SourceFiles);
        Assert.Equal(IoFatSourceKinds.Scl, result.SourceFiles[0].Source.Kind);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(result.SourceFiles[0].LocalPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sclPath));
    }

    [Fact]
    public async Task MultipleSclFiles_SelectionOrderDoesNotChangeSourceSetOrEngineeringFingerprint()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "relay-a.cid");
        var second = Path.Combine(temp.Path, "relay-b.cid");
        BuildSingleMemberFixture("IED_A", "S1", "Dig01", "ST", "BOOLEAN").Save(first);
        BuildSingleMemberFixture("IED_B", "S1", "Dig02", "ST", "BOOLEAN").Save(second);

        var service = new FatSclWorkspaceBootstrapService();
        var forward = await service.OpenAsync(
            new[] { first, second },
            Path.Combine(temp.Path, "projects"));
        var reverse = await service.OpenAsync(
            new[] { second, first },
            Path.Combine(temp.Path, "projects"));

        Assert.Equal(2, forward.Project.Signals.Count);
        Assert.Equal(2, reverse.Project.Signals.Count);
        Assert.Equal(forward.SourceSetSha256, reverse.SourceSetSha256);
        Assert.Equal(forward.EngineeringFingerprint, reverse.EngineeringFingerprint);
        Assert.Equal(forward.WorkspaceDirectory, reverse.WorkspaceDirectory);
        Assert.Equal(
            forward.Project.Signals.Select(signal => signal.SignalId).OrderBy(value => value),
            reverse.Project.Signals.Select(signal => signal.SignalId).OrderBy(value => value));
    }

    [Fact]
    public async Task NonStMxStaticMember_IsPreservedAsOtherInsteadOfFilteredOut()
    {
        using var temp = new TempDirectory();
        var sclPath = Path.Combine(temp.Path, "other.cid");
        BuildSingleMemberFixture("IED_OTHER", "S1", "SetVal", "CF", "INT32").Save(sclPath);

        var result = await new FatSclWorkspaceBootstrapService().OpenAsync(
            new[] { sclPath },
            Path.Combine(temp.Path, "projects"));

        var signal = Assert.Single(result.Project.Signals);
        Assert.Equal("CF", signal.FunctionalConstraint);
        Assert.Equal(FatSignalKind.Other, signal.SignalKind);
        Assert.Equal(FatCaptureMode.OperatorSnapshot, signal.CaptureMode);
        Assert.True(signal.IsIncludedInFat);
    }

    [Fact]
    public async Task CompetingSourcesForSameIedAccessPoint_FailClosed()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "relay-old.cid");
        var second = Path.Combine(temp.Path, "relay-new.cid");
        BuildSingleMemberFixture("IED_A", "S1", "Dig01", "ST", "BOOLEAN").Save(first);
        BuildSingleMemberFixture("IED_A", "S1", "Dig02", "ST", "BOOLEAN").Save(second);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new FatSclWorkspaceBootstrapService().OpenAsync(
                new[] { first, second },
                Path.Combine(temp.Path, "projects")));

        Assert.Contains("Conflicting SCL sources", error.Message, StringComparison.Ordinal);
        Assert.Contains("IED_A|S1", error.Message, StringComparison.Ordinal);
    }

    private static XDocument BuildSiemensLikeFixture(string iedName, string accessPointName)
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var dataSet = new XElement(ns + "DataSet", new XAttribute("name", "FAT"));
        for (var index = 1; index <= 36; index++)
        {
            dataSet.Add(Fcda(ns, "ADD", "GGIO", "1", $"Dig{index:00}", "ST"));
        }
        for (var index = 1; index <= 22; index++)
        {
            dataSet.Add(Fcda(ns, "MEAS", "MMXU", "1", $"Ana{index:00}", "MX"));
        }

        var ggioType = new XElement(
            ns + "LNodeType",
            new XAttribute("id", "GGIOType"),
            new XAttribute("lnClass", "GGIO"));
        for (var index = 1; index <= 36; index++)
        {
            ggioType.Add(new XElement(
                ns + "DO",
                new XAttribute("name", $"Dig{index:00}"),
                new XAttribute("type", "SpsType")));
        }

        var mmxuType = new XElement(
            ns + "LNodeType",
            new XAttribute("id", "MMXUType"),
            new XAttribute("lnClass", "MMXU"));
        for (var index = 1; index <= 22; index++)
        {
            mmxuType.Add(new XElement(
                ns + "DO",
                new XAttribute("name", $"Ana{index:00}"),
                new XAttribute("type", "MvType")));
        }

        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "FAT_58")),
                BuildIed(ns, iedName, accessPointName, dataSet,
                    ("ADD", "GGIO", "1", "GGIOType"),
                    ("MEAS", "MMXU", "1", "MMXUType")),
                new XElement(ns + "DataTypeTemplates",
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "LLN0Type"),
                        new XAttribute("lnClass", "LLN0")),
                    ggioType,
                    mmxuType,
                    new XElement(ns + "DOType",
                        new XAttribute("id", "SpsType"),
                        new XAttribute("cdc", "SPS"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "stVal"),
                            new XAttribute("bType", "BOOLEAN"),
                            new XAttribute("fc", "ST"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "MvType"),
                        new XAttribute("cdc", "MV"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "mag"),
                            new XAttribute("bType", "Struct"),
                            new XAttribute("type", "AnalogueValue"),
                            new XAttribute("fc", "MX"))),
                    new XElement(ns + "DAType",
                        new XAttribute("id", "AnalogueValue"),
                        new XElement(ns + "BDA",
                            new XAttribute("name", "f"),
                            new XAttribute("bType", "FLOAT32"))))));
    }

    private static XDocument BuildSingleMemberFixture(
        string iedName,
        string accessPointName,
        string doName,
        string fc,
        string bType)
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var dataSet = new XElement(
            ns + "DataSet",
            new XAttribute("name", "FAT"),
            Fcda(ns, "ADD", "GGIO", "1", doName, fc));
        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", $"FAT_{iedName}_{doName}")),
                BuildIed(ns, iedName, accessPointName, dataSet, ("ADD", "GGIO", "1", "GGIOType")),
                new XElement(ns + "DataTypeTemplates",
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "LLN0Type"),
                        new XAttribute("lnClass", "LLN0")),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "GGIOType"),
                        new XAttribute("lnClass", "GGIO"),
                        new XElement(ns + "DO",
                            new XAttribute("name", doName),
                            new XAttribute("type", "DoType"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "DoType"),
                        new XAttribute("cdc", fc.Equals("ST", StringComparison.OrdinalIgnoreCase) ? "SPS" : "ING"),
                        new XElement(ns + "DA",
                            new XAttribute("name", fc.Equals("ST", StringComparison.OrdinalIgnoreCase) ? "stVal" : "setVal"),
                            new XAttribute("bType", bType),
                            new XAttribute("fc", fc))))));
    }

    private static XElement BuildIed(
        XNamespace ns,
        string iedName,
        string accessPointName,
        XElement dataSet,
        params (string LdInst, string LnClass, string LnInst, string LnType)[] logicalNodes)
    {
        var server = new XElement(
            ns + "Server",
            new XElement(ns + "LDevice",
                new XAttribute("inst", "Application"),
                new XElement(ns + "LN0",
                    new XAttribute("lnClass", "LLN0"),
                    new XAttribute("lnType", "LLN0Type"),
                    dataSet)));
        foreach (var logicalNode in logicalNodes)
        {
            server.Add(new XElement(
                ns + "LDevice",
                new XAttribute("inst", logicalNode.LdInst),
                new XElement(ns + "LN0",
                    new XAttribute("lnClass", "LLN0"),
                    new XAttribute("lnType", "LLN0Type")),
                new XElement(ns + "LN",
                    new XAttribute("lnClass", logicalNode.LnClass),
                    new XAttribute("inst", logicalNode.LnInst),
                    new XAttribute("lnType", logicalNode.LnType))));
        }

        return new XElement(
            ns + "IED",
            new XAttribute("name", iedName),
            new XElement(ns + "AccessPoint",
                new XAttribute("name", accessPointName),
                server));
    }

    private static XElement Fcda(
        XNamespace ns,
        string ldInst,
        string lnClass,
        string lnInst,
        string doName,
        string fc)
        => new(ns + "FCDA",
            new XAttribute("ldInst", ldInst),
            new XAttribute("lnClass", lnClass),
            new XAttribute("lnInst", lnInst),
            new XAttribute("doName", doName),
            new XAttribute("fc", fc));

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "arsas-fat-v2-p4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
            catch
            {
                // Best-effort cleanup only; never hide the assertion result.
            }
        }
    }
}
