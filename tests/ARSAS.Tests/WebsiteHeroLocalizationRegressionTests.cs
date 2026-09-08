namespace ARSAS.Tests;

public sealed class WebsiteHeroLocalizationRegressionTests
{
    [Fact]
    public void HeroGradientHeadline_AllowsLocalizedCopyToWrapInsteadOfClipping()
    {
        var css = Read("landing/home.css").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("body[data-page=\"overview\"] .hero h1 .gradient-text", css, StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".gradient-text {\n    white-space: nowrap;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void IndonesianHero_HasDedicatedDesktopSafetyRules()
    {
        var css = Read("landing/home.css");

        Assert.Contains("body[data-page=\"overview\"] .hero:lang(id) .hero-grid", css, StringComparison.Ordinal);
        Assert.Contains("body[data-page=\"overview\"] .hero:lang(id) h1", css, StringComparison.Ordinal);
        Assert.Contains("body[data-page=\"overview\"] .hero:lang(id) .eyebrow", css, StringComparison.Ordinal);
        Assert.Contains("body[data-page=\"overview\"] .hero:lang(id) .hero-actions", css, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroBalance_HasWiderCanvasQuieterProductAndTertiaryActions()
    {
        var css = Read("landing/home.css");

        Assert.Contains("--home-hero-max: 1280px", css, StringComparison.Ordinal);
        Assert.Contains("filter: brightness(.92) saturate(.96) contrast(.985)", css, StringComparison.Ordinal);
        Assert.Contains(".hero-actions .btn-quiet", css, StringComparison.Ordinal);
        Assert.Contains(".floating-chip.two", css, StringComparison.Ordinal);
        Assert.Contains("opacity: .78", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CssArchitecture_ReplacesPhaseOverridesWithSemanticScopedLayers()
    {
        var header = Read("landing/partials/header.html");
        var home = Read("landing/home.css");
        var designSystem = Read("landing/design-system.css");

        Assert.Contains("design-system.css", header, StringComparison.Ordinal);
        Assert.Contains("home.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("hero-p0.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("section-p1.css", header, StringComparison.Ordinal);

        Assert.Contains("body[data-page=\"overview\"]", home, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.hero {", home, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.card {", home, StringComparison.Ordinal);

        Assert.Contains(".section {", designSystem, StringComparison.Ordinal);
        Assert.Contains(".card,", designSystem, StringComparison.Ordinal);
        Assert.DoesNotContain(".hero-grid", designSystem, StringComparison.Ordinal);
        Assert.DoesNotContain(".substation-scene", designSystem, StringComparison.Ordinal);
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
