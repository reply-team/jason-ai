using System.IO.Compression;
using System.Text;

namespace Jason.Cli.Tests.Ui;

/// <summary>
/// A UI build as <c>npm run package</c> lays it out — the files at the root, the bundle under <c>assets/</c>, the
/// manifest naming the version it was built for — made here, because the .NET tests run where there is no Node.
/// </summary>
public static class UiZip
{
    public static byte[] Build(string version, string apiVersion = "v1")
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[]
            {
                ("index.html", $"<!doctype html><title>Jason {version}</title><script type=\"module\" src=\"/ui/assets/app.js\"></script>"),
                ("manifest.json", $"{{\"version\":\"{version}\",\"api_version\":\"{apiVersion}\"}}\n"),
                ("favicon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
                ("assets/app.js", "export {};"),
                ("THIRD-PARTY-NOTICES", "preact: MIT"),
            })
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }
}
