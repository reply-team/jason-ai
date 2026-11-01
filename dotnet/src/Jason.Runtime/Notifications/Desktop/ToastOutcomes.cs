using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// What a <c>toast-host</c> child's answer means for the toast channel. Show returns success while the person has
/// switched Jason off, so "delivered" never rests on Show alone: the Setting read after Show decides between delivered
/// and silenced. Setting is never read before Show, and its "no entry yet" after the first Show is no fault.
/// </summary>
public static class ToastOutcomes
{
    public const string LaunchFailed = "toast_launch_failed";

    public const string Timeout = "toast_timeout";

    public const string RejectedCode = "toast_rejected";

    public const string NoOutcome = "toast_no_outcome";

    public const string RegistrationFailed = "toast_registration_failed";

    public const string CallFailed = "toast_call_failed";

    public const string ShowFailed = "toast_show_failed";

    /// <summary>Windows has no settings entry for this AppUserModelID yet: ERROR_NOT_FOUND.</summary>
    public const int NoEntryYet = unchecked((int)0x80070490);

    private const string Channel = "toast";

    public static ChannelOutcome Read(LaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Started)
        {
            return Failed(LaunchFailed, FailureClass.Transient, result.StartError);
        }

        if (result.TimedOut)
        {
            return Failed(Timeout, FailureClass.Ambiguous);
        }

        if (result.ExitCode is ToastHost.Usage or ToastHost.Rejected)
        {
            return Failed(RejectedCode, FailureClass.Permanent, result.LastStderrLine);
        }

        if (result.ExitCode != ToastHost.Outcome || Parse(result.Stdout) is not { } outcome)
        {
            return Failed(NoOutcome, FailureClass.Ambiguous, $"exit {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        }

        if (outcome.Registration == AppUserModelIdRegistration.Failed)
        {
            return Failed(RegistrationFailed, FailureClass.Permanent, outcome.RegistrationHr);
        }

        if (outcome.FailedStep is { } step)
        {
            var hr = outcome.Calls.LastOrDefault(call => call.Step == step)?.Hr ?? "unknown";
            return Failed(CallFailed, FailureClass.Permanent, $"{step}={hr}");
        }

        if (Hr(outcome.ShowHr) is not 0)
        {
            return Failed(ShowFailed, FailureClass.Permanent, outcome.ShowHr);
        }

        return (Hr(outcome.SettingAfterHr), outcome.SettingAfter) switch
        {
            (0, 0) => ChannelOutcome.Delivered(Channel, ChannelKinds.Toast),
            (0, 1) => ChannelOutcome.Silenced(Channel, ChannelKinds.Toast, SilencedReasons.DisabledForApplication),
            (0, 2) => ChannelOutcome.Silenced(Channel, ChannelKinds.Toast, SilencedReasons.DisabledForUser),
            (0, 3) => ChannelOutcome.Silenced(Channel, ChannelKinds.Toast, SilencedReasons.DisabledByGroupPolicy),
            (0, 4) => ChannelOutcome.Silenced(Channel, ChannelKinds.Toast, SilencedReasons.DisabledByManifest),
            (NoEntryYet, _) => ChannelOutcome.Delivered(Channel, ChannelKinds.Toast),
            _ => ChannelOutcome.Delivered(Channel, ChannelKinds.Toast) with { Detail = $"setting unreadable: {outcome.SettingAfterHr}" },
        };
    }

    private static ChannelOutcome Failed(string code, FailureClass @class, string? detail = null) =>
        ChannelOutcome.Failed(Channel, ChannelKinds.Toast, code, @class, detail);

    private static ToastHostOutcome? Parse(string stdout)
    {
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (line is null)
        {
            return null;
        }

        try
        {
            var outcome = JsonSerializer.Deserialize<ToastHostOutcome>(line, ToastHostJson.Options);
            return outcome is { Registration: not null, Calls: not null } ? outcome : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Hr(string? text) =>
        text is ['0', 'x', .. var digits] && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hr) ? hr : null;
}
