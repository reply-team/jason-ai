using System.Text;
using System.Text.Json;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>
/// <c>desktop/test/fixtures/layout-stamps.json</c> holds what a data directory's <c>layout.json</c> says, case by case, and
/// the desktop's reader is tested against the same file. This holds <see cref="LayoutStamp"/> to it, so that the runtime's
/// reading of a stamp and the desktop's cannot part: a case either reader answers differently is red on that side.
/// </summary>
public sealed class DesktopLayoutStampsTests
{
    [Fact]
    public async Task Every_desktop_stamp_fixture_is_read_by_LayoutStamp_as_its_case_says_and_refused_in_the_same_words()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "test", "fixtures", "layout-stamps.json")));
        var root = fixture.RootElement;
        var current = root.GetProperty("current").GetString()!;
        var sentences = root.GetProperty("sentences");
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        Assert.NotEmpty(cases);
        var parted = new List<string>();
        void Check(bool holds, string what)
        {
            if (!holds)
            {
                parted.Add(what);
            }
        }

        foreach (var @case in cases)
        {
            var name = @case.GetProperty("name").GetString()!;
            if (!Applies(Text(@case, "only")))
            {
                continue;
            }

            var data = Directory.CreateTempSubdirectory("jason-layout-stamp-").FullName;
            try
            {
                var stamp = await Read(data, @case);
                Check(@case.GetProperty("state").GetString() == State(stamp.State), $"{name}: {stamp}");
                Check(Text(@case, "problem") == stamp.Problem, $"{name}: the problem is '{stamp.Problem}'");
                Check(Number(@case, "layout") == stamp.Layout, $"{name}: the layout is {stamp.Layout}");
                Check(Text(@case, "written_by") == stamp.WrittenBy, $"{name}: the writer is '{stamp.WrittenBy}'");
                Check(Text(@case, "at") == stamp.At, $"{name}: the moment is '{stamp.At}'");
                Check(@case.GetProperty("answered").GetInt32() == stamp.Answered, $"{name}: answered by {stamp.Answered}");

                var refusal = stamp.RefusalFor(current);
                var code = Text(@case, "refusal");
                Check(code == refusal?.Code, $"{name}: refused with '{refusal?.Code}'");
                Check((code is null) == !stamp.Refuses, $"{name}: refuses {stamp.Refuses}");
                if (code is not null)
                {
                    var sentence = sentences.GetProperty(code).GetString()!
                        .Replace("{written_by}", stamp.WrittenBy ?? string.Empty, StringComparison.Ordinal)
                        .Replace("{current}", current, StringComparison.Ordinal)
                        .Replace("{path}", stamp.Path, StringComparison.Ordinal)
                        .Replace("{problem}", stamp.Problem ?? string.Empty, StringComparison.Ordinal);
                    Check(sentence == refusal?.Message, $"{name}: said '{refusal?.Message}'");
                }
            }
            finally
            {
                Directory.Delete(data, recursive: true);
            }
        }

        Assert.True(parted.Count == 0, string.Join(Environment.NewLine, parted));
    }

    /// <summary>Whether a case the fixture keeps to some systems is one this run can make: a pipe, a device or a folder's mode.</summary>
    private static bool Applies(string? only) => only switch
    {
        null => true,
        "posix" => !OperatingSystem.IsWindows(),
        "posix_unprivileged" => !OperatingSystem.IsWindows() && !Environment.IsPrivilegedProcess,
        _ => throw new InvalidOperationException($"No case is kept to '{only}'."),
    };

    /// <summary>
    /// The stamp of a data directory holding the case: on disk as it names, or a file of its text or bytes. Read off the
    /// test's thread and given thirty seconds, so a read that waits on what is at the name fails rather than hangs.
    /// </summary>
    private static async Task<LayoutStamp> Read(string data, JsonElement @case)
    {
        var ct = TestContext.Current.CancellationToken;
        var file = Path.Combine(data, LayoutStamp.FileName);
        var from = data;
        switch (Text(@case, "on_disk"))
        {
            case "missing":
                break;
            case "folder":
                Directory.CreateDirectory(file);
                break;
            case "oversized":
                File.WriteAllBytes(file, Encoding.UTF8.GetBytes("{\"layout\":1,\"written_by\":\"0.42.0\",\"x\":\"" + new string('x', LayoutStamp.MaxBytes) + "\"}"));
                break;
            case "under_a_file":
                from = Path.Combine(data, "inner");
                File.WriteAllBytes(from, []);
                break;
            case "dangling_link":
                File.CreateSymbolicLink(file, Path.Combine(data, "gone.json"));
                break;
            case "device_link":
                File.CreateSymbolicLink(file, "/dev/null");
                break;
            case "fifo":
                using (var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo") { ArgumentList = { file } })!)
                {
                    await make.WaitForExitAsync(ct);
                    Assert.Equal(0, make.ExitCode);
                }

                break;
            case "unaskable":
                if (!OperatingSystem.IsWindows())
                {
                    from = Path.Combine(data, "shut");
                    Directory.CreateDirectory(from);
                    File.WriteAllText(Path.Combine(from, LayoutStamp.FileName), """{"layout":1,"written_by":"0.42.0"}""");
                    File.SetUnixFileMode(from, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    try
                    {
                        return await Task.Run(() => LayoutStamp.Read(from), ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
                    }
                    finally
                    {
                        File.SetUnixFileMode(from, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }

                throw new InvalidOperationException("A folder's mode is how macOS and Linux refuse the look.");
            case null:
                File.WriteAllBytes(file, Text(@case, "base64") is { } encoded ? Convert.FromBase64String(encoded) : Encoding.UTF8.GetBytes(Text(@case, "text")!));
                break;
            case var other:
                throw new InvalidOperationException($"No case is on disk as '{other}'.");
        }

        return await Task.Run(() => LayoutStamp.Read(from), ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    private static string State(LayoutStampState state) => state switch
    {
        LayoutStampState.Missing => "missing",
        LayoutStampState.Read => "read",
        _ => "unreadable",
    };

    private static string? Text(JsonElement @case, string name) =>
        @case.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement @case, string name) =>
        @case.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
