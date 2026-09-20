using System.Xml.Linq;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclExportSemanticParityPatchTests
{
    [Fact]
    public void PhysicalReferenceCounter_IsCorrectedOnlyInSavedScl_AndSharedTypeIsNotMutated()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-scl-semantic-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "relay.iid");

        try
        {
            File.WriteAllText(
                path,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
                  <IED name="IED">
                    <AccessPoint name="AP1">
                      <Server>
                        <LDevice inst="ADD">
                          <LN prefix="MPLS_" lnClass="GGIO" inst="1" lnType="LN_TARGET">
                            <DOI name="CBClsCounter">
                              <DAI name="stVal"><Val>false</Val></DAI>
                            </DOI>
                          </LN>
                          <LN prefix="OTHER_" lnClass="GGIO" inst="2" lnType="LN_OTHER" />
                        </LDevice>
                      </Server>
                    </AccessPoint>
                  </IED>
                  <DataTypeTemplates>
                    <LNodeType id="LN_TARGET" lnClass="GGIO">
                      <DO name="CBClsCounter" type="DO_SHARED" />
                    </LNodeType>
                    <LNodeType id="LN_OTHER" lnClass="GGIO">
                      <DO name="SomeStatus" type="DO_SHARED" />
                    </LNodeType>
                    <DOType id="DO_SHARED" cdc="SPS">
                      <DA name="stVal" fc="ST" bType="BOOLEAN" />
                      <DA name="q" fc="ST" bType="Quality" />
                      <DA name="t" fc="ST" bType="Timestamp" />
                    </DOType>
                  </DataTypeTemplates>
                </SCL>
                """);

            var liveModel = PhysicalReferenceModel();
            var liveCounter = Assert.Single(
                liveModel.LogicalDevices
                    .SelectMany(device => device.LogicalNodes)
                    .SelectMany(node => node.DataObjects));

            Assert.Equal("SPS", liveCounter.InferredCdc);

            var result = SclExportSemanticParityPatch.ApplyForLiveModel(
                liveModel,
                path);

            Assert.True(result.Changed);
            Assert.Equal(1, result.PatchedDataObjects);
            Assert.Equal(1, result.RemovedInvalidInstanceValues);

            // The model bound to discovery/reporting is read-only input to the patch.
            Assert.Equal("SPS", liveCounter.InferredCdc);

            var document = XDocument.Load(path);
            var ns = document.Root!.Name.Namespace;

            var targetLn = document.Descendants(ns + "LN").Single(
                element =>
                    (string?)element.Attribute("prefix") == "MPLS_" &&
                    (string?)element.Attribute("lnClass") == "GGIO" &&
                    (string?)element.Attribute("inst") == "1");
            var targetLnTypeId = (string?)targetLn.Attribute("lnType");
            var targetLnType = document.Descendants(ns + "LNodeType").Single(
                element => (string?)element.Attribute("id") == targetLnTypeId);
            var targetDo = targetLnType.Elements(ns + "DO").Single(
                element => (string?)element.Attribute("name") == "CBClsCounter");
            var targetDoTypeId = (string?)targetDo.Attribute("type");

            Assert.NotEqual("DO_SHARED", targetDoTypeId);

            var targetDoType = document.Descendants(ns + "DOType").Single(
                element => (string?)element.Attribute("id") == targetDoTypeId);
            Assert.Equal("INS", (string?)targetDoType.Attribute("cdc"));

            var targetStVal = targetDoType.Elements(ns + "DA").Single(
                element => (string?)element.Attribute("name") == "stVal");
            Assert.Equal("INT32", (string?)targetStVal.Attribute("bType"));
            Assert.Null(targetStVal.Attribute("type"));

            var originalSharedType = document.Descendants(ns + "DOType").Single(
                element => (string?)element.Attribute("id") == "DO_SHARED");
            Assert.Equal("SPS", (string?)originalSharedType.Attribute("cdc"));
            Assert.Equal(
                "BOOLEAN",
                (string?)originalSharedType.Elements(ns + "DA").Single(
                    element => (string?)element.Attribute("name") == "stVal")
                    .Attribute("bType"));

            var instanceStVal = targetLn
                .Elements(ns + "DOI").Single(element => (string?)element.Attribute("name") == "CBClsCounter")
                .Elements(ns + "DAI").Single(element => (string?)element.Attribute("name") == "stVal");
            Assert.Empty(instanceStVal.Elements(ns + "Val"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NonTargetLiveModel_DoesNotRewriteSavedScl()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-scl-semantic-noop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "relay.iid");

        try
        {
            const string xml =
                "<SCL xmlns=\"http://www.iec.ch/61850/2003/SCL\"><DataTypeTemplates /></SCL>";
            File.WriteAllText(path, xml);

            var result = SclExportSemanticParityPatch.ApplyForLiveModel(
                new LiveIedModelDiscoveryDocument(),
                path);

            Assert.False(result.Changed);
            Assert.Equal(xml, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveHook_IsAfterCanonicalSerialization_AndBeforeReloadValidation()
    {
        var source = ReadRepoFile("MainWindow.xaml.cs");

        var write = source.IndexOf(
            "result = CanonicalLiveIedSclExporter.WriteFiles(",
            StringComparison.Ordinal);
        var patch = source.IndexOf(
            "SclExportSemanticParityPatch.ApplyForLiveModel(",
            write,
            StringComparison.Ordinal);
        var validate = source.IndexOf(
            "canonicalReloadWorkspace = CanonicalSclReloadValidator.Validate(",
            patch,
            StringComparison.Ordinal);

        Assert.True(write >= 0);
        Assert.True(patch > write);
        Assert.True(validate > patch);

        var engineLock = ReadRepoFile("engines/ARIEC61850.lock.json");
        Assert.Contains(
            "\"commit\": \"648124097621046f5f127ceb1cf853fea54db730\"",
            engineLock,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"sourcePullRequest\": 135",
            engineLock,
            StringComparison.Ordinal);
    }

    private static LiveIedModelDiscoveryDocument PhysicalReferenceModel()
        => new()
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "AA1E1F06R4ADD",
                    Inst = "ADD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MPLS_GGIO1",
                            Prefix = "MPLS_",
                            LnClass = "GGIO",
                            LnInst = "1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "AA1E1F06R4ADD/MPLS_GGIO1.CBClsCounter",
                                    Name = "CBClsCounter",
                                    InferredCdc = "SPS"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
