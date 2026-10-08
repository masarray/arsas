using AR.Iec61850.Scl;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclAssistedConnectionPreparationInteroperabilityTests
{
    [Fact]
    public void Incomplete_Cid_Association_Identity_Remains_Connectable_Without_Discovery_Fallback()
    {
        const string xml = """
        <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
          <Communication>
            <SubNetwork name="StationBus" type="8-MMS">
              <ConnectedAP iedName="IED1" apName="P1">
                <Address>
                  <P type="IP">192.0.2.10</P>
                  <P type="IP-SUBNET">255.255.255.0</P>
                  <P type="OSI-PSEL">00000001</P>
                  <P type="OSI-SSEL">0001</P>
                  <P type="OSI-TSEL">0001</P>
                </Address>
              </ConnectedAP>
            </SubNetwork>
          </Communication>
          <IED name="IED1">
            <AccessPoint name="P1">
              <Server>
                <LDevice inst="APP" ldName="EXACT_APP_DOMAIN">
                  <LN0 lnClass="LLN0" lnType="LN0_T">
                    <DataSet name="Digital">
                      <FCDA ldInst="APP" lnClass="MMXU" lnInst="1" doName="TotW" daName="mag.f" fc="MX" />
                    </DataSet>
                    <ReportControl name="Buffer" buffered="true" indexed="true" datSet="Digital" rptID="BR" confRev="1" />
                  </LN0>
                  <LN prefix="" lnClass="MMXU" inst="1" lnType="MMXU_T" />
                </LDevice>
              </Server>
            </AccessPoint>
          </IED>
          <DataTypeTemplates>
            <LNodeType id="LN0_T" lnClass="LLN0" />
            <LNodeType id="MMXU_T" lnClass="MMXU">
              <DO name="TotW" type="MV_T" />
            </LNodeType>
            <DOType id="MV_T" cdc="MV">
              <DA name="mag" bType="Struct" type="ANALOG_T" fc="MX" />
            </DOType>
            <DAType id="ANALOG_T">
              <BDA name="f" bType="FLOAT32" />
            </DAType>
          </DataTypeTemplates>
        </SCL>
        """;

        var preparation = SclAssistedConnectionPreparationBuilder.Build(
            xml,
            "IED1",
            "P1",
            "192.0.2.10",
            102);

        Assert.True(preparation.IsSuccess, string.Join(" | ", preparation.Errors));
        Assert.Null(preparation.AssociationPlan);

        var resolution = Assert.IsType<SclAssistedMmsAssociationResolution>(
            preparation.AssociationResolution);
        Assert.True(resolution.IsSuccess, string.Join(" | ", resolution.Errors));
        Assert.InRange(
            resolution.Candidates.Count,
            1,
            SclAssistedMmsAssociationCandidateResolver.MaximumCandidateCount);
        Assert.Contains(
            resolution.Fields,
            field =>
                field.Name == "OSI-AP-Title" &&
                field.State == SclAssociationFieldState.Unspecified);
        Assert.Contains(
            resolution.Fields,
            field =>
                field.Name == "OSI-AE-Qualifier" &&
                field.State == SclAssociationFieldState.Unspecified);

        Assert.Equal(new[] { "EXACT_APP_DOMAIN" }, preparation.DomainInventory.ExpectedDomains);
        Assert.Single(preparation.InitialReadDesign!.Model.DataSets);
        Assert.Single(preparation.InitialReadDesign.Model.ReportControls);
        Assert.NotNull(preparation.InitialReadPlan);

        // This fixture proves preparation/model convergence only. No network or full
        // discovery API is invoked from the pure preparation builder.
    }
}
