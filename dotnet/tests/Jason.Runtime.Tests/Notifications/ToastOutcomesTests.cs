using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// What a <c>toast-host</c> child's answer means, row by row. Show returns success while the person has switched
/// Jason off, so "delivered" never rests on Show alone: the Setting read after Show decides between delivered and
/// silenced, and it is never read before.
/// </summary>
public sealed class ToastOutcomesTests
{
    private static LaunchResult Exited(int code, string stdout = "", string stderr = "") => new(true, false, code, stdout, stderr);

    private static string Outcome(
        string registration = "present",
        string? failedStep = null,
        string? showHr = "0x00000000",
        string? settingHr = "0x00000000",
        int? setting = 0) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["protocol_version"] = 1,
            ["registration"] = registration,
            ["registration_hr"] = registration == "failed" ? "0x80070005" : null,
            ["calls"] = new System.Text.Json.Nodes.JsonArray(),
            ["failed_step"] = failedStep,
            ["show_hr"] = failedStep is null && registration != "failed" ? showHr : null,
            ["setting_after_hr"] = failedStep is null && registration != "failed" ? settingHr : null,
            ["setting_after"] = failedStep is null && registration != "failed" ? setting : null,
        }.ToJsonString();

    private static void Is(ChannelOutcome outcome, ChannelResult result, string? code = null, FailureClass? @class = null, string? reason = null, string? detail = null)
    {
        Assert.Equal("toast", outcome.Channel);
        Assert.Equal(ChannelKinds.Toast, outcome.Kind);
        Assert.Equal((result, code, @class, reason), (outcome.Result, outcome.Code, outcome.Class, outcome.Reason));
        if (detail is not null)
        {
            Assert.Equal(detail, outcome.Detail);
        }
    }

    [Fact]
    public void A_child_that_could_not_be_started_is_a_transient_failure() =>
        Is(ToastOutcomes.Read(new LaunchResult(false, false, null, "", "", "file not found")), ChannelResult.Failed, "toast_launch_failed", FailureClass.Transient, detail: "file not found");

    [Fact]
    [Critical(Family.F4, "a killed toast host")]
    public void A_child_killed_at_the_bound_is_ambiguous() =>
        Is(ToastOutcomes.Read(new LaunchResult(true, true, null, "", "")), ChannelResult.Failed, "toast_timeout", FailureClass.Ambiguous);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void A_child_that_refused_its_input_is_a_permanent_failure(int exit) =>
        Is(ToastOutcomes.Read(Exited(exit, stderr: "first\nusage: the reason")), ChannelResult.Failed, "toast_rejected", FailureClass.Permanent, detail: "usage: the reason");

    [Theory]
    [InlineData(4, "")]
    [InlineData(0, "")]
    [InlineData(0, "not json")]
    [InlineData(-1073741819, "")]
    public void A_child_that_said_nothing_readable_is_ambiguous(int exit, string stdout) =>
        Is(ToastOutcomes.Read(Exited(exit, stdout)), ChannelResult.Failed, "toast_no_outcome", FailureClass.Ambiguous);

    [Fact]
    public void A_registration_the_account_refused_is_a_permanent_failure() =>
        Is(ToastOutcomes.Read(Exited(0, Outcome(registration: "failed"))), ChannelResult.Failed, "toast_registration_failed", FailureClass.Permanent, detail: "0x80070005");

    [Fact]
    public void A_call_that_failed_on_the_way_to_show_is_a_permanent_failure()
    {
        var stdout = Outcome(failedStep: "RoActivateInstance(XmlDocument)").Replace(
            "\"calls\":[]",
            "\"calls\":[{\"step\":\"RoActivateInstance(XmlDocument)\",\"hr\":\"0x80040154\"}]",
            StringComparison.Ordinal);
        Is(ToastOutcomes.Read(Exited(0, stdout)), ChannelResult.Failed, "toast_call_failed", FailureClass.Permanent, detail: "RoActivateInstance(XmlDocument)=0x80040154");
    }

    [Fact]
    public void A_show_that_failed_is_a_permanent_failure() =>
        Is(ToastOutcomes.Read(Exited(0, Outcome(showHr: "0x80070005"))), ChannelResult.Failed, "toast_show_failed", FailureClass.Permanent, detail: "0x80070005");

    [Fact]
    public void Shown_and_enabled_is_delivered() =>
        Is(ToastOutcomes.Read(Exited(0, Outcome())), ChannelResult.Delivered);

    /// <summary>Before an AppUserModelID's first toast Windows has no entry for it, and says so; after Show it is no fault.</summary>
    [Fact]
    public void Shown_with_no_settings_entry_yet_is_delivered() =>
        Is(ToastOutcomes.Read(Exited(0, Outcome(settingHr: "0x80070490", setting: null))), ChannelResult.Delivered);

    [Theory]
    [InlineData(1, SilencedReasons.DisabledForApplication)]
    [InlineData(2, SilencedReasons.DisabledForUser)]
    [InlineData(3, SilencedReasons.DisabledByGroupPolicy)]
    [InlineData(4, SilencedReasons.DisabledByManifest)]
    public void Shown_while_switched_off_is_silenced(int setting, string reason) =>
        Is(ToastOutcomes.Read(Exited(0, Outcome(setting: setting))), ChannelResult.Silenced, reason: reason);

    [Fact]
    public void Shown_with_a_setting_that_could_not_be_read_is_delivered_and_says_so() =>
        Is(ToastOutcomes.Read(Exited(0, Outcome(settingHr: "0x80004005", setting: null))), ChannelResult.Delivered, detail: "setting unreadable: 0x80004005");
}
