using System.Windows;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Presentation-only node. Child factories materialize model objects on expansion, not when
/// navigating to the Browser. Stable keys use exact IEC references and never change protocol identity.
/// </summary>
public sealed class IedBrowserTreeRow : ObservableObject
{
    private readonly Func<IReadOnlyList<IedBrowserTreeRow>>? _loadChildren;
    private IReadOnlyList<IedBrowserTreeRow>? _children;

    public IedBrowserTreeRow(string key, string title, string kind, int depth, object? payload = null,
        Func<IReadOnlyList<IedBrowserTreeRow>>? loadChildren = null)
    {
        Key = key;
        Title = title;
        Kind = kind;
        Depth = depth;
        Payload = payload;
        _loadChildren = loadChildren;
    }

    public string Key { get; }
    public string Title { get; }
    public string Kind { get; }
    public int Depth { get; }
    public object? Payload { get; }
    public bool HasChildren => _loadChildren is not null;
    public Visibility ChevronVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public double IndentWidth => Depth * 14d;
    public string Glyph => Kind switch
    {
        "IED" => "IED", "Category" => "▦", "LD" => "LD", "LN" => "LN",
        "RCB" => "R", "DataSet" => "DS", "GoCB" => "G", "SGCB" => "SG",
        "SVCB" => "SV", "File" => "F", "DO" => "DO", "DA" => "DA",
        _ => "·"
    };

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        internal set { if (Set(ref _isExpanded, value)) Raise(nameof(ToggleGlyph)); }
    }
    public string ToggleGlyph => IsExpanded ? "▾" : "▸";
    internal IReadOnlyList<IedBrowserTreeRow> GetChildren()
        => _children ??= _loadChildren?.Invoke() ?? Array.Empty<IedBrowserTreeRow>();
}

/// <summary>
/// Only visible flattened rows enter the virtual DataGrid. Hidden grandchildren are
/// not allocated as WPF elements or materialized presentation rows.
/// </summary>
public sealed class IedBrowserTreeProjection
{
    public BulkObservableCollection<IedBrowserTreeRow> Rows { get; } = new();

    public void Reset(IReadOnlyList<IedBrowserTreeRow> roots, IReadOnlySet<string>? restoreExpanded = null)
    {
        Rows.ReplaceAll(roots);

        if (restoreExpanded is null) return;
        // Re-expand only paths visible in this revision of the immutable model.
        for (var i = 0; i < Rows.Count; i++)
            if (restoreExpanded.Contains(Rows[i].Key))
                Expand(Rows[i]);
    }

    public HashSet<string> ExpandedKeys()
        => Rows.Where(row => row.IsExpanded).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

    public IedBrowserTreeRow? FindVisible(string key)
        => Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal));

    public void Toggle(IedBrowserTreeRow row)
    {
        if (row.IsExpanded) Collapse(row);
        else Expand(row);
    }

    public void Expand(IedBrowserTreeRow row)
    {
        if (!row.HasChildren || row.IsExpanded) return;
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        row.IsExpanded = true;
        var updated = Rows.ToList();
        updated.InsertRange(index + 1, row.GetChildren());
        // One WPF collection reset rather than one routed update per expanded child.
        Rows.ReplaceAll(updated);
    }

    public void Collapse(IedBrowserTreeRow row)
    {
        if (!row.IsExpanded) return;
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        var descendant = index + 1;
        while (descendant < Rows.Count && Rows[descendant].Depth > row.Depth)
        {
            Rows[descendant].IsExpanded = false;
            descendant++;
        }
        var updated = Rows.ToList();
        updated.RemoveRange(index + 1, descendant - index - 1);
        row.IsExpanded = false;
        Rows.ReplaceAll(updated);
    }
}

public sealed record IedBrowserDetailRow(
    string Kind, string Name, string Value = "", int Depth = 0, string Source = "")
{
    public double IndentWidth => Depth * 16d;
    public string Tooltip => string.IsNullOrWhiteSpace(Source) ? Name : $"{Name} · {Source}";
}

/// <summary>
/// Exact model-to-UI projection. Never reads MMS or invents a report history.
/// The canonical dataset member order and multiplicity are preserved.
/// </summary>
public static class IedBrowserPresentation
{
    public static IReadOnlyList<IedBrowserTreeRow> Roots(
        string iedName, LiveIedModelDiscoveryDocument? model, IReadOnlyList<RcbExportRow> rcbs)
    {
        const int childDepth = 2;
        IedBrowserTreeRow Category(string name, string key,
            Func<IReadOnlyList<IedBrowserTreeRow>> children)
            => new("category/" + key, name, "Category", 1, loadChildren: children);

        var categories = new List<IedBrowserTreeRow>
        {
            Category("GOOSE", "goose", () => BlockGroups(model?.GooseControlBlocks, "GoCB")),
            Category("Reports", "reports", () => ReportGroups(rcbs)),
            Category("Setting Groups", "settings", () => BlockGroups(model?.SettingGroupControls, "SGCB")),
            Category("Files", "files", () =>
                model?.FileDirectory.Entries.Select(f =>
                    new IedBrowserTreeRow("file/" + f.Path, f.Name, "File", childDepth, f))
                    .ToArray() ?? Array.Empty<IedBrowserTreeRow>()),
            Category("DataSets", "datasets", () => DataSetGroups(model?.DataSets)),
            Category("Data Model", "model", () =>
                model?.LogicalDevices.Select(ld =>
                    new IedBrowserTreeRow("model/ld/" + ld.MmsDomain, ld.MmsDomain, "LD", 2, ld,
                        () => ld.LogicalNodes.Select(ln =>
                            new IedBrowserTreeRow("model/ln/" + ld.MmsDomain + "/" + ln.Name,
                                ln.Name, "LN", 3, ln,
                                () => ln.DataObjects.Select(obj =>
                                    new IedBrowserTreeRow("model/do/" + obj.Reference, obj.Name, "DO", 4, obj,
                                        () => obj.Attributes.Select((attr, ordinal) =>
                                            new IedBrowserTreeRow("model/da/" + attr.MmsReference + "/" + ordinal,
                                                attr.AttributePath, "DA", 5, attr)).ToArray()))
                                    .ToArray())).ToArray())).ToArray()
                    ?? Array.Empty<IedBrowserTreeRow>())
        };
        return [new IedBrowserTreeRow("ied", iedName, "IED", 0, model, () => categories)];
    }

    private static IReadOnlyList<IedBrowserTreeRow> ReportGroups(IReadOnlyList<RcbExportRow> rcbs)
        => rcbs.GroupBy(row => LogicalDevice(row.Reference), StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow("reports/ld/" + ld.Key, ld.Key, "LD", 2,
                loadChildren: () => ld.GroupBy(row => LogicalNode(row.Reference), StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow("reports/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 3,
                        loadChildren: () => ln.Select(row =>
                            new IedBrowserTreeRow("reports/rcb/" + row.SelectionIdentity,
                                row.Name, "RCB", 4, row)).ToArray())).ToArray())).ToArray();

    private static IReadOnlyList<IedBrowserTreeRow> DataSetGroups(IReadOnlyList<LiveIedDataSetModel>? sets)
        => sets?.GroupBy(set => set.Domain, StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow("datasets/ld/" + ld.Key, ld.Key, "LD", 2,
                loadChildren: () => ld.GroupBy(set => set.LogicalNode, StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow("datasets/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 3,
                        loadChildren: () => ln.Select(set =>
                            new IedBrowserTreeRow("datasets/set/" + set.Reference, set.Name,
                                "DataSet", 4, set)).ToArray())).ToArray())).ToArray()
            ?? Array.Empty<IedBrowserTreeRow>();

    private static IReadOnlyList<IedBrowserTreeRow> BlockGroups(
        IReadOnlyList<LiveIedControlBlockModel>? blocks, string kind)
        => blocks?.GroupBy(block => block.Domain, StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow(kind + "/ld/" + ld.Key, ld.Key, "LD", 2,
                loadChildren: () => ld.GroupBy(block => block.LogicalNode, StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow(kind + "/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 3,
                        loadChildren: () => ln.Select(block =>
                            new IedBrowserTreeRow(kind + "/" + block.Reference, block.Name,
                                kind, 4, block)).ToArray())).ToArray())).ToArray()
            ?? Array.Empty<IedBrowserTreeRow>();

    private static string LogicalDevice(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash < 1 ? "(unknown LD)" : reference[..slash];
    }

    private static string LogicalNode(string reference)
    {
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash >= reference.Length - 1) return "(unknown LN)";
        var suffix = reference[(slash + 1)..];
        var cut = suffix.IndexOfAny(['.', '$']);
        return cut < 0 ? suffix : suffix[..cut];
    }

    public static IReadOnlyList<IedBrowserDetailRow> Details(
        IedBrowserTreeRow node, LiveIedModelDiscoveryDocument? model, bool connected)
    {
        var rows = new List<IedBrowserDetailRow>();
        void Section(string name) => rows.Add(new("Section", name));
        void Field(string name, object? value, string source = "Model")
            => rows.Add(new("Property", name, value?.ToString() is { Length: > 0 } v ? v : "—", Source: source));
        void Member(string name, string value = "Not read", int depth = 0, string source = "Model")
            => rows.Add(new("Data", name, value, depth, source));

        switch (node.Payload)
        {
            case RcbExportRow rcb:
                Section("Control Block attributes");
                foreach (var prop in rcb.Inspection.Properties(rcb, connected))
                    Field(prop.Property, prop.Value, prop.Source);
                Section("Information received in last Report");
                rows.Add(new("Message", "No report-reception snapshot is available in this inspector."));
                Section("Data");
                rows.Add(new("DataHeader", "Name", "Value"));
                var linkedDataSet = FindDataSet(model, rcb.DataSetReference);
                if (linkedDataSet is null)
                    rows.Add(new("Message", "DataSet member directory is not available in the current model."));
                else
                    foreach (var member in linkedDataSet.Members)
                        Member($"{member.Index}: {member.Reference} [{member.FunctionalConstraint}]",
                            source: "Ordered DataSet member (not a live read)");
                break;
            case LiveIedDataSetModel set:
                Section("DataSet attributes");
                Field("Reference", set.Reference);
                Field("Member count", set.MemberCount);
                Field("Deletable", set.IsDeletable?.ToString() ?? "Unknown");
                Field("Used by reports", string.Join(", ", set.UsedByReportControls));
                Section("Data");
                rows.Add(new("DataHeader", "Name", "Value"));
                foreach (var member in set.Members)
                    Member($"{member.Index}: {member.Reference} [{member.FunctionalConstraint}]",
                        source: "Ordered DataSet member (not a live read)");
                break;
            case LiveIedControlBlockModel block:
                Section(node.Kind == "SGCB" ? "Setting Group attributes" : "Control Block attributes");
                Field("Reference", block.Reference);
                Field("Name", block.Name);
                Field("Functional constraint", block.FunctionalConstraint);
                Field("DataSet", block.DataSetReference);
                Field("Configuration revision", block.ConfRev);
                Field("Control ID", block.ControlId);
                Field("APP ID", block.AppId);
                Field("Discovery status", block.DiscoveryStatus);
                Section("Attribute inventory");
                rows.Add(new("DataHeader", "Attribute", "Value"));
                foreach (var attribute in block.Attributes)
                    Member(attribute, source: "Inventory only");
                if (node.Kind == "SGCB")
                    rows.Add(new("Message", "Setting-group values require an explicit supported engine read."));
                break;
            case LiveIedLogicalDeviceModel ld:
                Section("Logical Device");
                Field("MMS domain", ld.MmsDomain);
                Field("LDevice instance", ld.Inst);
                Field("Logical Nodes", ld.LogicalNodes.Count);
                Section("Logical Nodes");
                rows.Add(new("DataHeader", "Name", "Class"));
                foreach (var ln in ld.LogicalNodes)
                    Member(ln.Name, ln.LnClass, source: "Model");
                break;
            case LiveIedLogicalNodeModel ln:
                Section("Logical Node");
                Field("Name", ln.Name);
                Field("Class", ln.LnClass);
                Field("LN type", ln.ProposedLnTypeId);
                Section("Data Objects");
                rows.Add(new("DataHeader", "Name", "CDC"));
                foreach (var obj in ln.DataObjects)
                    Member(obj.Name, obj.InferredCdc);
                break;
            case LiveIedDataObjectModel obj:
                Section("Data Object");
                Field("Reference", obj.Reference);
                Field("CDC", obj.InferredCdc);
                Field("Type confidence", obj.ConfidenceLevel);
                Section("Data Attributes");
                rows.Add(new("DataHeader", "Name", "Type"));
                foreach (var attr in obj.Attributes)
                    Member($"{attr.AttributePath} [{attr.FunctionalConstraint}]",
                        string.IsNullOrWhiteSpace(attr.SclBType) ? attr.MmsType : attr.SclBType,
                        source: attr.TypeSource);
                break;
            case LiveIedDataAttributeModel attr:
                Section("Data Attribute");
                Field("IEC reference", attr.ObjectReference);
                Field("DA path", attr.AttributePath);
                Field("FC", attr.FunctionalConstraint);
                Field("MMS reference", attr.MmsReference);
                Field("MMS type", attr.MmsType);
                Field("SCL bType", attr.SclBType);
                Field("Type confidence", attr.TypeConfidence);
                Field("Value", "Not read", "No explicit MMS read");
                break;
            case LiveIedFileModel file:
                Section("File attributes");
                Field("Name", file.Name);
                Field("Path", file.Path);
                Field("Size (bytes)", file.SizeBytes?.ToString() ?? "Unknown");
                Field("Last modified", file.LastModified);
                Field("Source", file.Source);
                break;
            default:
                Section(node.Title);
                Field("Type", node.Kind);
                Field("Model", model is null ? "Unavailable" : "Cached model");
                Field("Read state", "No MMS read on navigation", "Browser policy");
                break;
        }
        return rows;
    }

    private static LiveIedDataSetModel? FindDataSet(LiveIedModelDiscoveryDocument? model, string reference)
    {
        if (model is null || string.IsNullOrWhiteSpace(reference)) return null;
        var exact = model.DataSets.FirstOrDefault(set => string.Equals(set.Reference, reference, StringComparison.Ordinal));
        if (exact is not null) return exact;
        // Use a case-insensitive fallback only if the match is unambiguous.
        var candidates = model.DataSets.Where(set =>
            string.Equals(set.Reference, reference, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
