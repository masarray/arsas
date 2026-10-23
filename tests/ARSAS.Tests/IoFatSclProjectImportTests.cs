using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatSclProjectImportTests
{
    [Fact]
    public async Task ProductionLoader_Siemens58_NoCommunication_ProjectsEveryStaticMember()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "Siprotec_58_member.cid");
        BuildSiemens58Fixture().Save(path);

        var imported = await new IoFatSclProjectImportService().ImportAsync(new[] { path }, "Siemens FAT");

        Assert.Single(imported.Sources);
        Assert.Equal(IoFatSourceKinds.Scl, imported.Sources[0].Kind);
        Assert.Equal(58, imported.VerificationProject.Signals.Count);
        Assert.Equal(58, imported.Project.SignalCount);
        Assert.Equal(58, imported.Project.ReadySignalCount);
        var ied = Assert.Single(imported.Project.Ieds);
        Assert.Equal("AA1C1F13R4", ied.IedName);
        Assert.Equal(string.Empty, ied.IpAddress);
        Assert.All(ied.TestPoints, point =>
        {
            Assert.True(point.TestEnabled);
            Assert.True(point.ImportReady);
            Assert.Equal("SCL_DATASET_AUTHORITY", point.BindingStatus);
            Assert.Equal(imported.Sources[0].SourceId, point.SignalAddress);
            Assert.Equal("Siprotec_58_member.cid", point.SourceSheet);
        });

        Assert.Equal(36, imported.VerificationProject.Signals.Count(signal => signal.SignalKind == FatSignalKind.Discrete));
        Assert.Equal(22, imported.VerificationProject.Signals.Count(signal => signal.SignalKind == FatSignalKind.Analog));
        Assert.Contains(imported.Findings, finding => finding.Code == "SCL_ENDPOINT_REQUIRED");
        Assert.Contains(ied.TestPoints, point => point.SourceIecReference == "AA1C1F13R4ADD/GGIO1.Dig01");
        Assert.Contains(ied.TestPoints, point => point.SourceIecReference == "AA1C1F13R4MEAS/MMXU1.Ana22");
    }

    [Fact]
    public async Task ProductionLoader_DuplicateMembershipAndMxInteger_RemainRequiredDistinctOtherRows()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "other-duplicate.cid");
        BuildOtherDuplicateFixture().Save(path);

        var imported = await new IoFatSclProjectImportService().ImportAsync(new[] { path });

        Assert.Equal(2, imported.Project.SignalCount);
        Assert.Equal(2, imported.VerificationProject.Signals.Count);
        Assert.Equal(2, imported.VerificationProject.Signals.Select(signal => signal.DataSetReference).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, imported.Project.Ieds.SelectMany(ied => ied.TestPoints).Select(point => point.TestPointId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(imported.VerificationProject.Signals, signal =>
        {
            Assert.Equal("MX", signal.FunctionalConstraint);
            Assert.Equal(FatSignalKind.Other, signal.SignalKind);
            Assert.Equal(FatCaptureMode.OperatorSnapshot, signal.CaptureMode);
            Assert.True(signal.IsIncludedInFat);
        });
        Assert.All(imported.Project.Ieds.SelectMany(ied => ied.TestPoints), point =>
        {
            Assert.True(point.TestEnabled);
            Assert.True(point.ImportReady);
            Assert.Contains("kind=Other", point.BindingEvidence, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MultiScl_ImportOrderDoesNotChangeIdentity_AndP3PersistenceKeepsRowProvenance()
    {
        var root = TempDirectory();
        var first = Path.Combine(root, "relay-a.cid");
        var second = Path.Combine(root, "relay-b.scd");
        BuildSinglePointFixture("IED-A", "S1", "ADD", "GGIO", "1", "Dig01", "ST", "BOOLEAN").Save(first);
        BuildSinglePointFixture("IED-B", "S1", "MEAS", "MMXU", "1", "Ana01", "MX", "FLOAT32").Save(second);
        var importer = new IoFatSclProjectImportService();

        var forward = await importer.ImportAsync(new[] { first, second }, "Multi SCL FAT");
        var reverse = await importer.ImportAsync(new[] { second, first }, "Multi SCL FAT");

        Assert.Equal(forward.Project.ProjectId, reverse.Project.ProjectId);
        Assert.Equal(forward.Project.SourceSetSha256, reverse.Project.SourceSetSha256);
        Assert.Equal(2, forward.Project.Sources.Count);
        Assert.Equal(2, forward.Project.SignalCount);
        Assert.Equal(2, forward.Project.Ieds.Count);

        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");
        var opened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            forward.Project,
            forward.SourceInputs,
            projectsRoot,
            evidenceRoot,
            Session);
        string excludedPointId;
        using (opened.Session)
        using (opened.Workspace)
        {
            var points = opened.Project.Ieds.SelectMany(ied => ied.TestPoints).ToArray();
            excludedPointId = points[0].TestPointId;
            points[0].TestEnabled = false;
            opened.Workspace.SaveNow();
            Assert.All(points, point =>
            {
                Assert.False(string.IsNullOrWhiteSpace(point.SignalAddress));
                Assert.False(string.IsNullOrWhiteSpace(point.SourceSheet));
                Assert.Contains("sourceSha256=", point.BindingEvidence, StringComparison.Ordinal);
            });
        }

        var restoredImport = await importer.ImportAsync(new[] { second, first }, "Multi SCL FAT");
        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            restoredImport.Project,
            restoredImport.SourceInputs,
            projectsRoot,
            evidenceRoot,
            Session);
        var package = Path.Combine(root, "p4-multi-scl.arsas");
        using (restored.Session)
        using (restored.Workspace)
        {
            Assert.True(restored.RestoredProgress);
            var point = restored.Project.Ieds.SelectMany(ied => ied.TestPoints)
                .Single(candidate => candidate.TestPointId == excludedPointId);
            Assert.False(point.TestEnabled);
            Assert.Contains(restored.Project.Sources, source => source.SourceId == point.SignalAddress);
            await IoFatProjectPackageService.ExportAsync(restored.Workspace, restored.Session, package);
        }

        var packaged = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            package,
            Path.Combine(root, "projects-package"),
            Path.Combine(root, "evidence-package"),
            Session);
        using (packaged.Session)
        using (packaged.Workspace)
        {
            Assert.Equal(2, packaged.Project.Sources.Count);
            Assert.Equal(2, packaged.Project.SignalCount);
            var point = packaged.Project.Ieds.SelectMany(ied => ied.TestPoints)
                .Single(candidate => candidate.TestPointId == excludedPointId);
            Assert.False(point.TestEnabled);
            Assert.False(string.IsNullOrWhiteSpace(point.SourceSheet));
            Assert.False(string.IsNullOrWhiteSpace(point.SignalAddress));
            Assert.Contains("dataset=", point.BindingEvidence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task IncrementalImport_PreservesExistingRuntimeWorkspaceAndAddsNextIed()
    {
        var root = TempDirectory();
        var first = Path.Combine(root, "relay-first.cid");
        var second = Path.Combine(root, "relay-next.cid");
        BuildSinglePointFixture("IED-FIRST", "S1", "ADD", "GGIO", "1", "Dig01", "ST", "BOOLEAN").Save(first);
        BuildSinglePointFixture("IED-NEXT", "S1", "MEAS", "MMXU", "1", "Ana01", "MX", "FLOAT32").Save(second);
        var importer = new IoFatSclProjectImportService();

        await importer.ImportAsync(new[] { first });
        var additional = await importer.ImportAdditionalAsync(new[] { second });

        Assert.True(importer.TryGetRuntimeWorkspace("IED-FIRST", string.Empty, out var firstWorkspace));
        Assert.True(importer.TryGetRuntimeWorkspace("IED-NEXT", string.Empty, out var nextWorkspace));
        Assert.NotNull(firstWorkspace);
        Assert.NotNull(nextWorkspace);
        Assert.Equal("IED-NEXT", Assert.Single(additional.Project.Ieds).IedName);
    }

    [Fact]
    public async Task ProductionLoader_NoStaticDataSet_ReturnsZeroRowsAndExplicitFinding()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "no-dataset.icd");
        BuildNoDataSetFixture().Save(path);

        var imported = await new IoFatSclProjectImportService().ImportAsync(new[] { path });

        Assert.Equal(0, imported.Project.SignalCount);
        Assert.Empty(imported.VerificationProject.Signals);
        Assert.Single(imported.Project.Ieds);
        Assert.Contains(imported.Findings, finding => finding.Code == "SCL_NO_STATIC_DATASET_MEMBERS");
    }

    private static XDocument BuildSiemens58Fixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var digitalDataSet = new XElement(ns + "DataSet", new XAttribute("name", "Digital"));
        foreach (var index in Enumerable.Range(1, 36))
            digitalDataSet.Add(Fcda(ns, "ADD", "GGIO", "1", $"Dig{index:00}", "ST"));

        var analogDataSet = new XElement(ns + "DataSet", new XAttribute("name", "Analog"));
        foreach (var index in Enumerable.Range(1, 22))
            analogDataSet.Add(Fcda(ns, "MEAS", "MMXU", "1", $"Ana{index:00}", "MX"));

        var ggioType = new XElement(ns + "LNodeType",
            new XAttribute("id", "GGIOType"), new XAttribute("lnClass", "GGIO"));
        foreach (var index in Enumerable.Range(1, 36))
            ggioType.Add(new XElement(ns + "DO", new XAttribute("name", $"Dig{index:00}"), new XAttribute("type", "SpsType")));

        var mmxuType = new XElement(ns + "LNodeType",
            new XAttribute("id", "MMXUType"), new XAttribute("lnClass", "MMXU"));
        foreach (var index in Enumerable.Range(1, 22))
            mmxuType.Add(new XElement(ns + "DO", new XAttribute("name", $"Ana{index:00}"), new XAttribute("type", "MvType")));

        return new XDocument(new XElement(ns + "SCL",
            new XAttribute("version", "2007"), new XAttribute("revision", "B"),
            new XElement(ns + "Header", new XAttribute("id", "SIEMENS_58_P4")),
            new XElement(ns + "IED", new XAttribute("name", "AA1C1F13R4"),
                new XElement(ns + "AccessPoint", new XAttribute("name", "E"),
                    new XElement(ns + "Server",
                        new XElement(ns + "LDevice", new XAttribute("inst", "Application"),
                            new XElement(ns + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", "LLN0Type"), analogDataSet, digitalDataSet)),
                        new XElement(ns + "LDevice", new XAttribute("inst", "ADD"),
                            new XElement(ns + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", "LLN0Type")),
                            new XElement(ns + "LN", new XAttribute("lnClass", "GGIO"), new XAttribute("inst", "1"), new XAttribute("lnType", "GGIOType"))),
                        new XElement(ns + "LDevice", new XAttribute("inst", "MEAS"),
                            new XElement(ns + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", "LLN0Type")),
                            new XElement(ns + "LN", new XAttribute("lnClass", "MMXU"), new XAttribute("inst", "1"), new XAttribute("lnType", "MMXUType")))))),
            new XElement(ns + "DataTypeTemplates",
                new XElement(ns + "LNodeType", new XAttribute("id", "LLN0Type"), new XAttribute("lnClass", "LLN0")),
                ggioType,
                mmxuType,
                new XElement(ns + "DOType", new XAttribute("id", "SpsType"), new XAttribute("cdc", "SPS"),
                    new XElement(ns + "DA", new XAttribute("name", "stVal"), new XAttribute("bType", "BOOLEAN"), new XAttribute("fc", "ST"))),
                new XElement(ns + "DOType", new XAttribute("id", "MvType"), new XAttribute("cdc", "MV"),
                    new XElement(ns + "DA", new XAttribute("name", "mag"), new XAttribute("bType", "Struct"), new XAttribute("type", "AnalogueValue"), new XAttribute("fc", "MX"))),
                new XElement(ns + "DAType", new XAttribute("id", "AnalogueValue"),
                    new XElement(ns + "BDA", new XAttribute("name", "f"), new XAttribute("bType", "FLOAT32"))))));
    }

    private static XDocument BuildOtherDuplicateFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return BuildBaseScl(
            ns,
            "IED-OTHER",
            "S1",
            "MEAS",
            "MMXU",
            "1",
            "Level01",
            "MX",
            new XElement(ns + "DOType", new XAttribute("id", "PointType"), new XAttribute("cdc", "MV"),
                new XElement(ns + "DA", new XAttribute("name", "mag"), new XAttribute("bType", "INT32"), new XAttribute("fc", "MX"))),
            new[]
            {
                new XElement(ns + "DataSet", new XAttribute("name", "FAT_A"), Fcda(ns, "MEAS", "MMXU", "1", "Level01", "MX", "mag")),
                new XElement(ns + "DataSet", new XAttribute("name", "FAT_B"), Fcda(ns, "MEAS", "MMXU", "1", "Level01", "MX", "mag"))
            });
    }

    private static XDocument BuildSinglePointFixture(
        string iedName,
        string accessPoint,
        string ldInst,
        string lnClass,
        string lnInst,
        string doName,
        string fc,
        string bType)
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var daName = fc.Equals("MX", StringComparison.OrdinalIgnoreCase) ? "mag" : "stVal";
        var cdc = fc.Equals("MX", StringComparison.OrdinalIgnoreCase) ? "MV" : "SPS";
        var doType = new XElement(ns + "DOType", new XAttribute("id", "PointType"), new XAttribute("cdc", cdc),
            new XElement(ns + "DA", new XAttribute("name", daName), new XAttribute("bType", bType), new XAttribute("fc", fc)));
        return BuildBaseScl(ns, iedName, accessPoint, ldInst, lnClass, lnInst, doName, fc, doType,
            new[] { new XElement(ns + "DataSet", new XAttribute("name", "FAT"), Fcda(ns, ldInst, lnClass, lnInst, doName, fc, daName)) });
    }

    private static XDocument BuildNoDataSetFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return BuildBaseScl(
            ns,
            "IED-NO-DS",
            "S1",
            "ADD",
            "GGIO",
            "1",
            "Dig01",
            "ST",
            new XElement(ns + "DOType", new XAttribute("id", "PointType"), new XAttribute("cdc", "SPS"),
                new XElement(ns + "DA", new XAttribute("name", "stVal"), new XAttribute("bType", "BOOLEAN"), new XAttribute("fc", "ST"))),
            Array.Empty<XElement>());
    }

    private static XDocument BuildBaseScl(
        XNamespace ns,
        string iedName,
        string accessPoint,
        string ldInst,
        string lnClass,
        string lnInst,
        string doName,
        string fc,
        XElement doType,
        IReadOnlyCollection<XElement> dataSets)
    {
        var ln0 = new XElement(ns + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", "LLN0Type"));
        foreach (var dataSet in dataSets)
            ln0.Add(dataSet);
        return new XDocument(new XElement(ns + "SCL",
            new XAttribute("version", "2007"), new XAttribute("revision", "B"),
            new XElement(ns + "Header", new XAttribute("id", "P4")),
            new XElement(ns + "IED", new XAttribute("name", iedName),
                new XElement(ns + "AccessPoint", new XAttribute("name", accessPoint),
                    new XElement(ns + "Server",
                        new XElement(ns + "LDevice", new XAttribute("inst", "Application"), ln0),
                        new XElement(ns + "LDevice", new XAttribute("inst", ldInst),
                            new XElement(ns + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", "LLN0Type")),
                            new XElement(ns + "LN", new XAttribute("lnClass", lnClass), new XAttribute("inst", lnInst), new XAttribute("lnType", "PointLnType")))))),
            new XElement(ns + "DataTypeTemplates",
                new XElement(ns + "LNodeType", new XAttribute("id", "LLN0Type"), new XAttribute("lnClass", "LLN0")),
                new XElement(ns + "LNodeType", new XAttribute("id", "PointLnType"), new XAttribute("lnClass", lnClass),
                    new XElement(ns + "DO", new XAttribute("name", doName), new XAttribute("type", "PointType"))),
                doType)));
    }

    private static XElement Fcda(
        XNamespace ns,
        string ldInst,
        string lnClass,
        string lnInst,
        string doName,
        string fc,
        string? daName = null)
    {
        var element = new XElement(ns + "FCDA",
            new XAttribute("ldInst", ldInst),
            new XAttribute("lnClass", lnClass),
            new XAttribute("lnInst", lnInst),
            new XAttribute("doName", doName),
            new XAttribute("fc", fc));
        if (!string.IsNullOrWhiteSpace(daName))
            element.Add(new XAttribute("daName", daName));
        return element;
    }

    private static IoTestSessionController Session(IoTestProject project, string evidenceRoot)
        => new(project, _ => null, action => action(), evidenceRoot);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
