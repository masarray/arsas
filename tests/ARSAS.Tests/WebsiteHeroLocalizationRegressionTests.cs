namespace ARSAS.Tests;

public sealed class WebsiteHeroLocalizationRegressionTests
{
    [Fact]
    public void HeroGradientHeadline_AllowsLocalizedCopyToWrapInsteadOfClipping()
    {
        var css = Read("landing/hero-p0.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".hero h1 .gradient-text", css, StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".hero h1 .gradient-text {\n    display: block;\n    white-space: nowrap;",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IndonesianHero_HasDedicatedDesktopSafetyRules()
    {
        var css = Read("landing/hero-p0.css");

        Assert.Contains(".hero:lang(id) .hero-grid", css, StringComparison.Ordinal);
        Assert.Contains(".hero:lang(id) h1", css, StringComparison.Ordinal);
        Assert.Contains(".hero:lang(id) .eyebrow", css, StringComparison.Ordinal);
        Assert.Contains(".hero:lang(id) .hero-actions", css, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap;", css, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
