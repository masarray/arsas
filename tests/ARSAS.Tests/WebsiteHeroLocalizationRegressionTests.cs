namespace ARSAS.Tests;

public sealed class WebsiteHeroLocalizationRegressionTests
{
    [Fact]
    public void HeroGradientHeadline_AllowsLocalizedCopyToWrapInsteadOfClipping()
    {
        var css = Read("landing/overview.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("body[data-page=\"overview\"] .hero h1 .gradient-text", css, StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".gradient-text {\n  white-space: nowrap;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void IndonesianHero_HasDedicatedDesktopSafetyRules()
    {
        var css = Read("landing/overview.css");

        Assert.Contains(".hero:lang(id) .hero-grid", css, StringComparison.Ordinal);
        Assert.Contains(".hero:lang(id) h1", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1.03fr) minmax(0, .97fr);", css, StringComparison.Ordinal);
        Assert.Contains("max-width: min(39rem, 100%);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroBalance_HasWideContainerQuietScreenshotAndTertiaryActions()
    {
        var css = Read("landing/overview.css");

        Assert.Contains("1280px", css, StringComparison.Ordinal);
        Assert.Contains("filter: brightness(.88) saturate(.92) contrast(.98);", css, StringComparison.Ordinal);
        Assert.Contains(".hero-actions .btn:nth-child(n+3)", css, StringComparison.Ordinal);
        Assert.Contains("opacity: .76;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CssArchitecture_SeparatesSharedComponentsFromOverviewComposition()
    {
        var header = Read("landing/partials/header.html");
        var overview = Read("landing/overview.css");
        var shared = Read("landing/design-system.css");

        Assert.Contains("design-system.css", header, StringComparison.Ordinal);
        Assert.Contains("overview.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("hero-p0.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("section-p1.css", header, StringComparison.Ordinal);

        Assert.Contains("body[data-page=\"overview\"] .hero", overview, StringComparison.Ordinal);
        Assert.Contains("body[data-page=\"overview\"] .section", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.hero {", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.section {", overview, StringComparison.Ordinal);

        Assert.Contains(".card,", shared, StringComparison.Ordinal);
        Assert.Contains(".site-footer", shared, StringComparison.Ordinal);
        Assert.DoesNotContain(".hero-grid", shared, StringComparison.Ordinal);
        Assert.DoesNotContain(".hero-window", shared, StringComparison.Ordinal);
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