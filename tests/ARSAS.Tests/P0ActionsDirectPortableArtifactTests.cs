namespace ARSAS.Tests;

public sealed class P0ActionsDirectPortableArtifactTests
{
    [Fact]
    public void PortableExeIsDirectDownloadWithNoZipAndKeepsSealedReleasePackage()
    {
        // Verifies distribution only; no compiler, IED, engine or release
        // authority changes. GitHub upload-artifact@v7 supports archive:false
        // only for ONE file and derives artifact name from its basename.
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".github", "workflows", "build.yml");
            if (!File.Exists(path)) continue;
            var yaml = File.ReadAllText(path);
            Assert.Contains("name: ARSAS-windows-package-input", yaml);
            Assert.Contains("name: Upload sealed Windows package input", yaml);
            Assert.Contains("name: Upload directly downloadable portable EXE (no ZIP)", yaml);
            var start = yaml.IndexOf(
                "      - name: Upload directly downloadable portable EXE (no ZIP)",
                StringComparison.Ordinal);
            Assert.True(start >= 0);
            var tail = yaml[start..];
            Assert.Contains("uses: actions/upload-artifact@v7", tail);
            Assert.Contains(@"path: ArIED61850Tester\dist\ARSAS-*-win-x64-portable.exe", tail);
            Assert.Contains("archive: false", tail);
            Assert.Contains("if-no-files-found: error", tail);
            Assert.DoesNotContain("portable-build-identity.json", tail);
            Assert.DoesNotContain("name: ARSAS-win-x64-portable-single-exe", tail);
            // No synthetic release asset, side workflow or new build steps.
            Assert.DoesNotContain("gh release create", tail);
            return;
        }
        throw new FileNotFoundException("Repository build.yml not found.");
    }
}
