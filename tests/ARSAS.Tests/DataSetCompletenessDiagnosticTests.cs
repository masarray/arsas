using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DataSetCompletenessDiagnosticTests
{
    [Fact]
    public void SiemensLike_CrossLd_Fcda_Is_Canonical_Visible_And_Diagnostically_Complete()
    {
        var model = SclLiveModelProjectionBuilder.Build(
            XDocument.Parse(CrossLogicalDeviceFixture()),
            "Siprotec_cross_ld.cid");
        var signals = new List<SignalDefinition>();

        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, model);
        var snapshot = Iec61850DataSetCompletenessDiagnostic.Evaluate(model, signals);

        Assert.Equal(1, merge.MandatoryCatalogCount);
        var signal = Assert.Single(signals);

        // IEC Telegram / Signal Selection must preserve the exact static FCDA FCD identity.
        Assert.Equal("AA1C1F13R4ADD/GGIO6.CBOpnd", signal.DisplayReference);
        Assert.False(signal.DisplayReference.EndsWith(".stVal", StringComparison.Ordinal));
        Assert.DoesNotContain("[ST]", signal.DisplayReference);

        // Runtime acquisition may use the primary DataAttribute resolved by ARIEC. Keeping
        // this separate from DisplayReference prevents the UI from rewriting the DataSet.
        Assert.Equal("AA1C1F13R4ADD/GGIO6.CBOpnd.stVal", signal.ObjectReference);

        Assert.Equal(1, snapshot.DataSetCount);
        Assert.Equal(1, snapshot.StaticMemberCount);
        Assert.Equal(1, snapshot.MandatoryInventoryCount);
        Assert.Equal(1, snapshot.RepresentedCount);
        Assert.Equal(0, snapshot.MissingCount);
        Assert.True(snapshot.IsComplete);
    }

    [Fact]
    public void Missing_Static_DataSet_Member_Is_Reported_With_DataSet_Index_And_Reference()
    {
        var model = SclLiveModelProjectionBuilder.Build(
            XDocument.Parse(CrossLogicalDeviceFixture()),
            "Siprotec_cross_ld.cid");

        var snapshot = Iec61850DataSetCompletenessDiagnostic.Evaluate(
            model,
            Array.Empty<SignalDefinition>());

        Assert.Equal(1, snapshot.MandatoryInventoryCount);
        Assert.Equal(0, snapshot.RepresentedCount);
        Assert.Equal(1, snapshot.MissingCount);
        Assert.Contains("AA1C1F13R4ADD/GGIO6.CBOpnd", snapshot.MissingReferences[0]);
        Assert.DoesNotContain("CBOpnd.stVal", snapshot.MissingReferences[0]);
        Assert.Contains("[1]", snapshot.MissingReferences[0]);
        Assert.False(snapshot.IsComplete);
    }

    private static string CrossLogicalDeviceFixture() => """
    <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
      <Header id="SIEMENS_CROSS_LD" version="1" revision="0" />
      <IED name="AA1C1F13R4">
        <AccessPoint name="E">
          <Server>
            <LDevice inst="Application">
              <LN0 lnClass="LLN0" lnType="LLN0Type">
                <DataSet name="Digital">
                  <FCDA ldInst="ADD" lnClass="GGIO" lnInst="6" doName="CBOpnd" fc="ST" />
                </DataSet>
              </LN0>
            </LDevice>
            <LDevice inst="ADD">
              <LN0 lnClass="LLN0" lnType="LLN0Type" />
              <LN lnClass="GGIO" inst="6" lnType="GGIOType" />
            </LDevice>
          </Server>
        </AccessPoint>
      </IED>
      <DataTypeTemplates>
        <LNodeType id="LLN0Type" lnClass="LLN0" />
        <LNodeType id="GGIOType" lnClass="GGIO">
          <DO name="CBOpnd" type="SpsType" />
        </LNodeType>
        <DOType id="SpsType" cdc="SPS">
          <DA name="stVal" bType="BOOLEAN" fc="ST" />
          <DA name="q" bType="Quality" fc="ST" />
          <DA name="t" bType="Timestamp" fc="ST" />
        </DOType>
      </DataTypeTemplates>
    </SCL>
    """;
}
