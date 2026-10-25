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
    public void ProductScreenshots_PreserveTheirSourceAspectRatio()
    {
        var audit = Read("landing/audit.css");

        Assert.Contains(".screenshot-link img { width: 100%; height: auto; object-fit: contain; }", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(".screenshot-link,\n.reporting-visual", audit.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("landing/templates/index.html", "Start here")]
    [InlineData("landing/templates/id.html", "Mulai di sini")]
    [InlineData("landing/templates/quick-start.html", "Three visible steps")]
    [InlineData("landing/templates/panduan-mulai-arsas.html", "Tiga langkah yang terlihat")]
    public void BeginnerJourney_UsesTheCurrentThreeStepProductFlow(string path, string heading)
    {
        var page = Read(path);

        Assert.Contains(heading, page, StringComparison.Ordinal);
        Assert.Contains("arsas-quick-start-choose-source-v1.6.40.webp", page, StringComparison.Ordinal);
        Assert.Contains("arsas-quick-start-discover-ip-v1.6.40.webp", page, StringComparison.Ordinal);
        // Setup-dialog captures are explicitly historical; step 3 is the actual public stable UI.
        Assert.Contains("arsas-ied-browser-v1.6.42.webp", page, StringComparison.Ordinal);
        Assert.Contains("href=\"assets/screenshots/arsas-ied-browser-v1.6.42.png\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("arsas-quick-start-monitor-control-v1.6.40.webp", page, StringComparison.Ordinal);
        Assert.Contains("v1.6.40", page, StringComparison.Ordinal);
        Assert.Contains("v1.6.42", page, StringComparison.Ordinal);
        Assert.Contains("SCL", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepageGuidedOnboarding_SurfacesBothEntryPathsAndSclFormats()
    {
        var english = Read("landing/templates/index.html");
        var indonesian = Read("landing/templates/id.html");

        foreach (var value in new[]
                 {
                     "I have a live relay or IED",
                     "I have an engineering file",
                     "Substation Configuration Language",
                     "ICD", "CID", "IID", "SCD",
                     "Open the complete Quick Start"
                 })
            Assert.Contains(value, english, StringComparison.Ordinal);

        foreach (var value in new[]
                 {
                     "Saya punya relay atau IED live",
                     "Saya punya file engineering",
                     "Substation Configuration Language",
                     "ICD", "CID", "IID", "SCD",
                     "Buka Quick Start lengkap"
                 })
            Assert.Contains(value, indonesian, StringComparison.Ordinal);

        foreach (var stale in new[]
                 {
                     "approved relay IP",
                     "approved IED IP",
                     "Connect by approved IP address",
                     "alamat IP relay yang disetujui",
                     "IP IED yang disetujui",
                     "alamat IP yang disetujui"
                 })
        {
            Assert.DoesNotContain(stale, english, StringComparison.Ordinal);
            Assert.DoesNotContain(stale, indonesian, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HomepageEvidence_UsesWideReadableScreenshotPresentation()
    {
        var home = Read("landing/home.css");
        var english = Read("landing/templates/index.html");

        Assert.Contains("body[data-page=\"overview\"] .home-evidence .product-tour-grid", home, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", home, StringComparison.Ordinal);
        Assert.Contains("minmax(0, 1.7fr)", home, StringComparison.Ordinal);
        Assert.Contains("class=\"product-tour-grid feature-evidence\"", english, StringComparison.Ordinal);
        Assert.Contains("src=\"assets/screenshots/arsas-ied-browser-v1.6.42.webp\"", english, StringComparison.Ordinal);
        Assert.Contains("href=\"assets/screenshots/arsas-annunciator-v1.6.42.png\"", english, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("landing/templates/demo.html")]
    [InlineData("landing/templates/demo-arsas.html")]
    public void GuidedDemo_UsesActualV1642CapturesWithOriginalPngLinks(string path)
    {
        var page = Read(path);
        foreach (var screen in new[]
                 {
                     "ied-browser", "event-logs", "signal-testing", "fat-report-preview",
                     "smv", "goose", "annunciator", "diagnostics"
                 })
        {
            Assert.Contains($"href=\"assets/screenshots/arsas-{screen}-v1.6.42.png\"", page, StringComparison.Ordinal);
            Assert.Contains($"src=\"assets/screenshots/arsas-{screen}-v1.6.42.webp\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PublicNavigation_UsesLocaleSpecificDestinationsAndAccessibleMenuLabels()
    {
        var header = Read("landing/partials/header.html");
        var css = Read("landing/audit.css");
        var app = Read("landing/app.js");

        Assert.Contains("Langsung ke konten", header, StringComparison.Ordinal);
        Assert.Contains("href=\"id.html\" data-nav-page=\"overview\" data-lang=\"id\"", header, StringComparison.Ordinal);
        Assert.Contains("href=\"pusat-belajar-iec61850.html\" data-nav-page=\"learn\" data-lang=\"id\"", header, StringComparison.Ordinal);
        Assert.Contains("href=\"panduan.html\" data-nav-page=\"guides\" data-lang=\"id\"", header, StringComparison.Ordinal);
        Assert.Contains("href=\"unduh.html\" data-nav-page=\"download\"", header, StringComparison.Ordinal);
        Assert.Contains("href=\"./\" lang=\"en\" hreflang=\"en\"", header, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"arsas-primary-nav\"", header, StringComparison.Ordinal);
        Assert.Contains("[data-lang=\"id\"] { display: none; }", css, StringComparison.Ordinal);
        Assert.Contains("html[lang=\"id\"] [data-lang=\"en\"] { display: none; }", css, StringComparison.Ordinal);
        Assert.Contains("Buka navigasi", app, StringComparison.Ordinal);
        Assert.Contains("Tutup navigasi", app, StringComparison.Ordinal);
        Assert.Contains("Navigasi utama", app, StringComparison.Ordinal);
        Assert.Contains("menuLabel.close : menuLabel.open", app, StringComparison.Ordinal);
    }

    [Fact]
    public void CssArchitecture_ReplacesPhaseOverridesWithSemanticScopedLayers()
    {
        var header = Read("landing/partials/header.html");
        var index = Read("landing/templates/index.html");
        var builder = Read("scripts/build-product-site.py");
        var home = Read("landing/home.css");
        var designSystem = Read("landing/design-system.css");

        Assert.DoesNotContain("design-system.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("polish.css", header, StringComparison.Ordinal);
        Assert.DoesNotContain("home.css", header, StringComparison.Ordinal);
        Assert.Contains("home.css", index, StringComparison.Ordinal);
        Assert.Contains("design-system.css", builder, StringComparison.Ordinal);
        Assert.Contains("polish.css", builder, StringComparison.Ordinal);
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
