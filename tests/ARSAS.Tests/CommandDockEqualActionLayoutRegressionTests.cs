using System.Text.RegularExpressions;

namespace ARSAS.Tests;

public sealed class CommandDockEqualActionLayoutRegressionTests
{
    [Theory]
    [InlineData("Open", "Close")]
    [InlineData("Raise", "Lower")]
    [InlineData("True", "False")]
    public void VisiblePairedActionsUseEqualGridTracks(string left, string right)
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var leftPattern = $@"<Button Grid.Column=""0"" Content=""{left}""";
        var rightPattern = $@"<Button Grid.Column=""2"" Content=""{right}""";
        Assert.Contains(leftPattern.Replace("@", ""), xaml, StringComparison.Ordinal);
        Assert.Contains(rightPattern.Replace("@", ""), xaml, StringComparison.Ordinal);
        Assert.True(Regex.Matches(xaml,
            @"<Grid.ColumnDefinitions><ColumnDefinition Width=""72""/><ColumnDefinition Width=""7""/><ColumnDefinition Width=""72""/></Grid.ColumnDefinitions>").Count >= 3);
    }

    private static string FindRepoFile(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException(path);
    }
}
