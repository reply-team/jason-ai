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
}
