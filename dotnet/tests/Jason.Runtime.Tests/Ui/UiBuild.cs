using System.Text.Json;

namespace Jason.Runtime.Tests.Ui;

/// <summary>A UI build as the asset lays it out, small enough to write in a test: the four files the module needs.</summary>
internal static class UiBuild
{
    public static void Write(string directory, string version = "0.0.0-test", string apiVersion = "v1", string marker = "default")
    {
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(Path.Combine(directory, "index.html"), $"<!doctype html><html><head><title>Jason · {marker}</title><link rel=\"icon\" href=\"favicon.svg\"><script type=\"module\" src=\"/ui/assets/app-abc123.js\"></script></head><body><div id=\"app\"></div></body></html>");
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new { version, api_version = apiVersion }));
        File.WriteAllText(Path.Combine(directory, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        File.WriteAllText(Path.Combine(directory, "assets", "app-abc123.js"), $"console.log(\"{marker}\");");
    }

    /// <summary>
    /// Files under <c>sealed/</c>, each saying what it is: a page, a script a directory down, a name too long to be a short
    /// one, and a name with an accent, composed.
    /// </summary>
    public static void WriteSealed(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, "sealed", "nested"));
        File.WriteAllText(Path.Combine(directory, "sealed", "page.html"), "<!doctype html><title>sealed content</title><script>document.title = 'sealed content';</script>");
        File.WriteAllText(Path.Combine(directory, "sealed", "nested", "part.js"), "// sealed content");
        File.WriteAllText(Path.Combine(directory, "sealed", "a-long-page-name.html"), "<!doctype html><title>sealed content</title>");
        File.WriteAllText(Path.Combine(directory, "sealed", "caf\u00e9.html"), "<!doctype html><title>sealed content</title>");
    }
}
