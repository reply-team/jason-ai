using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The one class whose rows call the toast interop, and the suite's one named exception to "no test touches the
/// desktop": it performs every call on the path to Show, for a test-only AppUserModelID, and never calls Show itself. It
/// writes no key and shows nothing; what it proves is that every vtable slot and interface id answers on a real Windows,
/// so a wrong one fails CI on the Windows leg rather than on a person's desktop.
/// </summary>
public sealed class WindowsToastDryTests
{
    private const string DryAumid = "ai.jason.tests.dry";

    /// <summary>Where Windows will not activate the notification classes at all, the test says so and skips.</summary>
    private static readonly int[] NotActivatable =
    [
        unchecked((int)0x80040154), // REGDB_E_CLASSNOTREG
        unchecked((int)0x80004001), // E_NOTIMPL
    ];

    [Fact]
    public void Every_call_on_the_path_to_show_answers_on_this_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the toast interop is Windows-only");
#pragma warning disable CA1416 // The skip above is the platform check.
        using var prepared = WindowsToast.Prepare(DryAumid, ToastXml.Compose("Jason", ["dry", "run"], launch: null));

        if (prepared.FailedStep is { } step && prepared.Calls.Single(call => call.Step == step).Hr is var hr && NotActivatable.Contains(hr))
        {
            Assert.Skip($"this Windows does not activate the notification classes: {step} = {WindowsToast.Hr(hr)}");
        }

        Assert.Null(prepared.FailedStep);
        Assert.NotEmpty(prepared.Calls);
        Assert.All(prepared.Calls, call => Assert.True(call.Hr == 0, $"{call.Step} answered {WindowsToast.Hr(call.Hr)}"));

        // And the one read after Show, made here without Show: the notifier's Setting, through the same slot the toast
        // host reads it by. Before an AppUserModelID's first toast Windows has no entry for it (0x80070490); a wrong
        // slot would be an access violation or an answer that is neither.
        var (settingHr, _) = prepared.ReadSetting();
        Assert.True(settingHr is 0 or unchecked((int)0x80070490), $"the Setting answered {WindowsToast.Hr(settingHr)}");
#pragma warning restore CA1416
    }

    /// <summary>
    /// The toast of one item, whose click launches a <c>jason:</c> link by protocol and whose one button is Copy prompt,
    /// is the XML Windows' own parser must take too: the same calls, none of them Show, each answering 0.
    /// </summary>
    [Fact]
    public void The_toast_of_one_item_with_its_link_and_its_button_answers_every_call_on_this_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the toast interop is Windows-only");
        const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";
        var xml = ToastXml.Compose("Jason", ["dry", "run"], AttentionLinks.Item(Approval), AttentionLinks.Prompt(Approval));
        Assert.Contains("activationType=\"protocol\" launch=", xml, StringComparison.Ordinal);
        Assert.Contains("<action ", xml, StringComparison.Ordinal);
#pragma warning disable CA1416 // The skip above is the platform check.
        using var prepared = WindowsToast.Prepare(DryAumid, xml);

        if (prepared.FailedStep is { } step && prepared.Calls.Single(call => call.Step == step).Hr is var hr && NotActivatable.Contains(hr))
        {
            Assert.Skip($"this Windows does not activate the notification classes: {step} = {WindowsToast.Hr(hr)}");
        }

        Assert.Null(prepared.FailedStep);
        Assert.NotEmpty(prepared.Calls);
        Assert.All(prepared.Calls, call => Assert.True(call.Hr == 0, $"{call.Step} answered {WindowsToast.Hr(call.Hr)}"));
#pragma warning restore CA1416
    }
}
