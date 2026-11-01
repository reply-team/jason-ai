using Jason.Contracts.Ui;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Ui;

public class UiManifestTests
{
    [Fact]
    public void A_manifest_is_read_and_an_absent_one_is_null()
    {
        var directory = Directory.CreateTempSubdirectory("jason-ui-manifest").FullName;
        try
        {
            Assert.Null(UiManifest.Read(directory));
            File.WriteAllText(Path.Combine(directory, UiAssetLayout.ManifestFile), "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}");
            Assert.Equal(new UiManifest("0.10.0", "v1"), UiManifest.Read(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    // Critical: a UI build's manifest.
    [InlineData("not json", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B6)])]
    [InlineData("{\"version\":\"0.10.0\"}")]
    [InlineData("{\"version\":\"\",\"api_version\":\"v1\"}")]
    [InlineData("{\"version\":\"0.10.0\",\"api_version\":\"v1\",\"extra\":1}")]
    public void A_manifest_that_is_not_one_is_refused_by_name(string text)
    {
        var directory = Directory.CreateTempSubdirectory("jason-ui-manifest").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, UiAssetLayout.ManifestFile), text);
            var refused = Assert.Throws<UiManifestException>(() => UiManifest.Read(directory));
            Assert.Contains(UiAssetLayout.ManifestFile, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
