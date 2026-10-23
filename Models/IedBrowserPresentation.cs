using System.Windows;
using System.ComponentModel;
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

    public IedBrowserTreeRow? Parent { get; private set; }
    public string Key { get; }
    public string Title { get; }
    public string Kind { get; }
    public int Depth { get; }
    public object? Payload { get; }
    // The left Data Model tree intentionally stops at LN. The lazy DO/DA
    // factories remain available to the right-hand object browser only.
    public bool HasChildren => _loadChildren is not null &&
        !Key.StartsWith("model/ln/", StringComparison.Ordinal);
    public Visibility ChevronVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public double IndentWidth => Depth * 14d;
    public string Glyph => Kind switch
    {
        "IED" => "IED", "Category" => "▦", "LD" => "LD", "LN" => "LN",
        "RCB" => "R", "DataSet" => "DS", "GoCB" => "G", "SGCB" => "SG",
        "SVCB" => "SV", "File" => "F", "DO" => "DO", "DA" => "DA",
        _ => "·"
    };

    public System.Windows.Media.Brush GlyphBrush => Payload is RcbExportRow report
        ? report.Buffered ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.RoyalBlue
        : System.Windows.Media.Brushes.SteelBlue;
    // UI-only display state. This flag is re-derived from the current-IED
    // LiveValues ledger; it is never used as an MMS/process state store.
    private bool _rcbEnabledVerified;
    private string _rcbEnabledTooltip = "";
    public Visibility RcbEnabledVisibility => Payload is RcbExportRow && _rcbEnabledVerified
        ? Visibility.Visible : Visibility.Collapsed;
    public string RcbEnabledTooltip => _rcbEnabledTooltip;

    public bool SetRcbEnabled(bool verified, string tooltip)
    {
        if (Payload is not RcbExportRow) return false;
        tooltip = verified ? tooltip : "";
        if (_rcbEnabledVerified == verified &&
            string.Equals(_rcbEnabledTooltip, tooltip, StringComparison.Ordinal))
            return false;
        _rcbEnabledVerified = verified;
        _rcbEnabledTooltip = tooltip;
        Raise(nameof(RcbEnabledVisibility));
        Raise(nameof(RcbEnabledTooltip));
        return true;
    }
    public string RcbUsageTooltip => Payload is RcbExportRow report
        ? report.Availability switch
        {
            AR.Iec61850.Mms.MmsRcbOperationalAvailability.UsedByCaller =>
                "Active in the current ARSAS monitoring session",
            AR.Iec61850.Mms.MmsRcbOperationalAvailability.InUse =>
                "RCB in use by another client (read-only RCB status)",
            AR.Iec61850.Mms.MmsRcbOperationalAvailability.Unknown =>
                "RCB occupancy unknown; use Check live RCB for a read-only availability snapshot. " +
                report.Reason,
            _ => report.Reason
        } : "";

    /// <summary>RCB status changes after the lazy tree is built. Notify its
    /// bound glyph without resetting navigation, selection or scroll state.</summary>
    public void RefreshRcbUsage()
    {
        if (Payload is not RcbExportRow) return;
        Raise(nameof(RcbUsageTooltip));
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        internal set { if (Set(ref _isExpanded, value)) Raise(nameof(ToggleGlyph)); }
    }
    public string ToggleGlyph => IsExpanded ? "▾" : "▸";
    internal IReadOnlyList<IedBrowserTreeRow> GetChildren()
    {
        if (_children is not null) return _children;
        var children = _loadChildren?.Invoke() ?? Array.Empty<IedBrowserTreeRow>();
        foreach (var child in children)
            child.Parent = this;
        return _children = children;
    }
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

    // A detail-grid object points to the identical cached node owned by the tree.
    // Reveal ancestors on demand; unrelated LD/LN branches stay lazy.
    public void Reveal(IedBrowserTreeRow row)
    {
        var ancestors = new Stack<IedBrowserTreeRow>();
        for (var parent = row.Parent; parent != null; parent = parent.Parent)
            ancestors.Push(parent);
        while (ancestors.Count > 0)
            Expand(ancestors.Pop());
    }

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

public sealed record IedBrowserFlagOption(string Label, bool? IsEnabled)
{
    public string Status => IsEnabled is null ? "Unknown" : IsEnabled.Value ? "Enabled" : "Disabled";
}

public sealed record IedBrowserDetailRow(
    string Kind, string Name, string Value = "", int Depth = 0, string Source = "")
    : INotifyPropertyChanged
{
    private string _displayValue = Value;
    public string Value
    {
        get => _displayValue;
        set
        {
            if (_displayValue == value) return;
            _displayValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInvalidQuality)));
        }
    }
    /// <summary>Presentation only: never alters the evidence or an IEC quality bit.</summary>
    public bool IsInvalidQuality =>
        QualityInvalidAttention.IsInvalid(Value) &&
        (EvidenceKind == "Quality" &&
            (EvidenceField.Length == 0 || EvidenceField == "Validity") ||
         EvidenceField.Length == 0 &&
            (FunctionalConstraint is "ST" or "MX" or "SV") &&
            (ValueReference.EndsWith(".q", StringComparison.Ordinal) ||
             ValueReference.EndsWith("$q", StringComparison.Ordinal)));
    private string _observationSource = "";
    private string _observationTooltip = "";
    /// <summary>Explicit observation channel, never model metadata or a source guess.</summary>
    public string ObservationSource
    {
        get => _observationSource;
        set
        {
            if (_observationSource == value) return;
            _observationSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ObservationSource)));
        }
    }
    public string ObservationTooltip
    {
        get => _observationTooltip.Length != 0 ? _observationTooltip : Tooltip;
        set
        {
            if (_observationTooltip == value) return;
            _observationTooltip = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ObservationTooltip)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ValueReference { get; init; } = "";
    // The exact alternate reference is evaluated only when the primary has
    // no observation in the current association. Never borrow a sibling.
    public string FallbackValueReference { get; init; } = "";
    // Units apply to engineering aggregates only, never to primitive f/q/t.
    public bool IsEngineeringValue { get; init; }
    public string EngineeringUnitReference { get; init; } = "";
    // Exact M2.14 timestamp-compatible polar plan for CMV and PPV vectors.
    public IedBrowserPolarPlan? PolarPlan { get; init; }
    public IReadOnlyList<IedBrowserPolarPlan> PhasePlans { get; init; } =
        Array.Empty<IedBrowserPolarPlan>();
    // Model-declared vector leaves only. Immutable row descriptor, not an
    // alternative value store; each component is resolved from canonical live
    // observations on demand. Empty for scalar and q/t rows.
    public IReadOnlyList<IedBrowserVectorComponent> VectorComponents { get; init; } =
        Array.Empty<IedBrowserVectorComponent>();
    // A small, immutable set of exact ST process leaves, e.g. TapChg
    // valWTr + posVal. Values are resolved from SSOT on refresh, never cached.
    // These fields are independent, not a synthetic IEC scalar or phasor.
    public IReadOnlyList<IedBrowserScalarComponent> ScalarComponents { get; init; } =
        Array.Empty<IedBrowserScalarComponent>();
    // Model-proven, immutable metadata ONLY: priority-ordered exact FC=DC
    // leaves of this Nameplate object. No values are retained outside SSOT.
    public IReadOnlyList<string> NameplateDcReferences { get; init; } =
        Array.Empty<string>();
    // A typed child of a received q/t field; never treat the entire parent
    // value text as this individual Boolean/time-quality bit.
    public string EvidenceField { get; init; } = "";
    // Schema-qualified presentation type; never infer a q/t bit from a DO name.
    public string EvidenceKind { get; init; } = "";
    public string FunctionalConstraint { get; init; } = "";
    public bool IsDataObject { get; init; }
    // Exact whole IEC DO for an operator's explicit Read choice. Unlike
    // ValueReference, never points to an arbitrary preferred scalar leaf.
    public string ReadObjectReference { get; init; } = "";
    // Immutable, strictly scoped child references from THIS exact IEC DO.
    // Only for a non-scalar parent: a bounded read-only presence indicator,
    // not a fabricated parent value and not a second observation registry.
    public IReadOnlyList<IedBrowserReadTarget> ParentObservedChildCandidates { get; init; } =
        Array.Empty<IedBrowserReadTarget>();
    // IEC DataSet position, not a live value; only a validated frame can populate it.
    public int GooseSlot { get; init; } = -1;
    public string GooseReference { get; init; } = "";
    public string GooseFc { get; init; } = "";
    private IReadOnlyList<IedBrowserDetailRow>? _children;
    public IedBrowserTreeRow? NavigateTo { get; init; }
    public Func<IReadOnlyList<IedBrowserDetailRow>>? LoadChildren { get; init; }
    public IReadOnlyList<IedBrowserFlagOption> Flags { get; init; } = Array.Empty<IedBrowserFlagOption>();
    public string SecondaryText { get; init; } = "";
    public string Glyph => NavigateTo?.Glyph ?? (IsDataObject ? "DO" : Kind == "Data" ? "DA" : "");
    public System.Windows.Media.Brush GlyphBrush => NavigateTo?.GlyphBrush ??
        System.Windows.Media.Brushes.SteelBlue;
    public Visibility RcbEnabledVisibility => NavigateTo?.RcbEnabledVisibility ?? Visibility.Collapsed;
    public string RcbEnabledTooltip => NavigateTo?.RcbEnabledTooltip ?? "";
    public string RcbUsageTooltip => NavigateTo?.RcbUsageTooltip ?? "";

    /// <summary>Mirror the same RCB node's active state in the right list.</summary>
    public void RefreshRcbUsage()
    {
        if (NavigateTo?.Payload is not RcbExportRow rcb) return;
        PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(RcbEnabledVisibility)));
        PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(RcbEnabledTooltip)));
        PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(RcbUsageTooltip)));
        if (Kind == "Object") Value = rcb.StatusText;
    }

    public bool ExpandByDefault { get; init; }
    public bool HasChildren => LoadChildren is not null;
    public Visibility ChevronVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public bool IsExpanded { get; internal set; }
    public string ToggleGlyph => IsExpanded ? "▾" : "▸";
    public double IndentWidth => Depth * 15d;
    public string Tooltip => string.IsNullOrWhiteSpace(Source) ? Name : $"{Name} · {Source}";
    internal IReadOnlyList<IedBrowserDetailRow> GetChildren()
        => _children ??= LoadChildren?.Invoke() ?? Array.Empty<IedBrowserDetailRow>();
}

/// <summary>Virtualized flat detail projection; a collapsed model attribute never creates WPF rows.</summary>
public sealed class IedBrowserDetailProjection
{
    public BulkObservableCollection<IedBrowserDetailRow> Rows { get; } = new();

    public void Reset(IReadOnlyList<IedBrowserDetailRow> rows)
    {
        // Attach UI-only collapsibility to exact section boundaries. Do not
        // mutate source projection rows or create a second evidence store.
        var sections = IedBrowserDetailSectionLayout.Attach(rows);
        var display = new List<IedBrowserDetailRow>(sections.Count);
        foreach (var row in sections)
        {
            display.Add(row);
            if (!row.ExpandByDefault || !row.HasChildren) continue;
            row.IsExpanded = true;
            display.AddRange(row.GetChildren());
        }
        Rows.ReplaceAll(display);
    }

    public void Toggle(IedBrowserDetailRow row)
    {
        if (!row.HasChildren) return;
        var position = Rows.IndexOf(row);
        if (position < 0) return;
        var updated = Rows.ToList();
        if (row.IsExpanded)
        {
            var end = position + 1;
            while (end < updated.Count && updated[end].Depth > row.Depth)
            {
                updated[end].IsExpanded = false;
                end++;
            }
            updated.RemoveRange(position + 1, end - position - 1);
            row.IsExpanded = false;
        }
        else
        {
            row.IsExpanded = true;
            updated.InsertRange(position + 1, row.GetChildren());
        }
        Rows.ReplaceAll(updated);
    }
}

/// <summary>
/// Exact model-to-UI projection. Never reads MMS or invents a report history.
/// The canonical dataset member order and multiplicity are preserved.
/// </summary>
public static class IedBrowserPresentation
{
    public static IReadOnlyList<IedBrowserTreeRow> Roots(
        LiveIedModelDiscoveryDocument? model, IReadOnlyList<RcbExportRow> rcbs,
        IReadOnlyList<IedBrowserRemoteFile>? remoteFiles = null,
        string? iedName = null,
        LiveIedModelDiscoveryDocument? sclModel = null)
    {
        const int childDepth = 1;
        // Keep domain/object keys untouched; only presentation labels are short.
        var labelIed = !string.IsNullOrWhiteSpace(model?.IedName)
            ? model.IedName : iedName;
        IedBrowserTreeRow Category(string name, string key,
            Func<IReadOnlyList<IedBrowserTreeRow>> children)
            => new("category/" + key, name, "Category", 0, loadChildren: children);

        // ARSAS navigation order is a presentation choice, not a wire/model contract.
        // Stable category keys and lazy children preserve existing selected paths and
        // avoid any additional MMS discovery, control or report requests.
        var categories = new List<IedBrowserTreeRow>
        {
            Category("Reports", "reports", () => ReportGroups(rcbs, labelIed)),
            Category("DataSets", "datasets", () => DataSetGroups(model?.DataSets, labelIed)),
            Category("Data Model", "model", () =>
                model?.LogicalDevices.Select(ld =>
                    new IedBrowserTreeRow("model/ld/" + ld.MmsDomain,
                        LdDisplayName(ld.MmsDomain, labelIed), "LD", 1, ld,
                        () => ld.LogicalNodes.Select(ln =>
                            new IedBrowserTreeRow("model/ln/" + ld.MmsDomain + "/" + ln.Name,
                                ln.Name, "LN", 2, ln,
                                () => IedBrowserEngineeringObjects.GroupExact(ln.DataObjects)
                                    .Select(obj =>
                                    new IedBrowserTreeRow("model/do/" + obj.Reference, obj.Name, "DO", 3, obj,
                                        () => obj.Attributes.Select((attr, ordinal) =>
                                            new IedBrowserTreeRow("model/da/" + attr.MmsReference + "/" + ordinal,
                                                attr.AttributePath, "DA", 4, attr)).ToArray()))
                                    .ToArray())).ToArray())).ToArray()
                    ?? Array.Empty<IedBrowserTreeRow>()),
            // Files inventory belongs exclusively in the right-side
            // virtualized grid. Nested file nodes duplicate content and
            // inflate the compact engineering navigation.
            new("category/files", "Files", "Category", 0),
            Category("GOOSE", "goose", () => BlockGroups(
                IedBrowserGooseDirectory.Resolve(model, sclModel), "GoCB", labelIed)),
            Category("Setting Groups", "settings", () => BlockGroups(model?.SettingGroupControls, "SGCB", labelIed))
        };
        return categories; // ARSAS IED Explorer already owns device selection; never duplicate it.
    }

    public static string LdDisplayName(string domain, string? iedName)
    {
        var name = (domain ?? "").Trim();
        var owner = (iedName ?? "").Trim();
        // The MMS domain is the protocol identity. Strip only an exact
        // engineering IEDName prefix and never show an empty LD label.
        if (owner.Length == 0 || name.Length <= owner.Length ||
            !name.StartsWith(owner, StringComparison.Ordinal))
            return name;
        return name[owner.Length..];
    }

    private static IReadOnlyList<IedBrowserTreeRow> ReportGroups(
        IReadOnlyList<RcbExportRow> rcbs, string? iedName)
        => rcbs.GroupBy(row => LogicalDevice(row.Reference), StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow("reports/ld/" + ld.Key,
                LdDisplayName(ld.Key, iedName), "LD", 1,
                loadChildren: () => ld.GroupBy(row => LogicalNode(row.Reference), StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow("reports/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 2,
                        loadChildren: () => ln.OrderBy(row => row.Buffered).Select(row =>
                            new IedBrowserTreeRow("reports/rcb/" + row.SelectionIdentity,
                                row.Name, "RCB", 3, row)).ToArray())).ToArray())).ToArray();

    private static IReadOnlyList<IedBrowserTreeRow> DataSetGroups(
        IReadOnlyList<LiveIedDataSetModel>? sets, string? iedName)
        => sets?.GroupBy(set => set.Domain, StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow("datasets/ld/" + ld.Key,
                LdDisplayName(ld.Key, iedName), "LD", 1,
                loadChildren: () => ld.GroupBy(set => set.LogicalNode, StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow("datasets/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 2,
                        loadChildren: () => ln.Select(set =>
                            new IedBrowserTreeRow("datasets/set/" + set.Reference,
                                QualifiedName(set.LogicalNode, set.Name), "DataSet", 3, set)).ToArray())).ToArray())).ToArray()
            ?? Array.Empty<IedBrowserTreeRow>();

    private static IReadOnlyList<IedBrowserTreeRow> BlockGroups(
        IReadOnlyList<LiveIedControlBlockModel>? blocks, string kind,
        string? iedName)
        => blocks?.GroupBy(block => block.Domain, StringComparer.Ordinal)
            .Select(ld => new IedBrowserTreeRow(kind + "/ld/" + ld.Key,
                LdDisplayName(ld.Key, iedName), "LD", 1,
                loadChildren: () => ld.GroupBy(block => block.LogicalNode, StringComparer.Ordinal)
                    .Select(ln => new IedBrowserTreeRow(kind + "/ln/" + ld.Key + "/" + ln.Key,
                        ln.Key, "LN", 2,
                        loadChildren: () => ln.Select(block =>
                            new IedBrowserTreeRow(kind + "/" + block.Reference,
                                QualifiedName(block.LogicalNode, block.Name), kind, 3, block)).ToArray())).ToArray())).ToArray()
            ?? Array.Empty<IedBrowserTreeRow>();

    // Visual labels may qualify the name with LN context; IEC/MMS references remain exact.
    private static string QualifiedName(string logicalNode, string name)
    {
        if (string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(name) ||
            name.StartsWith(logicalNode + ".", StringComparison.Ordinal))
            return name;
        return logicalNode + "." + name;
    }

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

    // Right pane is an object browser for containers, not a generic metadata form.
    // Flatten only LD/LN wrappers for service categories (Reports, GOOSE, etc.).
    // Data Model retains LD -> LN -> DO navigation, so a 100k-DO model is not
    // eagerly represented when its category or LD is selected.
    private static bool TryBuildObjectList(
        IedBrowserTreeRow node, List<IedBrowserDetailRow> rows)
    {
        var serviceCategory = (node.Kind is "LD" or "LN") &&
            (node.Key.StartsWith("reports/", StringComparison.Ordinal) ||
             node.Key.StartsWith("GoCB/", StringComparison.Ordinal) ||
             node.Key.StartsWith("SGCB/", StringComparison.Ordinal) ||
             node.Key.StartsWith("datasets/", StringComparison.Ordinal));
        var serviceRoot = node.Key is "category/reports" or "category/goose" or
            "category/settings" or "category/datasets";
        // LN and DO must use the SAME real hierarchical data projection as
        // DataSets, not this generic navigation-card list. Those pages contain
        // actual expandable DO/DA/BDA with live Report/Read evidence.
        var dataModelContainer = node.Key == "category/model" ||
            node.Key.StartsWith("model/ld/", StringComparison.Ordinal);
        var fileRoot = node.Key == "category/files";
        if (!serviceRoot && !serviceCategory && !dataModelContainer && !fileRoot)
            return false;

        var isCategory = node.Kind == "Category";
        if (isCategory)
            rows.Add(new("Section", node.Title));
        else
            rows.Add(new IedBrowserDetailRow("Object", node.Title, Source: node.Key)
            {
                NavigateTo = node
            });

        var objects = serviceRoot || serviceCategory
            ? ServiceObjects(node).ToArray()
            : node.GetChildren();

        if (objects.Count == 0)
        {
            rows.Add(new("Message", node.Key == "category/settings"
                ? "No SGCB discovered in the current IED/SCL model. Setting Groups are unavailable until a real SGCB is verified; no groups or values are inferred."
                : "No objects available in the current SCL/discovery model."));
            return true;
        }
        foreach (var child in objects)
        {
            var descriptor = child.Payload switch
            {
                RcbExportRow rcb when rcb.Availability !=
                    AR.Iec61850.Mms.MmsRcbOperationalAvailability.Unknown => rcb.StatusText,
                LiveIedLogicalNodeModel ln => ln.LnClass,
                LiveIedDataSetModel ds => $"{ds.MemberCount} members",
                LiveIedFileModel file => file.SizeBytes is { } bytes ? FormatFileSize(bytes) : "",
                IedBrowserRemoteFile file => file.SizeText,
                _ => ""
            };
            // Keep exact reference as tooltip/evidence; list glyph and short
            // name follow the navigation tree, never use RptID as RCB identity.
            rows.Add(new IedBrowserDetailRow("Object", child.Title, descriptor,
                Source: child.Key)
            {
                NavigateTo = child,
                SecondaryText = child.Payload switch
                {
                    LiveIedFileModel modelFile => modelFile.LastModified,
                    IedBrowserRemoteFile remote => remote.ModifiedText,
                    _ => ""
                },
                ValueReference = child.Payload is LiveIedDataObjectModel doModel
                    ? doModel.Reference : "",
                FunctionalConstraint = child.Payload is LiveIedDataObjectModel sourceDo
                    ? PrimaryValueFunctionalConstraint(sourceDo) : ""
            });
        }
        return true;
    }

    private static string PrimaryValueFunctionalConstraint(LiveIedDataObjectModel model)
    {
        // A DO can contain ST, MX, CF and CO together. Use only its proven
        // process-value leaf; never borrow a setting or control FC implicitly.
        var primary = model.Attributes.FirstOrDefault(attr =>
            attr.FunctionalConstraint is "ST" or "MX" &&
            (attr.AttributePath.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) ||
             attr.AttributePath.EndsWith(".posVal", StringComparison.OrdinalIgnoreCase) ||
             attr.AttributePath.EndsWith(".valWTr", StringComparison.OrdinalIgnoreCase) ||
             attr.AttributePath.EndsWith(".general", StringComparison.OrdinalIgnoreCase) ||
             attr.AttributePath.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase) ||
             attr.AttributePath.EndsWith(".instMag.f", StringComparison.OrdinalIgnoreCase)));
        return primary?.FunctionalConstraint ?? "";
    }

    private static string FormatFileSize(uint size)
        => size >= 1_048_576 ? $"{size / 1_048_576d:0.##} MB"
            : size >= 1_024 ? $"{size / 1_024d:0.##} KB"
            : $"{size} B";

    private static IEnumerable<IedBrowserTreeRow> ServiceObjects(IedBrowserTreeRow parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.Kind is "LD" or "LN")
            {
                foreach (var leaf in ServiceObjects(child))
                    yield return leaf;
            }
            else
                yield return child;
        }
    }

    public static IReadOnlyList<IedBrowserDetailRow> Details(
        IedBrowserTreeRow node, LiveIedModelDiscoveryDocument? model, bool connected,
        LiveIedModelDiscoveryDocument? sclModel = null)
    {
        var rows = new List<IedBrowserDetailRow>();
        void Section(string name) => rows.Add(new("Section", name));
        void Field(string name, object? value, string source = "Model")
            => rows.Add(new("Property", name, value?.ToString() is { Length: > 0 } v ? v : "—", Source: source));
        void Member(string name, string value = "Not read", int depth = 0, string source = "Model")
            => rows.Add(new("Data", name, value, depth, source));

        if (TryBuildObjectList(node, rows))
            return rows;

        switch (node.Payload)
        {
            case RcbExportRow rcb:
                Section("Control Block attributes");
                foreach (var prop in rcb.Inspection.Properties(rcb, connected))
                {
                    if (prop.Property is "Trigger options" or "Optional fields")
                        continue; // Never show opaque BER hex as the operator-facing value.
                    Field(prop.Property, prop.Value, prop.Source);
                }
                var observed = connected ? rcb.Inspection.ReadOnlyCheck : null;
                var trgRaw = !string.IsNullOrWhiteSpace(observed?.TriggerOptions)
                    ? observed.TriggerOptions : rcb.Inspection.Model.TriggerOptions;
                var optRaw = !string.IsNullOrWhiteSpace(observed?.OptionalFields)
                    ? observed.OptionalFields : rcb.Inspection.Model.OptionalFields;
                Section("Trigger options");
                rows.Add(new IedBrowserDetailRow("FlagGroup", "Trigger options",
                    Source: $"Raw TrgOps: {trgRaw}; {rcb.Inspection.ModelSource}")
                {
                    Flags = IedBrowserReportFlags.TriggerOptions(trgRaw)
                });
                Section("Optional fields");
                rows.Add(new IedBrowserDetailRow("FlagGroup", "Optional fields",
                    Source: $"Raw OptFlds: {optRaw}; {rcb.Inspection.ModelSource}")
                {
                    Flags = IedBrowserReportFlags.OptionalFields(optRaw)
                });
                Section("Information received in last Report");
                Field("Last Report status", "No matching received Report");
                Field("Last Report received at", "—");
                Field("Received frame count", "—");
                Field("Last observed SqNum", "—");
                Field("Received RptID", "—");
                Field("Received DataSet", "—");
                Field("Inclusion reason", "Not exposed in continuity summary");
                Section("Data");
                rows.Add(new("DataHeader", "Name", "Value"));
                var linkedDataSet = FindDataSet(model, rcb.DataSetReference);
                if (linkedDataSet is null)
                    rows.Add(new("Message", "DataSet member directory is not available in the current model."));
                else
                    foreach (var member in linkedDataSet.Members)
                        rows.Add(IedBrowserStructuredDetails.Member(
                            member, model, sclModel: sclModel));
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
                    rows.Add(IedBrowserStructuredDetails.Member(
                        member, model, sclModel: sclModel));
                break;
            case LiveIedControlBlockModel block:
                Section("Control Block attributes");
                Field("Control Block reference", block.Reference);
                if (node.Kind == "SGCB")
                {
                    Field("Number of Setting Groups", "Not read");
                    Field("Active Setting Group", "Not read");
                    Field("Last changed", "Unknown");
                    Field("MMS state inventory",
                        IedBrowserControlStateEvidence.ReadTargets(block, "SGCB").Count > 0
                            ? "Verified read-only attributes" : "Not verified / SCL-only");
                    Field("Affected Logical Devices", block.Domain);
                    Section("Settings");
                    rows.Add(new("DataHeader", "Name", "Value"));
                    if (model is not null || sclModel is not null)
                    {
                        // Live inventory is the acquisition authority. A live
                        // model may omit configuration settings present in SCL:
                        // retain those as *labelled declarations*, not invented
                        // active values or authorization to send MMS Read.
                        static (LiveIedLogicalNodeModel Ln, LiveIedDataObjectModel Object)[]
                            InDomain(LiveIedModelDiscoveryDocument? source, string domain)
                            => source is null ? []
                                : source.LogicalDevices
                                    .Where(ld => string.Equals(ld.MmsDomain, domain,
                                        StringComparison.Ordinal))
                                    .SelectMany(ld => ld.LogicalNodes)
                                    .SelectMany(ln => ln.DataObjects.Select(obj =>
                                        (Ln: ln, Object: obj)))
                                    .ToArray();

                        var live = InDomain(model, block.Domain);
                        var scl = InDomain(sclModel, block.Domain);
                        var liveSg = live.Any(s => s.Object.Attributes.Any(a =>
                            string.Equals(a.FunctionalConstraint, "SG",
                                StringComparison.OrdinalIgnoreCase)));
                        var sclSg = scl.Any(s => s.Object.Attributes.Any(a =>
                            string.Equals(a.FunctionalConstraint, "SG",
                                StringComparison.OrdinalIgnoreCase)));
                        var directory = liveSg ? live : scl;
                        var fromScl = !liveSg && scl.Length > 0;
                        // SE exposes the editing declaration, NOT current SG;
                        // only show this separately labelled if SG is absent.
                        var fc = liveSg || sclSg ? "SG" : "SE";
                        if (fromScl)
                            rows.Add(new("Message",
                                fc == "SG"
                                    ? "SCL settings directory only · live MMS SG inventory not verified; Read does not authorize speculative fields."
                                    : "SCL edit-group [SE] declarations only · not current active [SG] values; no group selection or write."));
                        var shown = 0;
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var setting in directory)
                        {
                            var attrs = setting.Object.Attributes.Where(a =>
                                string.Equals(a.FunctionalConstraint, fc,
                                    StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (attrs.Length == 0) continue;
                            foreach (var field in IedBrowserStructuredDetails.AttributeRows(
                                         attrs, setting.Object.Name))
                            {
                                var refToRead = field.FallbackValueReference.Length > 0
                                    ? field.FallbackValueReference : field.ValueReference;
                                var identity = setting.Object.Reference + "|" +
                                    field.Name + "|" + fc;
                                if (!seen.Add(identity)) continue;
                                rows.Add(field with
                                {
                                    Name = $"{setting.Ln.Name}.{setting.Object.Name}.{field.Name}",
                                    ValueReference = refToRead,
                                    FallbackValueReference = refToRead == field.ValueReference
                                        ? "" : field.ValueReference,
                                    Source = field.Source + (fromScl
                                        ? " · SCL declaration; value requires real evidence"
                                        : " · verified live SG directory")
                                });
                                shown++;
                            }
                        }
                        if (shown == 0)
                            rows.Add(new("Message",
                                "No model-proven SG or SCL SE settings for this exact MMS domain; verify SGCB binding and SCL matching."));
                    }
                    else
                        rows.Add(new("Message", "No model is available for SG/SE setting attributes."));
                }
                else
                {
                    Field("Enabled", "Not read");
                    Field("GOOSE ID", block.ControlId);
                    Field("DataSet reference", block.DataSetReference);
                    Field("Application ID", block.AppId);
                    Field("Configuration revision", block.ConfRev);
                    Field("Minimum time (ms)", block.MinimumTimeMs);
                    Field("Maximum time (ms)", block.MaximumTimeMs);
                    Field("Destination MAC", "Not read");
                    Field("VLAN ID", "Not read");
                    Field("VLAN priority", "Not read");
                    Field("Needs commissioning", "Not read");
                    Field("MMS state inventory",
                        IedBrowserControlStateEvidence.ReadTargets(block, "GoCB").Count > 0
                            ? "Verified read-only attributes" : "Not verified / SCL-only");
                    Section("Information received in last GOOSE");
                    Field("Capture status", "Subscriber not capturing", "Passive GOOSE");
                    Field("Frame received at", "—", "Passive GOOSE");
                    Field("Source MAC", "—", "Passive GOOSE");
                    Field("Frame test", "—", "Passive GOOSE");
                    Field("Frame ndsCom", "—", "Passive GOOSE");
                    Field("State number", "—", "Passive GOOSE");
                    Field("Sequence number", "—", "Passive GOOSE");
                    Field("Time allowed to live", "—", "Passive GOOSE");
                    Field("Frame DataSet entries", "—", "Passive GOOSE");
                    Section("Data");
                    rows.Add(new("DataHeader", "Name", "Value"));
                    var gooseDirectoryModel =
                        IedBrowserGooseDirectory.ModelForDataSet(model, sclModel, block);
                    var linkedGooseSet = FindDataSet(gooseDirectoryModel, block.DataSetReference);
                    if (linkedGooseSet is null)
                        rows.Add(new("Message", "No exact FCDA directory yet. A matching GOOSE frame will display its received allData slots without guessed signal names."));
                    else
                    {
                        var ordered = linkedGooseSet.Members.OrderBy(m => m.Index).ToArray();
                        for (var slot = 0; slot < ordered.Length; slot++)
                        {
                            var member = ordered[slot];
                            rows.Add(IedBrowserStructuredDetails.Member(member,
                                gooseDirectoryModel) with
                            {
                                GooseSlot = slot,
                                GooseReference = member.Reference,
                                GooseFc = member.FunctionalConstraint
                            });
                        }
                    }
                }
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
                Section("Logical Node · " + ln.Name);
                Field("Class", ln.LnClass);
                Field("LN type", ln.ProposedLnTypeId);
                Section("Data Objects");
                rows.Add(new("DataHeader", "Name", "Live value · provenance"));
                // O(number of DO), with lazy expansion of each selected DO.
                // No recursive UI object creation for a large IED namespace.
                foreach (var obj in IedBrowserEngineeringObjects.GroupExact(ln.DataObjects))
                    rows.Add(IedBrowserStructuredDetails.DataObject(
                        obj, PrimaryValueFunctionalConstraint(obj)));
                break;
            case LiveIedDataObjectModel obj:
                Section("Data Object · " + obj.Name);
                Field("Reference", obj.Reference);
                Field("CDC", obj.InferredCdc);
                Field("Type confidence", obj.ConfidenceLevel);
                Section("Data");
                rows.Add(new("DataHeader", "Name", "Live value · provenance"));
                foreach (var attr in IedBrowserStructuredDetails.AttributeRows(
                             obj.Attributes, obj.Name, mergeFcRoots: true))
                    rows.Add(attr);
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
                rows.Add(new IedBrowserDetailRow("Property", "Value", "Not read",
                    Source: "Read-only MMS / observed report")
                {
                    ValueReference = attr.MmsReference,
                    FunctionalConstraint = attr.FunctionalConstraint
                });
                break;
            case IedBrowserRemoteFile remote:
                Section("Remote file");
                Field("Name", remote.Name);
                Field("Path", remote.RemotePath);
                Field("Size", remote.SizeText);
                Field("Last modified", remote.ModifiedText);
                Field("Source", "Live IEC 61850 FileDirectory");
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
