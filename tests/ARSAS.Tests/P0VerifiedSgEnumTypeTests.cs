using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P0VerifiedSgEnumTypeTests
{
    private const string Owner = "BCUGELD0/SGgndPIOC1.InMagTyp";
    private const string Raw = Owner + ".setVal";
    private const string Scl = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
          <IED name="BCUGE">
            <AccessPoint name="AP1">
              <Server>
                <LDevice inst="LD0"><LN prefix="SGgnd" lnClass="PIOC" inst="1" lnType="PIOCType"/></LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="PIOCType" lnClass="PIOC">
              <DO name="InMagTyp" type="BCUGESelector"/>
              <DO name="AnotherSetting" type="AnotherSelector"/>
            </LNodeType>
            <DOType id="BCUGESelector" cdc="ENG">
              <DA name="setVal" fc="SG" bType="Enum" type="BCUGEInMagKind"/>
            </DOType>
            <DOType id="AnotherSelector" cdc="ENG">
              <DA name="setVal" fc="SG" bType="Enum" type="BCUGEOtherKind"/>
            </DOType>
            <EnumType id="BCUGEInMagKind">
              <EnumVal ord="0">phasor</EnumVal>
              <EnumVal ord="1">amplitude</EnumVal>
            </EnumType>
            <EnumType id="BCUGEOtherKind">
              <EnumVal ord="0">not-phasor</EnumVal>
            </EnumType>
          </DataTypeTemplates>
        </SCL>
        """;

    private static LiveIedModelDiscoveryDocument Online(string type = "enumerated",
        string fc = "SG", bool exact = true)
    {
        LiveIedDataAttributeModel Attribute(string reference) => new()
        {
            ObjectReference = reference[..reference.LastIndexOf('.')],
            AttributePath = reference[(reference.IndexOf('/') + 1)..],
            MmsReference = reference,
            FunctionalConstraint = fc,
            SclBType = "Enum",
            MmsType = type,
            TypeDiscoveryStatus = exact ? "Exact" : "NotRead",
            TypeSource = exact ? "GetVariableAccessAttributesLogicalNodeTree"
                : "NameListHeuristic"
        };
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGELD0",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "SGgndPIOC1", LnClass = "PIOC",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "InMagTyp", Reference = Owner,
                        Attributes = [Attribute(Raw)]
                    },new LiveIedDataObjectModel
                    {
                        Name = "AnotherSetting", Reference = "BCUGELD0/SGgndPIOC1.AnotherSetting",
                        Attributes = [Attribute("BCUGELD0/SGgndPIOC1.AnotherSetting.setVal")]
                    }]
                }]
            }]
        };
    }

    private static IedBrowserSclEnumEvidence Resolve(string xml = Scl,
        LiveIedModelDiscoveryDocument? online = null,
        string iedName = "BCUGE")
    {
        var design = SclLiveModelProjectionBuilder.Build(XDocument.Parse(xml));
        var workspace = new SclIedWorkspace
        {
            IedName = iedName, DesignModel = design
        };
        return IedBrowserSclEnumEvidence.Build(
            new VerifiedSclSource("trusted.cid", new string('a', 64), xml),
            workspace, online ?? Online());
    }

    [Fact]
    public void Sg01_ExactVerifiedSclEnumTypeMapsOrdinalOnlyForItsOwnSetting()
    {
        var types = Resolve();
        Assert.Equal(2, types.BoundAttributeCount);
        Assert.True(types.TryFormat(Raw, "SG", "0",
            out var display, out var evidence));
        Assert.Equal("phasor", display);
        Assert.Contains("BCUGEInMagKind", evidence);
        Assert.Contains("ord=0", evidence);
        Assert.True(types.TryFormat(Raw, "SG", "value1",
            out display, out _));
        Assert.Equal("amplitude", display);
        Assert.True(types.TryFormat("BCUGELD0/SGgndPIOC1.AnotherSetting.setVal",
            "SG", "0", out display, out _));
        Assert.Equal("not-phasor", display);
        Assert.False(types.TryFormat(Raw, "SE", "0", out display, out _));
        Assert.Equal("0", display);
    }

    [Fact]
    public void Sg02_UnknownOrdinalAndUnverifiedMmsTypeRemainRaw()
    {
        var types = Resolve();
        Assert.False(types.TryFormat(Raw, "SG", "99",
            out var display, out var reason));
        Assert.Equal("99", display);
        Assert.Contains("no ordinal 99", reason);
        Assert.Equal(0, Resolve(online: Online(exact: false)).BoundAttributeCount);
        Assert.Equal(0, Resolve(online: Online(type: "boolean")).BoundAttributeCount);
        Assert.Equal(0, Resolve(online: Online(fc: "SE")).BoundAttributeCount);
        Assert.Equal(0, Resolve(iedName: "WRONG").BoundAttributeCount);
    }

    [Fact]
    public void Sg03_DuplicatedEnumTypeIdMustFailClosed()
    {
        var duplicate = Scl.Replace("</DataTypeTemplates>",
            """
            <EnumType id="BCUGEInMagKind"><EnumVal ord="0">wrong</EnumVal></EnumType>
            </DataTypeTemplates>
            """);
        var types = Resolve(duplicate);
        Assert.False(types.TryFormat(Raw, "SG", "0",
            out var raw, out _));
        Assert.Equal("0", raw);
        Assert.True(types.TryFormat("BCUGELD0/SGgndPIOC1.AnotherSetting.setVal",
            "SG", "0", out var other, out _));
        Assert.Equal("not-phasor", other);
    }

    [Fact]
    public void Sg04_LiveOrdinalRemainsUntouchedInAssociationBoundCanonicalLedger()
    {
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, Raw, "SG",
            "0", "", "", "MMS Read"));
        var types = Resolve();
        Assert.True(ledger.TryGet(Raw, "SG", out var evidence));
        Assert.Equal("0", evidence.Value);
        Assert.True(types.TryFormat(Raw, "SG", evidence.Value, out var display, out _));
        Assert.Equal("phasor", display);
        Assert.Equal("0", evidence.Value);
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(Raw, "SG", out _));
    }
}
