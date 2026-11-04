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

        public string? ReadClassesString(string key, string name) => null;

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
        var outcome = ToastHost.Show(payload, new RegisteredAlready(), Self, (_, _) => toast);
#pragma warning restore CA1416

        Assert.Equal(["show", "setting"], toast.Order);
        Assert.Equal(("0x00000000", "0x00000000", (int?)0), (outcome.ShowHr, outcome.SettingAfterHr, outcome.SettingAfter));
    }

    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private static readonly string[] Self = [@"C:\jason\jason.exe"];

    private static ToastHostPayload Shown(string? item = null) =>
        new(ToastCommands.Protocol, ToastRegistration.Aumid, ToastRegistration.DisplayName, "Jason", ["one", "two"], item);

    /// <summary>What the toast was made from, and how many registry writes had been made by then.</summary>
    private sealed class Made
    {
        public string? Xml { get; private set; }

        public int WritesBefore { get; private set; } = -1;

        public Func<string, string, IPreparedToast> Prepare(FakeUserRegistry registry, OrderedToast toast) => (_, xml) =>
        {
            Xml = xml;
            WritesBefore = registry.Writes.Count;
            return toast;
        };
    }

#pragma warning disable CA1416 // Driven with fakes: the registry and the toast are this test's, and nothing reaches Windows.

    /// <summary>The AppUserModelID key first, then the link registration, and only then is the toast made.</summary>
    [Fact]
    public void The_link_is_registered_after_the_app_user_model_id_and_before_the_toast_is_made()
    {
        var registry = new FakeUserRegistry();
        var toast = new OrderedToast();
        var made = new Made();

        var outcome = ToastHost.Show(Shown(), registry, Self, made.Prepare(registry, toast));

        Assert.Equal(ToastRegistration.Key, registry.Writes[0].Key);
        Assert.All(registry.Writes.Skip(1), write => Assert.StartsWith(LinkRegistration.Key, write.Key, StringComparison.Ordinal));
        Assert.Equal(5, made.WritesBefore);
        Assert.Equal(registry.Writes.Count, made.WritesBefore);
        Assert.Equal(("written", "written", (string?)null), (outcome.Registration, outcome.Scheme, outcome.SchemeHr));
        Assert.Equal(["show", "setting"], toast.Order);
    }

    [Fact]
    public void One_item_s_toast_launches_that_item()
    {
        var registry = new FakeUserRegistry();
        var made = new Made();

        ToastHost.Show(Shown(Approval), registry, Self, made.Prepare(registry, new OrderedToast()));

        Assert.StartsWith($"<toast activationType=\"protocol\" launch=\"jason://attention/approvals/{Approval}\">", made.Xml, StringComparison.Ordinal);
        Assert.Contains($"arguments=\"jason://attention/approvals/{Approval}/prompt\"", made.Xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The toast after the first finds both keys as it left them: nothing is written, and the link present is a link
    /// the click launches, with the item's Copy prompt button, as a link just written is.
    /// </summary>
    [Fact]
    public void A_link_registration_already_present_launches_the_item_and_writes_nothing()
    {
        var registry = new FakeUserRegistry();
        AppUserModelIdRegistration.Ensure(registry);
        SchemeRegistration.Ensure(registry, Self);
        registry.Writes.Clear();
        var toast = new OrderedToast();
        var made = new Made();

        var outcome = ToastHost.Show(Shown(Approval), registry, Self, made.Prepare(registry, toast));

        Assert.Equal(("present", "present", (string?)null), (outcome.Registration, outcome.Scheme, outcome.SchemeHr));
        Assert.Empty(registry.Writes);
        Assert.StartsWith($"<toast activationType=\"protocol\" launch=\"jason://attention/approvals/{Approval}\">", made.Xml, StringComparison.Ordinal);
        Assert.Contains($"arguments=\"jason://attention/approvals/{Approval}/prompt\"", made.Xml, StringComparison.Ordinal);
        Assert.Equal(["show", "setting"], toast.Order);
    }

    [Fact]
    public void A_toast_with_no_item_launches_the_list()
    {
        var registry = new FakeUserRegistry();
        var made = new Made();

        ToastHost.Show(Shown(), registry, Self, made.Prepare(registry, new OrderedToast()));

        Assert.StartsWith("<toast activationType=\"protocol\" launch=\"jason://attention\">", made.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<actions>", made.Xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Another program owns <c>jason:</c>: the toast is still shown, with no link at all, so a click can never hand an
    /// id to that program.
    /// </summary>
    [Fact]
    public void A_foreign_link_registration_shows_the_toast_without_a_link()
    {
        var registry = new FakeUserRegistry();
        registry.Classes[(LinkRegistration.ClassesKey, LinkRegistration.ProtocolValue)] = string.Empty;
        var toast = new OrderedToast();
        var made = new Made();

        var outcome = ToastHost.Show(Shown(Approval), registry, Self, made.Prepare(registry, toast));

        Assert.Equal("foreign", outcome.Scheme);
        Assert.StartsWith("<toast><visual>", made.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("jason:", made.Xml, StringComparison.Ordinal);
        Assert.Equal(["show", "setting"], toast.Order);
    }

    /// <summary>A link registration that failed never fails the delivery: the toast is shown without a link.</summary>
    [Fact]
    public void A_failed_link_registration_shows_the_toast_without_a_link()
    {
        var registry = new FakeUserRegistry { Refuse = new UnauthorizedAccessException("denied"), RefuseAfter = 1 };
        var toast = new OrderedToast();
        var made = new Made();

        var outcome = ToastHost.Show(Shown(Approval), registry, Self, made.Prepare(registry, toast));

        Assert.Equal(("written", "failed", "0x80070005"), (outcome.Registration, outcome.Scheme, outcome.SchemeHr));
        Assert.DoesNotContain("launch=", made.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<actions>", made.Xml, StringComparison.Ordinal);
        Assert.Equal(["show", "setting"], toast.Order);
    }

    /// <summary>The AppUserModelID key refused ends the toast where it ended before: nothing more is written or made.</summary>
    [Fact]
    public void A_refused_app_user_model_id_still_ends_before_the_link_and_the_toast()
    {
        var registry = new FakeUserRegistry { Refuse = new UnauthorizedAccessException("denied") };
        var made = new Made();

        var outcome = ToastHost.Show(Shown(Approval), registry, Self, made.Prepare(registry, new OrderedToast()));

        Assert.Equal(("failed", (string?)null), (outcome.Registration, outcome.Scheme));
        Assert.Null(made.Xml);
        Assert.Empty(registry.Writes);
    }

#pragma warning restore CA1416

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
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two"],"item":"cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6"}""", "neither an approval nor a question")]
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two"],"item":"apr_01jbz3x7q9k2m4n6p8r0s2t4v6"}""", "neither an approval nor a question")]
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two"],"item":"apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6/prompt"}""", "neither an approval nor a question")]
    [InlineData("""{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["one","two"],"item":""}""", "neither an approval nor a question")]
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
    [Critical(Family.B3, "the toast host quotes nothing")]
    public async Task A_payload_it_cannot_parse_is_rejected_without_being_quoted()
    {
        const string Sentinel = "SENTINEL_TOAST_PAYLOAD";

        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], "{\"title\":\"" + Sentinel + "\",\"lines\":");

        Assert.Equal(3, exit);
        Assert.Empty(stdout);
        Assert.Contains("could not be read as JSON", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, stderr, StringComparison.Ordinal);
    }

    /// <summary>A malformed item is refused by what it is, never quoted.</summary>
    [Fact]
    public async Task A_malformed_item_is_rejected_without_being_quoted()
    {
        const string Sentinel = "apr_SENTINEL";
        var payload = Payload(ToastRegistration.Aumid);
        payload["item"] = Sentinel;

        var (exit, stdout, stderr) = await RunAsync(["--protocol", "1"], payload.ToJsonString());

        Assert.Equal(3, exit);
        Assert.Empty(stdout);
        Assert.DoesNotContain(Sentinel, stderr, StringComparison.Ordinal);
    }

    /// <summary>A payload of a runtime from before there was an item reads as one with none, so an update mid-way still shows a toast.</summary>
    [Fact]
    public void A_payload_without_an_item_reads_as_one_with_none()
    {
        Assert.Null(ToastHost.Refusal(Payload(ToastRegistration.Aumid).ToJsonString(), out var payload));
        Assert.Null(payload.Item);
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
