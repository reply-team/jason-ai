using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>What the runtime hands the <c>toast-host</c> child on stdin: one toast, under Jason's own AppUserModelID.</summary>
public sealed record ToastHostPayload(
    [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
    [property: JsonPropertyName("aumid")] string Aumid,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines);

/// <summary>One call the child made, as its outcome reports it.</summary>
public sealed record ToastHostCall(
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("hr")] string Hr);

/// <summary>
/// The one line the child writes on stdout: what it found of the registration, every call on the path to Show, the
/// call that failed if any, Show's answer, and the Setting read after Show — its HRESULT, and its value where it was read.
/// </summary>
public sealed record ToastHostOutcome(
    [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
    [property: JsonPropertyName("registration")] string Registration,
    [property: JsonPropertyName("registration_hr")] string? RegistrationHr,
    [property: JsonPropertyName("calls")] IReadOnlyList<ToastHostCall> Calls,
    [property: JsonPropertyName("failed_step")] string? FailedStep,
    [property: JsonPropertyName("show_hr")] string? ShowHr,
    [property: JsonPropertyName("setting_after_hr")] string? SettingAfterHr,
    [property: JsonPropertyName("setting_after")] int? SettingAfter);

/// <summary>
/// How the payload and the outcome are written: compact JSON with every character outside ASCII escaped, so the wire is
/// ASCII both ways, as the plugin host's is. The toast host reads its stdin through <see cref="Console.In"/>, and a child
/// started without a window of its own is given a fresh console on the OEM code page: a byte above 127 would be read
/// through that page, whatever encoding the runtime wrote it in. Escaped, no such byte is ever written.
/// </summary>
internal static class ToastHostJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
