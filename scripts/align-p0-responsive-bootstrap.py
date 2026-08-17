from pathlib import Path

main_path = Path('MainWindow.xaml')
sas_path = Path('SasOperationalUiPolicy.cs')
test_path = Path('tests/ARSAS.Tests/VisualSystemP0RegressionTests.cs')

main = main_path.read_text(encoding='utf-8')
sas = sas_path.read_text(encoding='utf-8')
test = test_path.read_text(encoding='utf-8')

pairs = [
    (main, 'x:Name="WorkflowNavShell" Width="900" Height="56"', 'x:Name="WorkflowNavShell" Width="760" Height="56"', 'MainWindow bootstrap width'),
    (sas, 'shell.Width = 900;', 'shell.Width = 760;', 'runtime bootstrap width'),
]
for index, (text, old, new, label) in enumerate(pairs):
    if text.count(old) != 1:
        raise SystemExit(f'{label}: expected one match, found {text.count(old)}')
    text = text.replace(old, new, 1)
    if index == 0:
        main = text
    else:
        sas = text

old_test = '''    [Fact]\n    public void RuntimeNavigation_UsesTheSameP0GeometryAndStableTypography()\n    {\n        var source = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));\n\n        Assert.Contains("shell.Width = 900", source, StringComparison.Ordinal);\n        Assert.Contains("shell.CornerRadius = new CornerRadius(14)", source, StringComparison.Ordinal);\n        Assert.Contains("button.FontSize = 12.5", source, StringComparison.Ordinal);\n        Assert.Contains("button.FocusVisualStyle = null", source, StringComparison.Ordinal);\n        Assert.Contains("CornerRadius=\\"10\\"", source, StringComparison.Ordinal);\n        Assert.DoesNotContain("shell.Width = 760", source, StringComparison.Ordinal);\n    }\n'''
new_test = '''    [Fact]\n    public void RuntimeNavigation_UsesStableP0Style_WhileResponsiveFixOwnsFinalWidth()\n    {\n        var runtime = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));\n        var responsive = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));\n\n        Assert.Contains("shell.Width = 760", runtime, StringComparison.Ordinal);\n        Assert.Contains("shell.CornerRadius = new CornerRadius(14)", runtime, StringComparison.Ordinal);\n        Assert.Contains("button.FontSize = 12.5", runtime, StringComparison.Ordinal);\n        Assert.Contains("button.FocusVisualStyle = null", runtime, StringComparison.Ordinal);\n        Assert.Contains("CornerRadius=\\"10\\"", runtime, StringComparison.Ordinal);\n        Assert.Contains("WideNavWidth = 990d", responsive, StringComparison.Ordinal);\n        Assert.Contains("MediumNavWidth = 900d", responsive, StringComparison.Ordinal);\n        Assert.Contains("CompactNavWidth = 720d", responsive, StringComparison.Ordinal);\n        Assert.Contains("shell.Width = shellWidth", responsive, StringComparison.Ordinal);\n    }\n'''
if test.count(old_test) != 1:
    raise SystemExit(f'Runtime navigation test: expected one match, found {test.count(old_test)}')
test = test.replace(old_test, new_test, 1)

main_path.write_text(main, encoding='utf-8', newline='\n')
sas_path.write_text(sas, encoding='utf-8', newline='\n')
test_path.write_text(test, encoding='utf-8', newline='\n')
print('Aligned P0 bootstrap width with legacy CI while keeping responsive 990/900/720 authority.')
