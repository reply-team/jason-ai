using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The reading <see cref="SystemRefusalInLog"/> makes, held on lines written here: what it refuses is the refusal's own
/// line carrying an exception, or the run directory named anywhere but as the descriptor; an exception another line of
/// the log carries is not the refusal's.
/// </summary>
public sealed class SystemRefusalInLogTests : IDisposable
{
    private readonly TempDataDir _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// A start refused after the host had started its services may log, from the host, a background service cancelled as
    /// the host is disposed. The line names no path and is not the refusal's.
    /// </summary>
    [Fact]
    public void An_exception_the_host_writes_on_a_line_of_its_own_is_not_the_refusals()
    {
        SystemRefusalInLog.AssertNone(Log(Refusal(), HostCancelled()), _data.Paths);
    }

    [Fact]
    public void The_refusals_own_line_carrying_an_exception_is_refused()
    {
        var refusal = Refusal();
        refusal["@x"] = "System.IO.IOException: (any language)";

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SystemRefusalInLog.AssertNone(Log(refusal), _data.Paths));
    }

    [Fact]
    public void The_run_directory_named_other_than_as_the_descriptor_is_refused()
    {
        var leak = new JsonObject { ["@mt"] = "{Detail}", ["Detail"] = $"(any language) \"{Path.Combine(_data.Paths.RunDirectory, ".runtime.0123.tmp")}\" (any language)" };

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SystemRefusalInLog.AssertNone(Log(Refusal(), leak), _data.Paths));
    }

    /// <summary>The descriptor's own property is exempt only where it names the descriptor, not another file beside it.</summary>
    [Fact]
    public void A_descriptor_property_naming_another_file_in_the_run_directory_is_refused()
    {
        var temporary = Path.Combine(_data.Paths.RunDirectory, ".runtime.0123.tmp");
        var refusal = Refusal();
        refusal["Descriptor"] = temporary;

        var refused = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SystemRefusalInLog.AssertNone(Log(refusal), _data.Paths));
        Assert.Contains(temporary, refused.Message, StringComparison.Ordinal);
    }

    private JsonObject Refusal() => new()
    {
        ["@mt"] = "The runtime did not start: its endpoint descriptor at {Descriptor} could not be published: {Why}",
        ["@l"] = "Error",
        ["Descriptor"] = _data.Paths.DescriptorFile,
        ["Why"] = "replacing it was refused for 0.3 seconds",
    };

    /// <summary>The host's line as a refused start's log carried it on a loaded machine, its message in any language.</summary>
    private static JsonObject HostCancelled() => new()
    {
        ["@mt"] = "BackgroundService failed",
        ["@l"] = "Error",
        ["@x"] = "System.Threading.Tasks.TaskCanceledException: (any language)\r\n   at Microsoft.Extensions.Hosting.Internal.Host.TryExecuteBackgroundServiceAsync(BackgroundService backgroundService)",
        ["EventId"] = new JsonObject { ["Id"] = 9, ["Name"] = "BackgroundServiceFaulted" },
        ["SourceContext"] = "Microsoft.Extensions.Hosting.Internal.Host",
    };

    private static string Log(params JsonObject[] lines) => string.Concat(lines.Select(line => line.ToJsonString() + "\n"));
}
