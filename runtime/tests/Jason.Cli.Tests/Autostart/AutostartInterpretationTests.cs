using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// What each tool's answer means, read from its exit code and its own words. This is the half of a registrar
/// that can be tested without a machine, and it is the half where the important distinction lives: "nothing is
/// registered" and "the tool would not say" are different answers, and one of them must never be reported as
/// the other.
/// </summary>
/// <remarks>
/// The strings below are what the tools really print. They are here so that the first run on a real machine
/// meets no shape this code has never seen — which is the failure mode a registrar has, being the one piece
/// nothing else can exercise.
/// </remarks>
public class AutostartInterpretationTests
{
    /// <summary>A task nobody has registered: exit 1, and the Task Scheduler says which file it cannot find.</summary>
    [Theory]
    [InlineData("ERROR: The system cannot find the file specified.\r\n")]
    [InlineData("ERROR: The specified task name \"Jason\" does not exist in the system.\r\n")]
    public void A_windows_task_that_is_not_there_is_an_answer_and_not_a_refusal(string error)
    {
        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Windows, 1, string.Empty, error);

        Assert.False(answer.Registered);
        Assert.Empty(answer.Document);
    }

    /// <summary>And a task that is there hands back its own document, which is what names the command line.</summary>
    [Fact]
    public void A_windows_task_that_is_there_hands_back_the_document()
    {
        const string Document = "<Task><Actions><Exec><Command>jason.exe</Command></Exec></Actions></Task>";

        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Windows, 0, Document, string.Empty);

        Assert.True(answer.Registered);
        Assert.Equal(Document, answer.Document);
    }

    /// <summary>
    /// The same exit code for a different reason: this account may not look at that task. Reported as "nothing
    /// is registered", it would send a person to run `enable` again and get the same silence.
    /// </summary>
    [Fact]
    public void A_windows_task_this_account_may_not_read_is_a_refusal()
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.Interpret(AutostartPlatform.Windows, 1, string.Empty, "ERROR: Access is denied.\r\n"));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("Access is denied", refused.Message, StringComparison.Ordinal);
        Assert.Contains("schtasks", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// `systemctl --user is-enabled` has three answers a person would recognise and one they would not: 0 is
    /// enabled, 1 is disabled, 4 is no such unit, and anything else is the manager itself refusing — most often
    /// because there is no user manager running at all, which is worth saying rather than calling it "no".
    /// </summary>
    [Theory]
    [InlineData(0, "enabled\n", true)]
    [InlineData(1, "disabled\n", false)]
    [InlineData(4, "", false)]
    public void A_user_unit_is_enabled_disabled_or_not_there(int exit, string output, bool registered) =>
        Assert.Equal(registered, AutostartRegistrars.Interpret(AutostartPlatform.Linux, exit, output, string.Empty).Registered);

    [Fact]
    public void A_user_manager_that_will_not_answer_is_a_refusal()
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.Interpret(
                AutostartPlatform.Linux,
                3,
                string.Empty,
                "Failed to connect to bus: No medium found\n"));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("Failed to connect to bus", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>`launchctl list` exits non-zero for a label it does not know, and says nothing worth repeating.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(113, false)]
    public void A_launch_agent_is_known_or_it_is_not(int exit, bool registered) =>
        Assert.Equal(registered, AutostartRegistrars.Interpret(AutostartPlatform.MacOs, exit, string.Empty, string.Empty).Registered);

    /// <summary>A machine that registers nothing answers that nothing is registered, whatever it was asked.</summary>
    [Fact]
    public void A_machine_that_registers_nothing_has_nothing_registered() =>
        Assert.False(AutostartRegistrars.Interpret(AutostartPlatform.Unsupported, 0, "anything", "anything").Registered);

    /// <summary>
    /// And the registrar for such a machine refuses to register, rather than doing nothing and reporting
    /// success — which is what an environment that names no registrar at all also gets.
    /// </summary>
    [Fact]
    public void The_registrar_for_a_machine_that_registers_nothing_refuses()
    {
        var registrar = AutostartRegistrars.Unsupported;
        var registration = AutostartArtifacts.Compose(
            AutostartPlatform.Linux,
            ["/usr/local/bin/jason"],
            "/home/ada/.jason",
            "/home/ada",
            "ada");

        Assert.Equal(AutostartPlatform.Unsupported, registrar.Platform);
        Assert.False(registrar.Read().Registered);

        var refused = Assert.Throws<AutostartException>(() => registrar.Apply(registration));
        Assert.Equal(AutostartCodes.Unsupported, refused.Code);
        Assert.Contains("jason runtime start", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one place this machine's own registrar is built. Every other environment in this repository leaves
    /// it null, and null is the refusal — a guard that types a documented `autostart enable` for real would
    /// otherwise register a logon task on whatever machine the tests are running on.
    /// </summary>
    [Fact]
    public void The_default_environment_is_the_one_place_this_machine_is_asked()
    {
        Assert.NotNull(CliEnvironment.Default().Autostart);

        using var directory = new TempPaths();
        Assert.Null(new CliEnvironment(new StringWriter(), new StringWriter(), directory.Paths).Autostart);
    }
}
