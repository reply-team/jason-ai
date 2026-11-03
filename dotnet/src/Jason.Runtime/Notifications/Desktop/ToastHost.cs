using System.Runtime.Versioning;
using System.Text.Json;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Notifications;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// The <c>toast-host</c> mode: a short-lived child of the same executable that shows one toast and says what happened.
/// It reads one payload on stdin, ensures Jason's per-user AppUserModelID, makes every call on the path to Show, shows,
/// reads the Setting after Show, and writes one outcome line on stdout. The exit code says only whether that completed:
/// 0 an outcome written, 2 usage, 3 rejected, 4 host failure — the plugin host's codes.
/// </summary>
/// <remarks>
/// It is a mode rather than code in the runtime because a fault in the hand-written interop ends this child, which is
/// recorded as a failed toast, and never the runtime. It registers nothing but <see cref="ToastRegistration.Aumid"/>
/// and the <c>jason:</c> link that starts this executable: a payload naming anything else is rejected before the
/// registry is touched.
/// </remarks>
public static class ToastHost
{
    public const int Outcome = 0;

    public const int Usage = 2;

    public const int Rejected = 3;

    public const int HostFailure = 4;

    /// <summary>The longest line a toast is given; the pointer's lines are far shorter.</summary>
    public const int MaxLine = 200;

    public static async Task<int> RunAsync(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args is not ["--protocol", "1"])
        {
            await stderr.WriteLineAsync("usage: jason toast-host --protocol 1, with one toast payload on stdin").ConfigureAwait(false);
            return Usage;
        }

        var text = await stdin.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (Refusal(text, out var payload) is { } reason)
        {
            await stderr.WriteLineAsync($"rejected: {reason}").ConfigureAwait(false);
            return Rejected;
        }

        if (!OperatingSystem.IsWindows())
        {
            await stderr.WriteLineAsync("usage: toast-host runs on Windows only; other platforms show a notification with their own program").ConfigureAwait(false);
            return Usage;
        }

        try
        {
            var outcome = Show(payload, new RegistryUserRegistry(), SelfExecutable.Command, WindowsToast.Prepare);
            await stdout.WriteLineAsync(JsonSerializer.Serialize(outcome, ToastHostJson.Options)).ConfigureAwait(false);
            return Outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"host failure: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            return HostFailure;
        }
    }

    /// <summary>Why a payload cannot be shown, or null with the payload when it can.</summary>
    internal static string? Refusal(string text, out ToastHostPayload payload)
    {
        payload = null!;
        ToastHostPayload? read;
        try
        {
            read = JsonSerializer.Deserialize<ToastHostPayload>(text, ToastHostJson.Options);
        }
        catch (JsonException)
        {
            return "the payload could not be read as JSON";
        }

        if (read is null || read.Aumid is null || read.DisplayName is null || read.Title is null || read.Lines is null)
        {
            return "the payload could not be read: it needs protocol_version, aumid, display_name, title and lines";
        }

        if (read.ProtocolVersion != ToastCommands.Protocol)
        {
            return $"this host reads protocol_version {ToastCommands.Protocol}, not {read.ProtocolVersion}";
        }

        if (!string.Equals(read.Aumid, ToastRegistration.Aumid, StringComparison.Ordinal))
        {
            return $"this host registers only {ToastRegistration.Aumid}, not '{read.Aumid}'";
        }

        if (!string.Equals(read.DisplayName, ToastRegistration.DisplayName, StringComparison.Ordinal))
        {
            return $"a toast says it is from {ToastRegistration.DisplayName}, not '{read.DisplayName}'";
        }

        if (read.Lines.Count is 0 or > 2)
        {
            return $"a toast has a title and one or at most two lines, not {read.Lines.Count}";
        }

        if (read.Lines.Prepend(read.Title).Any(line => line is null || line.Length > MaxLine))
        {
            return $"every line is text of at most {MaxLine} characters";
        }

        // By what it is and never by what it says: the item is the one field a person's toast carries from the database.
        if (read.Item is { } item
            && !PublicId.IsWellFormed(AttentionLinks.ApprovalPrefix, item)
            && !PublicId.IsWellFormed(AttentionLinks.QuestionPrefix, item))
        {
            return "an item is neither an approval nor a question by its public id";
        }

        payload = read;
        return null;
    }

    /// <summary>
    /// The measured sequence: ensure the key, ensure the <c>jason:</c> link, every call on the path to Show, Show, then
    /// the Setting. The registry and the toast come in through the seam, so a test holds the order with fakes.
    /// </summary>
    /// <remarks>
    /// The link never fails a toast. Present or written, a click launches the one item's link, or the list's when the
    /// payload names no item; another program's or refused, the toast is the one shown before there was a link, so a
    /// click can hand nothing to a program that is not Jason.
    /// </remarks>
    /// <param name="self">How this executable is started, which the link's command names.</param>
    [SupportedOSPlatform("windows")]
    internal static ToastHostOutcome Show(ToastHostPayload payload, IUserRegistry registry, IReadOnlyList<string> self, Func<string, string, IPreparedToast> prepare)
    {
        var (registration, registrationHr) = AppUserModelIdRegistration.Ensure(registry);
        if (registration == AppUserModelIdRegistration.Failed)
        {
            return new ToastHostOutcome(ToastCommands.Protocol, registration, registrationHr, [], null, null, null, null);
        }

        var (scheme, schemeHr) = SchemeRegistration.Ensure(registry, self);
        var launch = scheme is SchemeRegistration.Present or SchemeRegistration.Written
            ? AttentionLinks.Item(payload.Item) ?? AttentionLinks.List
            : null;
        using var prepared = prepare(payload.Aumid, ToastXml.Compose(payload.Title, payload.Lines, launch));
        var calls = prepared.Calls.Select(call => new ToastHostCall(call.Step, WindowsToast.Hr(call.Hr))).ToList();
        if (prepared.FailedStep is { } failed)
        {
            return new ToastHostOutcome(ToastCommands.Protocol, registration, null, calls, failed, null, null, null, scheme, schemeHr);
        }

        var shown = prepared.Show();
        var (settingHr, setting) = prepared.ReadSetting();
        return new ToastHostOutcome(
            ToastCommands.Protocol,
            registration,
            null,
            calls,
            null,
            WindowsToast.Hr(shown),
            WindowsToast.Hr(settingHr),
            setting,
            scheme,
            schemeHr);
    }
}
