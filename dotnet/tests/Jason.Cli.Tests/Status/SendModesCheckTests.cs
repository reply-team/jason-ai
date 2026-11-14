using Jason.Cli.Status;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status says how the installation's sends go, as the runtime reports it: for a contact who has not answered and
/// for one who has, the mode in force and where it comes from, in a sentence and as fields. Every message waiting for a
/// person is a way to work, so the check never decides readiness and is never a failure.
/// </summary>
public class SendModesCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendModeInForce Mode(SendMode mode, SendModeSource source) => new(mode, source);

    public static TheoryData<SendModesInForce, string, string, string, string, string> Cases() => new()
    {
        {
            new SendModesInForce(Mode(SendMode.EveryMessage, SendModeSource.Default), Mode(SendMode.EveryMessage, SendModeSource.Default)),
            "To a contact who has not answered: every message waits for a person, as nobody has chosen yet. To a contact who has: every message waits for a person, as nobody has chosen yet.",
            "every_message", "default", "every_message", "default"
        },
        {
            new SendModesInForce(Mode(SendMode.Automatic, SendModeSource.Preference), Mode(SendMode.EveryMessage, SendModeSource.Preference)),
            "To a contact who has not answered: released without asking unless something puts one to a person first, as the installation's preference says. To a contact who has: every message waits for a person, as the installation's preference says.",
            "automatic", "preference", "every_message", "preference"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_check_says_each_mode_and_where_it_comes_from_and_never_decides_readiness(
        SendModesInForce modes, string fact, string cold, string coldSource, string warm, string warmSource)
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        StatusCommandTests.SendModes(dir, modes);
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "send_modes");
        Assert.Equal((CheckState.Ok, false, fact, (string?)null), (check.State, check.Required, check.Fact, check.Fix));
        Assert.Equal(
            new Dictionary<string, string> { ["cold"] = cold, ["cold_source"] = coldSource, ["warm"] = warm, ["warm_source"] = warmSource },
            check.Details);
    }

    [Fact]
    public async Task A_runtime_older_than_the_section_leaves_it_unknown_with_no_repair()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "send_modes");
        Assert.Equal((CheckState.Unknown, false, (string?)null), (check.State, check.Required, check.Fix));
        Assert.Equal("The runtime is older than this CLI and does not say how its sends go.", check.Fact);
    }

    [Fact]
    public async Task A_runtime_that_does_not_answer_leaves_it_unknown_and_names_the_start()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "send_modes");
        Assert.Equal((CheckState.Unknown, false, "jason runtime start"), (check.State, check.Required, check.Fix));
    }
}
