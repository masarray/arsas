using System.Text;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeCfgDisplayTextTests
{
    [Fact]
    public void DecodeCfg_PreservesValidUtf8Accents()
    {
        var bytes = Encoding.UTF8.GetBytes("1,Déclenchement,,,0");

        Assert.Contains("Déclenchement", ComtradeCfgDisplayText.DecodeCfg(bytes));
    }

    [Fact]
    public void DecodeCfg_FallsBackToLatin1ForLegacyRelayText()
    {
        var bytes = Encoding.Latin1.GetBytes("1,Déclenchement Überstrom,,,0");

        Assert.Contains("Déclenchement Überstrom", ComtradeCfgDisplayText.DecodeCfg(bytes));
    }

    [Fact]
    public void StatusChannelIds_AreRecoveredFromOriginalLegacyCfgBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsas-comtrade-{Guid.NewGuid():N}.cfg");
        try
        {
            var cfg = string.Join("\r\n", new[]
            {
                "Station,Relay,1999",
                "2,1A,1D",
                "1,IA,A,Feeder,A,1,0,0,-32768,32767,1,1,P",
                "1,Déclenchement Überstrom,,,0"
            });
            File.WriteAllBytes(path, Encoding.Latin1.GetBytes(cfg));

            var names = ComtradeCfgDisplayText.TryReadStatusChannelIds(path, 1, 1);

            Assert.Equal("Déclenchement Überstrom", names[0]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CsvParser_PreservesQuotedCommasAndEscapedQuotes()
    {
        var fields = ComtradeCfgDisplayText.ParseCsvLine("1,\"Trip, stage \"\"2\"\"\",,,0");

        Assert.Equal("Trip, stage \"2\"", fields[1]);
    }
}
