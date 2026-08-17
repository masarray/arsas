from pathlib import Path

main_path = Path("MainWindow.xaml")
test_path = Path("tests/ARSAS.Tests/VisualSystemP0RegressionTests.cs")

main = main_path.read_text(encoding="utf-8")
test = test_path.read_text(encoding="utf-8")

old = '''                                            <TextBlock Text="Search IED, signal, IEC reference, value, quality or acquisition"
                                                       VerticalAlignment="Center" IsHitTestVisible="False"
                                                       Style="{StaticResource Caption}" Foreground="#98A2B3">
                                                <TextBlock.Style>'''
new = '''                                            <TextBlock Text="Search IED, signal, IEC reference, value, quality or acquisition"
                                                       VerticalAlignment="Center" IsHitTestVisible="False"
                                                       Foreground="#98A2B3">
                                                <TextBlock.Style>'''
if main.count(old) != 1:
    raise SystemExit(f"Search placeholder review fix expected 1 match, found {main.count(old)}")
main = main.replace(old, new, 1)

old_test = '''    [Fact]
    public void P0_RemainsPresentationOnly()
    {
        var testDirectory = Path.GetDirectoryName(FindRepoFile("MainWindow.xaml"))!;
        var engineLock = File.ReadAllText(Path.Combine(testDirectory, "engines", "ARIEC61850.lock.json"));

        Assert.Contains("manual-reviewed-immutable-commit", engineLock, StringComparison.OrdinalIgnoreCase);
    }
'''
new_test = '''    [Fact]
    public void RuntimeNavigation_UsesTheSameP0GeometryAndStableTypography()
    {
        var source = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));

        Assert.Contains("shell.Width = 900", source, StringComparison.Ordinal);
        Assert.Contains("shell.CornerRadius = new CornerRadius(14)", source, StringComparison.Ordinal);
        Assert.Contains("button.FontSize = 12.5", source, StringComparison.Ordinal);
        Assert.Contains("button.FocusVisualStyle = null", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\\"10\\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.Width = 760", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_KeepsTheReviewedAriecEnginePinUntouched()
    {
        var testDirectory = Path.GetDirectoryName(FindRepoFile("MainWindow.xaml"))!;
        var engineLock = File.ReadAllText(Path.Combine(testDirectory, "engines", "ARIEC61850.lock.json"));

        Assert.Contains("becda399b4a3ae34831215fc915798b4f846c1be", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\\"sourcePullRequest\\\": 81", engineLock, StringComparison.Ordinal);
    }
'''
if test.count(old_test) != 1:
    raise SystemExit(f"P0 lock test expected 1 match, found {test.count(old_test)}")
test = test.replace(old_test, new_test, 1)

main_path.write_text(main, encoding="utf-8", newline="\n")
test_path.write_text(test, encoding="utf-8", newline="\n")
print("Applied P0 review fixes: valid WPF search placeholder and precise runtime/engine regression guards.")
