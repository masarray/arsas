using System.Xml;
using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester.Services;

/// <summary>
/// Extracts only exact source ReportControl@rptID attributes. Indexed families
/// cannot have a fabricated per-instance RptID; missing/ambiguous values stay unknown.
/// This is source-file inspection only; no MMS service or association is started.
/// </summary>
public static class RcbSclReportIdCatalog
{
    public static IReadOnlyDictionary<string, string> Read(
        string? sourcePath, IReadOnlyList<SclReportControlDescriptor> descriptors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return result;

        using var reader = XmlReader.Create(sourcePath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        });
        var document = XDocument.Load(reader);
        var elements = document.Descendants()
            .Where(element => element.Name.LocalName == "ReportControl")
            .ToArray();

        foreach (var descriptor in descriptors)
        {
            var exact = elements.Where(element =>
            {
                var ln = element.Parent;
                if (ln?.Name.LocalName is not ("LN" or "LN0")) return false;
                var ld = ln.Ancestors().FirstOrDefault(node => node.Name.LocalName == "LDevice");
                var ap = ln.Ancestors().FirstOrDefault(node => node.Name.LocalName == "AccessPoint");
                var ied = ln.Ancestors().FirstOrDefault(node => node.Name.LocalName == "IED");
                var path = ln.Name.LocalName == "LN0"
                    ? "LLN0"
                    : $"{Attr(ln, "prefix")}{Attr(ln, "lnClass")}{Attr(ln, "inst")}";
                return Same(Attr(ied, "name"), descriptor.IedName) &&
                       Same(Attr(ap, "name"), descriptor.AccessPointName) &&
                       Same(Attr(ld, "inst"), descriptor.LogicalDeviceInstance) &&
                       Same(path, descriptor.LogicalNodePath) &&
                       Same(Attr(element, "name"), descriptor.Name);
            }).Take(2).ToArray();

            if (exact.Length != 1) continue;
            var reportId = Attr(exact[0], "rptID");
            if (!string.IsNullOrWhiteSpace(reportId))
                result[descriptor.SelectionKey] = reportId;
        }
        return result;
    }

    private static bool Same(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Attr(XElement? element, string name)
        => element?.Attribute(name)?.Value?.Trim() ?? string.Empty;
}
