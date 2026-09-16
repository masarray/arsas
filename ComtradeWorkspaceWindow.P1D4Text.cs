using System.Windows.Data;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private bool _p1d4DisplayNamesNormalized;

    private void NormalizeP1D4ComtradeDisplayNames()
    {
        if (_p1d4DisplayNamesNormalized) return;
        _p1d4DisplayNamesNormalized = true;

        if (SignalList.ItemsSource is not IEnumerable<ComtradeSignalItem> source)
            return;

        var statusNames = ComtradeCfgDisplayText.TryReadStatusChannelIds(
            _record.CfgPath,
            _record.Info.AnalogCount,
            _record.Info.StatusCount);
        if (statusNames.Count == 0)
            return;

        var selected = SignalList.SelectedItem as ComtradeSignalItem;
        var changed = false;
        var normalized = source
            .Select(item =>
            {
                if (item.IsAnalog || !statusNames.TryGetValue(item.Index, out var recovered) ||
                    string.IsNullOrWhiteSpace(recovered) || string.Equals(recovered, item.Title, StringComparison.Ordinal))
                    return item;

                changed = true;
                return item with { Title = recovered };
            })
            .OrderBy(item => item.SectionOrder)
            .ThenBy(item => item.PhaseOrder)
            .ThenBy(item => item.Index)
            .ToList();

        if (!changed) return;

        SignalList.ItemsSource = normalized;
        var view = CollectionViewSource.GetDefaultView(normalized);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComtradeSignalItem.Section)));

        if (selected is not null)
        {
            SignalList.SelectedItem = normalized.FirstOrDefault(item =>
                item.IsAnalog == selected.IsAnalog && item.Index == selected.Index);
        }
        if (SignalList.SelectedItem is null && normalized.Count > 0)
            SignalList.SelectedIndex = 0;
    }
}
