using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// What each tool's answer means, read from what it returns rather than from what it says. This is the half of
/// a registrar that can be tested without a machine, and it is the half where the important distinction lives:
/// "nothing is registered" and "the tool would not say" are different answers, and one of them must never be
/// reported as the other.
/// </summary>
/// <remarks>
/// The values below are what the tools really return; the Windows ones were measured on this machine and the
/// Linux ones under its WSL. They are here so that the first run on a real machine meets no shape this code has
/// never seen — which is the failure mode a registrar has, being the one piece nothing else can exercise.
/// </remarks>
public class AutostartInterpretationTests
{
    /// <summary>ERROR_FILE_NOT_FOUND, which is what `schtasks /Query … /HRESULT` returns for a missing task.</summary>
    private const int TaskNotFound = unchecked((int)0x80070002);

    /// <summary>ERROR_ACCESS_DENIED, which is the same exit code without `/HRESULT` and a different one with it.</summary>
    private const int AccessDenied = unchecked((int)0x80070005);

    /// <summary>
    /// The query asks for a code, and that is the whole point of it. Without <c>/HRESULT</c> the Task Scheduler
    /// exits 1 for a task that is not there and 1 for a task this account may not read, and only the sentence
    /// tells them apart — in the language Windows was installed in.
    /// </summary>
    [Fact]
    public void The_windows_query_asks_for_a_code_rather_than_a_sentence() =>
        Assert.Contains("/HRESULT", AutostartArtifacts.QueryFor(AutostartPlatform.Windows), StringComparer.Ordinal);

    /// <summary>
    /// A task nobody has registered, in two languages. An English-only matcher answered "the tool refused" on
    /// every Windows that is not English — so a clean machine there answered `status` with a failure, `disable`
    /// on nothing with a failure, and `enable` registered the task and then failed reading it back.
    /// </summary>
    [Theory]
    [InlineData("ERROR: The system cannot find the file specified.\r\n")]
    [InlineData("ОШИБКА: Не удается найти указанный файл.\r\n")]
    [InlineData("")]
    public void A_windows_task_that_is_not_there_is_an_answer_in_any_language(string error)
    {
        var answer = AutostartRegistrars.Interpret(AutostartPlatform.Windows, TaskNotFound, string.Empty, error);

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
    /// Anything else is the tool refusing, in any language and whatever it printed: this account may not read
    /// that task, or the scheduler service is not running. Reported as "nothing is registered", it would send a
    /// person to run `enable` again and get the same silence.
    /// </summary>
    [Theory]
    [InlineData(AccessDenied, "ERROR: Access is denied.\r\n")]
    [InlineData(AccessDenied, "ОШИБКА: Отказано в доступе.\r\n")]
    [InlineData(1, "anything at all")]
    [InlineData(1, "")]
    [InlineData(-2147024891, "ERROR: Access is denied.")]
    public void Any_other_code_from_the_task_scheduler_is_a_refusal(int exit, string error)
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.Interpret(AutostartPlatform.Windows, exit, string.Empty, error));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("schtasks", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// `systemctl --user is-enabled` is read by the word it prints. Its exit code cannot carry the answer: 1 is
    /// `disabled`, and 1 is also what it returns with nothing on standard output when it cannot reach a user
    /// manager at all.
    /// </summary>
    [Theory]
    [InlineData("enabled\n", 0, true)]
    [InlineData("enabled-runtime\n", 0, true)]
    [InlineData("static\n", 0, true)]
    [InlineData("indirect\n", 0, true)]
    [InlineData("generated\n", 0, true)]
    [InlineData("alias\n", 0, true)]
    [InlineData("disabled\n", 1, false)]
    [InlineData("masked\n", 1, false)]
    [InlineData("not-found\n", 4, false)]
    public void A_user_unit_is_read_by_the_word_the_manager_prints(string output, int exit, bool registered) =>
        Assert.Equal(registered, AutostartRegistrars.Interpret(AutostartPlatform.Linux, exit, output, string.Empty).Registered);

    /// <summary>
    /// The measured shape of a machine with no user bus: exit 1, nothing on standard output, and the reason on
    /// standard error. Read by the exit code it is "disabled", which for a unit that is registered is a lie in
    /// the direction that sounds safe — and the same machine's `enable` refuses loudly, so the two verbs would
    /// disagree about the same installation.
    /// </summary>
    [Fact]
    public void A_user_manager_that_cannot_be_reached_is_a_refusal_and_not_a_disabled_unit()
    {
        const string Said = "Failed to connect to user scope bus via local transport: $DBUS_SESSION_BUS_ADDRESS and $XDG_RUNTIME_DIR not defined\n";

        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.Interpret(AutostartPlatform.Linux, 1, string.Empty, Said));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("Failed to connect to user scope bus", refused.Message, StringComparison.Ordinal);
        Assert.Contains("systemctl --user", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>And a word this build has never heard of is a refusal too, rather than a guess either way.</summary>
    [Fact]
    public void A_word_the_manager_has_not_printed_before_is_a_refusal()
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartRegistrars.Interpret(AutostartPlatform.Linux, 0, "something-new\n", string.Empty));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("something-new", refused.Message, StringComparison.Ordinal);
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
    /// The one place this machine's own registrar is built, and it really is this machine's: a
    /// <see cref="CliEnvironment.Default"/> that answered with the refusing one would keep a "not null" test
    /// green while every real `enable` refused. Every other environment in this repository leaves it null, and
    /// null is the refusal — a guard that types a documented `autostart enable` for real would otherwise
    /// register a logon task on whatever machine the tests are running on.
    /// </summary>
    [Fact]
    public void The_default_environment_is_the_one_place_this_machine_is_asked()
    {
        var expected =
            OperatingSystem.IsWindows() ? AutostartPlatform.Windows
            : OperatingSystem.IsMacOS() ? AutostartPlatform.MacOs
            : OperatingSystem.IsLinux() ? AutostartPlatform.Linux
            : AutostartPlatform.Unsupported;

        var registrar = CliEnvironment.Default().Autostart;

        Assert.NotNull(registrar);
        Assert.Equal(expected, registrar.Platform);

        using var directory = new TempPaths();
        Assert.Null(new CliEnvironment(new StringWriter(), new StringWriter(), directory.Paths).Autostart);
    }
}
