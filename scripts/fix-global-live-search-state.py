from pathlib import Path

def replace_once(path, old, new):
    p=Path(path); s=p.read_text(encoding='utf-8'); c=s.count(old)
    if c != 1: raise SystemExit(f'{path}: expected 1 match, got {c}')
    p.write_text(s.replace(old,new,1),encoding='utf-8',newline='\n')

replace_once('MainWindow.xaml',
'''                        <DataGrid ItemsSource="{Binding GlobalPoints}" IsReadOnly="True" Style="{StaticResource ModernDataGrid}"''',
'''                        <DataGrid x:Name="GlobalLiveGrid" ItemsSource="{Binding GlobalPoints}" IsReadOnly="True" Style="{StaticResource ModernDataGrid}"''')

replace_once('MainWindow.GlobalLiveSearch.cs',
'''    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => GridUxBehavior.SetGlobalRapidSearch(this, GlobalLiveSearchBox?.Text);''',
'''    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => GridUxBehavior.SetGlobalRapidSearch(GlobalLiveGrid, GlobalLiveSearchBox?.Text);''')

p=Path('GridUxBehavior.cs'); s=p.read_text(encoding='utf-8')
start=s.index('    internal static void SetGlobalRapidSearch(')
end=s.index('    private static void ApplyGlobalColumnStretch', start)
new='''    internal static void SetGlobalRapidSearch(DataGrid grid, string? query)
    {
        if (!GlobalGrids.TryGetValue(grid, out var state))
            return;

        state.SearchQuery = query?.Trim() ?? string.Empty;
        state.RefreshTimer.Stop();
        state.RefreshTimer.Start();
    }

'''
p.write_text(s[:start]+new+s[end:],encoding='utf-8',newline='\n')

replace_once('tests/ARSAS.Tests/EventDrivenSessionLiveMonitorRegressionTests.cs',
'''        Assert.Contains("GlobalLiveSearchBox", section, StringComparison.Ordinal);''',
'''        Assert.Contains("GlobalLiveSearchBox", section, StringComparison.Ordinal);
        Assert.Contains("x:Name=\\\"GlobalLiveGrid\\\"", section, StringComparison.Ordinal);''')
replace_once('tests/ARSAS.Tests/EventDrivenSessionLiveMonitorRegressionTests.cs',
'''        Assert.Contains("SetGlobalRapidSearch", bridge, StringComparison.Ordinal);''',
'''        Assert.Contains("SetGlobalRapidSearch(GlobalLiveGrid", bridge, StringComparison.Ordinal);''')
print('Bound global search directly to the installed Live Monitor grid state.')
