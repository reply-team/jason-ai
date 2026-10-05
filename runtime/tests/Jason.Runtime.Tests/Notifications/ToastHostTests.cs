using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The <c>toast-host</c> mode's refusals, on every operating system. Nothing is shown here: every case stops before
/// the registry or WinRT is touched, and the one that would not stop on Windows runs only where the mode refuses to.
/// </summary>
public sealed class ToastHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Payload(string aumid = "ai.jason.tests.never-registered", params string[] lines) => new()
    {
        ["protocol_version"] = 1,
        ["aumid"] = aumid,
        ["display_name"] = ToastRegistration.DisplayName,
        ["title"] = "Jason",
        ["lines"] = new JsonArray([.. (lines.Length == 0 ? ["one", "two"] : lines).Select(line => (JsonNode?)line)]),
    };

    /// <summary>A registry that already holds Jason's key, so nothing is written.</summary>
    private sealed class RegisteredAlready : IUserRegistry
    {
        public string? ReadString(string key, string name) => ToastRegistration.DisplayName;

        public void WriteString(string key, string name, string value) => throw new InvalidOperationException("nothing is written here");
    }

    /// <summary>A prepared toast that touches nothing and remembers the order it was asked in.</summary>
    private sealed class OrderedToast : IPreparedToast
    {
        public List<string> Order { get; } = [];

        public IReadOnlyList<ToastCall> Calls { get; } = [new("CreateToastNotifierWithId", 0)];

        public string? FailedStep => null;

        public int Show()
        {
            Order.Add("show");
            return 0;
        }

        public (int Hr, int? Value) ReadSetting()
        {
            Order.Add("setting");
            return (0, 0);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The Setting is read after Show, never before it: before an AppUserModelID's first toast Windows has no entry for
    /// it, and only Show makes one. Driven through the seam with fakes, so nothing touches Windows.
    /// </summary>
    [Fact]
    public void The_setting_is_read_after_show()
    {
        var toast = new OrderedToast();
        var payload = new ToastHostPayload(ToastCommands.Protocol, ToastRegistration.Aumid, ToastRegistration.DisplayName, "Jason", ["one", "two"]);

#pragma warning disable CA1416 // Driven with fakes: the registry and the toast are this test's, and nothing reaches Windows.
        var outcome = ToastHost.Show(payload, new RegisteredAlready(), (_, _) => toast);
#pragma warning restore CA1416

        Assert.Equal(["show", "setting"], toast.Order);
        Assert.Equal(("0x00000000", "0x00000000", (int?)0), (outcome.ShowHr, outcome.SettingAfterHr, outcome.SettingAfter));
    }

    /// <summary>
    /// The payload and the outcome are escaped JSON, ASCII on the wire both ways, and read back as they were written. The
    /// toast host reads its stdin through the console a child without a window is given, on the OEM code page, so no
    /// byte above 127 may reach it; and the line it writes back goes through the same page.
    /// </summary>
    [Fact]
    public void The_payload_and_the_outcome_are_ascii_on_the_wire_and_read_back_as_written()
    {
        const string Text = "✓ € naïve";
        var launch = ToastCommands.Windows(["jason.exe"], new NotificationMessage(Text, Text, Text, 1, 0));

        var stdin = Assert.IsType<string>(launch.Stdin);
        Assert.True(stdin.All(char.IsAscii), $"the payload carries text outside ASCII as itself: {stdin}");
        Assert.Null(ToastHost.Refusal(stdin, out var payload));
        Assert.Equal([Text, Text, Text], [payload.Title, .. payload.Lines]);

        var written = JsonSerializer.Serialize(
            new ToastHostOutcome(ToastCommands.Protocol, Text, Text, [new ToastHostCall(Text, Text)], Text, Text, Text, 1),
            ToastHostJson.Options);
        Assert.True(written.All(char.IsAscii), $"the outcome carries text outside ASCII as itself: {written}");
        var read = JsonSerializer.Deserialize<ToastHostOutcome>(written, ToastHostJson.Options)!;
        Assert.Equal((Text, Text, Text, Text, Text), (read.Registration, read.Calls[0].Step, read.Calls[0].Hr, read.FailedStep, read.ShowHr));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(string[] args, string stdin)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await ToastHost.RunAsync(args, new StringReader(stdin), stdout, stderr, Ct);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("--protocol")]
    [InlineData("--protocol 2")]
    [InlineData("--protocol 1 --more")]
    public async Task A_command_line_it_does_not_speak_is_usage(string line)
    {
        var (exit, stdout, stderr) = await RunAsync(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), Payload().ToJsonString());

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.Contains("--protocol 1", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// What it was handed is checked before anything is touched: an AUMID other than Jason's own is refused, so this
    /// mode can register nothing else, and so is anything a toast of two counts and where to look could not be.
    /// </summary>
    [Theory]
    [InlineData("not json", "could not be read")]
    [InlineData("""{"protocol_version":1}""", "could not be read")]
    [InlineData("""{"protocol_version":1,"aumid":"someone.else","display_name":"Jason","title":"Jason","lines":["one","two"]}""", "registers only ai.jason.runtime")]
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Not Jason","title":"Jason","lines":["one","two"]}""", "says it is from Jason")]
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two","three"]}""", "at most two lines")]
    [InlineData("""{"protocol_version":2,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two"]}""", "protocol_version 1")]
    public async Task A_payload_it_cannot_show_is_rejected(string payload, string reason)
    {
        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], payload);

        Assert.Equal(3, exit);
        Assert.Empty(stdout);
        Assert.Contains(reason, stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A payload that cannot be parsed is said to be unreadable and nothing more: what the parser read is not repeated
    /// on the stderr the runtime keeps the end of, whatever the parser's own message would have quoted.
    /// </summary>
    [Fact]
    public async Task A_payload_it_cannot_parse_is_rejected_without_being_quoted()
    {
        const string Sentinel = "SENTINEL_TOAST_PAYLOAD";

        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], "{\"title\":\"" + Sentinel + "\",\"lines\":");

        Assert.Equal(3, exit);
        Assert.Empty(stdout);
        Assert.Contains("could not be read as JSON", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_longer_than_a_toast_holds_is_rejected()
    {
        var payload = Payload(ToastRegistration.Aumid, "one", new string('x', 201)).ToJsonString();

        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], payload);

        Assert.Equal(3, exit);
        Assert.Empty(stdout);
        Assert.Contains("at most 200 characters", stderr, StringComparison.Ordinal);
    }

    /// <summary>Everywhere but Windows the mode has nothing to show with: a payload it would accept is usage there.</summary>
    [Fact]
    public async Task Elsewhere_than_windows_a_payload_it_would_accept_is_usage()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "on Windows this payload would be shown; the case is about every other platform");

        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], Payload(ToastRegistration.Aumid).ToJsonString());

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.Contains("toast-host runs on Windows only", stderr, StringComparison.Ordinal);
    }
}
