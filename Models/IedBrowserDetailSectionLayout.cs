namespace ArIED61850Tester.Models;

/// <summary>
/// UI-only expandable section layout for the existing virtualized Browser.
/// Reports, control blocks and GOOSE retain the exact projected row objects;
/// only each targeted section header owns a lazily enumerable subset.
/// Values and observations still come exclusively from the canonical ledger.
/// </summary>
public static class IedBrowserDetailSectionLayout
{
    private static readonly HashSet<string> ExpandableNames = new(
        ["Control Block attributes", "Information received in last Report",
         "Information received in last GOOSE"],
        StringComparer.Ordinal);

    public static IReadOnlyList<IedBrowserDetailRow> Attach(
        IReadOnlyList<IedBrowserDetailRow> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var result = new List<IedBrowserDetailRow>(raw.Count);
        for (var index = 0; index < raw.Count; index++)
        {
            var section = raw[index];
            if (section.Kind != "Section" || !ExpandableNames.Contains(section.Name))
            {
                result.Add(section);
                continue;
            }
            var end = index + 1;
            while (end < raw.Count)
            {
                if (raw[end].Kind == "Section")
                {
                    // TrgOps and OptFlds are children of the same RCB, not
                    // independent root sections. Their FlagGroup rows remain
                    // the existing engine-backed read-only projections.
                    if (section.Name == "Control Block attributes" &&
                        raw[end].Name is "Trigger options" or "Optional fields")
                    {
                        end++;
                        continue;
                    }
                    break;
                }
                end++;
            }
            if (end == index + 1)
            {
                result.Add(section);
                continue;
            }

            // Depth+1 is solely a visual tree contract: the existing
            // projection's collapse removal checks descendant depth.
            var children = raw.Skip(index + 1).Take(end - index - 1)
                .Select(child => child with { Depth = child.Depth + 1 })
                .ToArray();
            result.Add(section with
            {
                LoadChildren = () => children,
                // Status summaries are rarely needed during routine browsing.
                // Respect any explicit user toggle when the page is revisited.
                ExpandByDefault = section.Name is not
                    ("Information received in last Report" or "Information received in last GOOSE")
            });
            index = end - 1;
        }
        return result;
    }
}
