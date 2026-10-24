using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P0GrxSclLiveCswiParityTests
{
    private const string Target = "GR_X_7SX85CB1/CSWI1.Pos";

    private static LiveIedDataAttributeModel Field(string path, string fc)
        => new()
        {
            ObjectReference = Target + "." + path,
            AttributePath = path,
            FunctionalConstraint = fc,
            SclBType = path.Equals("ctlModel", StringComparison.Ordinal)
                ? "Enum" : "Struct"
        };

    private static LiveIedModelDiscoveryDocument Model(bool includeCswi)
    {
        var objects = includeCswi ? new[]
        {
            new LiveIedDataObjectModel
            {
                Name = "Pos", Reference = Target, InferredCdc = "DPC",
                Attributes =
                [
                    Field("stVal", "ST"),
                    Field("ctlModel", "CF"),
                    Field("SBO", "CO"),
                    Field("SBOw.ctlVal", "CO"),
                    Field("Oper.ctlVal", "CO"),
                    Field("Cancel.ctlVal", "CO")
                ]
            }
        } : [];
        return new LiveIedModelDiscoveryDocument
        {
            IedName = "GR_X_7SX85", Source = "synthetic SCL/MMS",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "GR_X_7SX85CB1", Inst = "CB1",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "CSWI1", LnClass = "CSWI", LnInst = "1",
                            DataObjects = objects
                        },
                        new LiveIedLogicalNodeModel
                        {
                            Name = "XCBR1", LnClass = "XCBR", LnInst = "1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "Pos", Reference = "GR_X_7SX85CB1/XCBR1.Pos",
                                    InferredCdc = "DPC",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "GR_X_7SX85CB1/XCBR1.Pos.ctlModel",
                                            AttributePath = "ctlModel",
                                            FunctionalConstraint = "CF"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void SclDeclaredCswiDoesNotGrantMmsOrOperateAuthority()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "GR_X_7SX85",
            SclWorkspace = new SclIedWorkspace
            {
                IedName = "GR_X_7SX85", AccessPointName = "J",
                DesignModel = Model(true)
            },
            LiveDiscoveryModel = Model(false)
        };
        var lines = Iec61850SclLiveCswiParityDiagnostic.Summarize(device);
        Assert.Contains(lines, s => s.Contains(
            "design=1, live=0, sameExactDO=0", StringComparison.Ordinal));
        Assert.Contains(lines, s => s.Contains("SCL_ONLY / NOT_LIVE_PROVEN", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, s => s.Contains("OPERABLE /", StringComparison.Ordinal));
    }

    [Fact]
    public void ExactLiveCswiDeclarationStillDoesNotMeanCtlModelVerified()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "GR_X_7SX85",
            SclWorkspace = new SclIedWorkspace
            {
                IedName = "GR_X_7SX85", AccessPointName = "J",
                DesignModel = Model(true)
            },
            LiveDiscoveryModel = Model(true)
        };
        var lines = Iec61850SclLiveCswiParityDiagnostic.Summarize(device);
        Assert.Contains(lines, s => s.Contains(
            "design=1, live=1, sameExactDO=1, liveCOdeclared=1, liveOperable=0",
            StringComparison.Ordinal));
        Assert.Contains(lines, s => s.Contains("SCD_CO=Oper/SBO/SBOw/Cancel",
            StringComparison.Ordinal));
        Assert.Contains(lines, s => s.Contains("MMS_CF_ctlModel=True",
            StringComparison.Ordinal));
        Assert.Contains(lines, s => s.Contains("MODEL_ONLY / LIVE_COMMAND_UNVERIFIED",
            StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutOpenedSclReportAnExplicitProvenanceGap()
    {
        var device = new Iec61850MonitorDevice { LiveDiscoveryModel = Model(true) };
        var lines = Iec61850SclLiveCswiParityDiagnostic.Summarize(device);
        Assert.Contains(lines, s => s.Contains("NO OPEN SCD", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, s => s.Contains("liveOperable=", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticContainsNoBackgroundMmsWriteOrUnboundedModelText()
    {
        var code = Find("Services/Iec61850SclLiveCswiParityDiagnostic.cs");
        var builder = Find("Services/DiagnosticReportBuilder.cs");
        Assert.Contains("Math.Clamp(sampleBudget, 0, 24)", code);
        Assert.Contains("Iec61850SclLiveCswiParityDiagnostic.Summarize(device)", builder);
        Assert.DoesNotContain("ExecuteControlAsync(", code);
        Assert.DoesNotContain("ReadValueAsync(", code);
        Assert.DoesNotContain("ConfigureControlModelAsync(", code);
    }

    private static string Find(string relative)
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory);
             root is not null; root = root.Parent)
        {
            var path = Path.Combine(root.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }
}
