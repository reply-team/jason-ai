using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// What the Windows registrar does with the Task Scheduler's answers, one command at a time, with the Task
/// Scheduler played by a script. Nothing here starts a process or registers anything: <see cref="WindowsTasks"/>
/// is handed the thing that runs <c>schtasks</c>, and a test hands it a script instead.
/// </summary>
/// <remarks>
/// <para>
/// Two names are asked after. <c>Jason-&lt;SID&gt;</c> is this account's own task. <c>Jason</c> is what 0.1.0
/// registered, in the one folder every account on the machine shares, so a task of that name may be this
/// account's or somebody else's. <c>enable</c>, <c>disable</c> and <c>jason uninstall</c> ask after it once, and
/// take it away only when its principal is this account.
/// </para>
/// <para>
/// The codes are what <c>/HRESULT</c> makes <c>schtasks</c> exit with: <c>0x80070002</c> for a task that is not
/// there, <c>0x80070005</c> for one this prompt may not touch. Another account's per-user task answers an
/// ordinary prompt with the second — measured on Windows 11 24H2.
/// </para>
/// </remarks>
public class WindowsTasksTests
{
    private const int TaskNotFound = unchecked((int)0x80070002);
    private const int AccessDenied = unchecked((int)0x80070005);

    /// <summary>SCHED_E_SERVICE_NOT_RUNNING: a code none of the branches below is about.</summary>
    private const int ServiceNotRunning = unchecked((int)0x80041315);

    private const string Sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
    private const string Own = "Jason-" + Sid;
    private const string Legacy = "Jason";
    private const string Denied = "ERROR: Access is denied.";

    private static readonly WindowsAccount Ada = new(Sid, @"ADA-PC\ada");

    private static readonly AutostartRegistration Registration = AutostartArtifacts.Compose(
        AutostartPlatform.Windows, [@"C:\jason.exe"], @"C:\Users\ada\.jason", @"C:\Users\ada", Sid);

    /// <summary>What 0.1.0 registered its task to run: the runtime itself, detached, on the data directory it named.</summary>
    private const string Runtime = "runtime run --detached --data-dir C:\\Users\\ada\\.jason";

    /// <summary>
    /// A task document as the Task Scheduler hands it back, run as whoever <paramref name="userId"/> names, running
    /// <paramref name="command"/> with <paramref name="arguments"/> — 0.1.0's own shape unless a test says otherwise.
    /// </summary>
    private static string Stored(string userId, string command = @"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", string arguments = Runtime) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Principals>
            <Principal id="Author">
              <UserId>{userId}</UserId>
              <LogonType>S4U</LogonType>
            </Principal>
          </Principals>
          <Triggers>
            <LogonTrigger>
              <UserId>ADA-PC\ada</UserId>
            </LogonTrigger>
          </Triggers>
          <Actions Context="Author">
            <Exec>
              <Command>{command}</Command>
              <Arguments>{arguments}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """.ReplaceLineEndings("\r\r\n");

    // ---- The task 0.1.0 registered: whose it is ----

    /// <summary>Not there: nothing to do, in any language.</summary>
    [Theory]
    [InlineData("ERROR: The system cannot find the file specified.")]
    [InlineData("ОШИБКА: Не удается найти указанный файл.")]
    public void A_legacy_task_that_is_not_there_is_nothing_to_do(string error) =>
        Assert.Equal(LegacyTask.Absent, WindowsTasks.Legacy(TaskNotFound, string.Empty, error, Ada));

    /// <summary>
    /// Access denied is another account's task: an ordinary prompt cannot read a task somebody else registered in
    /// the folder every account shares. It is never touched, and never reported as this account's registration.
    /// </summary>
    [Fact]
    public void A_legacy_task_this_prompt_may_not_read_is_somebody_else_s() =>
        Assert.Equal(LegacyTask.NotThisAccount, WindowsTasks.Legacy(AccessDenied, string.Empty, Denied, Ada));

    /// <summary>
    /// A task this prompt can read is this account's only when its principal is: by SID, or by the
    /// <c>MACHINE\name</c> the Task Scheduler may store instead, compared as Windows compares account names.
    /// </summary>
    [Theory]
    [InlineData(Sid)]
    [InlineData("s-1-5-21-1004336348-1177238915-682003330-1001")]
    [InlineData(@"ADA-PC\ada")]
    [InlineData(@"ada-pc\ADA")]
    public void A_legacy_task_whose_principal_is_this_account_is_this_account_s(string userId) =>
        Assert.Equal(LegacyTask.ThisAccount, WindowsTasks.Legacy(0, Stored(userId), string.Empty, Ada));

    /// <summary>
    /// And one whose principal is anybody else — or that names nobody, or cannot be read as a task at all — is
    /// not: a task this account cannot show is its own is left exactly where it is.
    /// </summary>
    [Theory]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1002")]
    [InlineData(@"ADA-PC\bob")]
    [InlineData(@"OTHER-PC\ada")]
    [InlineData("ada")]
    [InlineData("")]
    public void A_legacy_task_run_as_anybody_else_is_not_this_account_s(string userId) =>
        Assert.Equal(LegacyTask.NotThisAccount, WindowsTasks.Legacy(0, Stored(userId), string.Empty, Ada));

    /// <summary>
    /// The name alone does not make a task 0.1.0's. A person may have a task of their own called <c>Jason</c>, run as
    /// themselves; it is recognised as this product's only when it runs what 0.1.0 registered — the runtime, detached,
    /// on a data directory — through the executable or through the muxer. Anything else is somebody's namesake, and
    /// is left where it is.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Tools\notes.exe", "--sync")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "runtime run --data-dir C:\\Users\\ada\\.jason")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "runtime run --detached")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "runtime run --detached --data-dir C:\\data --verbose")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "runtime run --detached --verbose C:\\data")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "runtime run --detached --data-dir \"\"")]
    [InlineData(@"C:\Users\ada\AppData\Local\Programs\jason\jason.exe", "")]
    public void A_task_named_jason_that_runs_something_else_is_a_namesake_s(string command, string arguments) =>
        Assert.Equal(LegacyTask.NotThisAccount, WindowsTasks.Legacy(0, Stored(Sid, command, arguments), string.Empty, Ada));

    /// <summary>And 0.1.0's shape through the muxer, which a build run with <c>dotnet</c> registered, is 0.1.0's.</summary>
    [Fact]
    public void A_task_named_jason_that_runs_the_runtime_through_the_muxer_is_0_1_0_s() =>
        Assert.Equal(
            LegacyTask.ThisAccount,
            WindowsTasks.Legacy(0, Stored(Sid, @"C:\Program Files\dotnet\dotnet.exe", "C:\\src\\jason\\Jason.App.dll " + Runtime), string.Empty, Ada));

    [Theory]
    [InlineData("")]
    [InlineData("not a task at all")]
    [InlineData("<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings /></Task>")]
    public void A_legacy_task_that_names_no_principal_is_not_this_account_s(string document) =>
        Assert.Equal(LegacyTask.NotThisAccount, WindowsTasks.Legacy(0, document, string.Empty, Ada));

    /// <summary>Any other code is the tool refusing, with its own line: a scheduler that is not running is not "no task".</summary>
    [Fact]
    public void Any_other_code_from_the_legacy_query_is_a_refusal()
    {
        var refused = Assert.Throws<AutostartException>(
            () => WindowsTasks.Legacy(ServiceNotRunning, string.Empty, "ERROR: The Task Scheduler service is not running.", Ada));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Equal("schtasks refused: ERROR: The Task Scheduler service is not running.", refused.Message);
        Assert.Null(refused.Denial);
    }

    // ---- This account's own task: whether it is there ----

    [Theory]
    [InlineData(0, TaskPresence.Present)]
    [InlineData(TaskNotFound, TaskPresence.Absent)]
    [InlineData(AccessDenied, TaskPresence.Denied)]
    public void This_account_s_task_is_there_not_there_or_out_of_this_prompt_s_reach(int exit, TaskPresence expected) =>
        Assert.Equal(expected, WindowsTasks.Presence(exit, string.Empty, string.Empty));

    [Fact]
    public void Any_other_code_from_this_account_s_query_is_a_refusal()
    {
        var refused = Assert.Throws<AutostartException>(
            () => WindowsTasks.Presence(ServiceNotRunning, string.Empty, "ERROR: The Task Scheduler service is not running."));

        Assert.Equal("schtasks refused: ERROR: The Task Scheduler service is not running.", refused.Message);
    }

    // ---- What each denial says, to each verb: every branch, word for word ----

    private const string Nothing = "Nothing was registered or removed.";

    private const string Went =
        "The task named `Jason` that an earlier version registered for this account was removed; nothing else was registered or removed.";

    private const string AdministratorForLegacy =
        "On an administrator's own account, run `jason runtime autostart disable` from an elevated prompt once, which "
        + "removes it and this version's task alike.";

    private const string AdministratorForOwn =
        "On an administrator's own account, run `jason runtime autostart disable` from an elevated prompt once.";

    private const string ThenEnable = " Then run `jason runtime autostart enable` from an ordinary prompt.";
    private const string ThenUninstall = " Then run `jason uninstall` again.";
    private const string SaidDenied = " schtasks said: ERROR: Access is denied.";

    /// <summary>What a standard account is told instead: an elevated prompt there is another account.</summary>
    private static string Standard(string task) =>
        $" On a standard account an elevated prompt runs as somebody else, so ask an administrator to delete the task named `{task}` instead, and never run `jason` under another account.";

    private static string LegacyText(string then) =>
        "this account has a logon task named `Jason`, the name an earlier version of Jason registered its task under, and this "
        + $"prompt may not remove it. {Nothing} If an earlier version registered it from an elevated prompt, only an elevated "
        + $"prompt can remove it. {AdministratorForLegacy}{Standard(Legacy)}{then}{SaidDenied}";

    private static string OwnText(string state, string then) =>
        $"this account's logon task, `{Own}`, cannot be changed from this prompt. {state} If `{Own}` was registered from an "
        + $"elevated prompt, only an elevated prompt can remove it. {AdministratorForOwn}{Standard(Own)}{then}{SaidDenied}";

    private static string UnreadableText(string state, string then) =>
        $"the task named `{Own}` cannot be read from this prompt, so whose it is cannot be told from here. "
        + $"{state} If `{Own}` was registered from an elevated prompt, only an elevated prompt can remove it. "
        + $"{AdministratorForOwn}{Standard(Own)}{then}{SaidDenied}";

    private static string MachineText(string state) =>
        $"this machine's Task Scheduler refused to register a logon task for this account. {state} This account may not "
        + $"register logon tasks here, and `jason runtime start` still starts the runtime by hand.{SaidDenied}";

    public static TheoryData<AutostartAct, AutostartDenialKind, bool, string> Denials() => new()
    {
        { AutostartAct.Enable, AutostartDenialKind.Legacy, false, LegacyText(ThenEnable) },
        { AutostartAct.Disable, AutostartDenialKind.Legacy, false, LegacyText(string.Empty) },
        { AutostartAct.Uninstall, AutostartDenialKind.Legacy, false, LegacyText(ThenUninstall) },
        { AutostartAct.Enable, AutostartDenialKind.Own, false, OwnText(Nothing, ThenEnable) },
        { AutostartAct.Disable, AutostartDenialKind.Own, false, OwnText(Nothing, string.Empty) },
        { AutostartAct.Uninstall, AutostartDenialKind.Own, false, OwnText(Nothing, ThenUninstall) },
        { AutostartAct.Enable, AutostartDenialKind.Own, true, OwnText(Went, ThenEnable) },
        { AutostartAct.Disable, AutostartDenialKind.Own, true, OwnText(Went, string.Empty) },
        { AutostartAct.Uninstall, AutostartDenialKind.Own, true, OwnText(Went, ThenUninstall) },
        { AutostartAct.Enable, AutostartDenialKind.Unreadable, false, UnreadableText(Nothing, ThenEnable) },
        { AutostartAct.Disable, AutostartDenialKind.Unreadable, false, UnreadableText(Nothing, string.Empty) },
        { AutostartAct.Uninstall, AutostartDenialKind.Unreadable, false, UnreadableText(Nothing, ThenUninstall) },
        { AutostartAct.Enable, AutostartDenialKind.Unreadable, true, UnreadableText(Went, ThenEnable) },
        { AutostartAct.Disable, AutostartDenialKind.Unreadable, true, UnreadableText(Went, string.Empty) },
        { AutostartAct.Uninstall, AutostartDenialKind.Unreadable, true, UnreadableText(Went, ThenUninstall) },
        { AutostartAct.Enable, AutostartDenialKind.Machine, false, MachineText(Nothing) },
        { AutostartAct.Enable, AutostartDenialKind.Machine, true, MachineText(Went) },
    };

    /// <summary>
    /// Every denial says what happened, what did not, and the one thing a person can do about it, for the verb they
    /// ran: <c>enable</c> ends by enabling again, <c>disable</c> ends at the removal, and <c>jason uninstall</c> ends by
    /// running itself again — never with a second instruction that contradicts the first. The elevated prompt is named
    /// conditionally, because what is denied cannot say why; and it is named as an administrator's act on their own
    /// account, because on a standard account "Run as administrator" runs as the administrator, whose <c>disable</c>
    /// would take the administrator's own tasks away and leave this person's where they are.
    /// </summary>
    [Theory]
    [MemberData(nameof(Denials))]
    public void Every_denial_says_what_to_do_for_the_verb_that_met_it(
        AutostartAct act, AutostartDenialKind kind, bool legacyRemoved, string expected)
    {
        var task = kind is AutostartDenialKind.Legacy ? Legacy : Own;

        Assert.Equal(expected, WindowsTasks.Denied(act, new AutostartDenial(kind, task, legacyRemoved, Denied)));
    }

    /// <summary>
    /// A task this prompt could not read is not called this account's: all this prompt knows is its name. And the
    /// variant that already removed the task 0.1.0 left names the task it is about, never "it".
    /// </summary>
    [Fact]
    public void A_task_this_prompt_could_not_read_is_named_not_owned()
    {
        var unreadable = WindowsTasks.Denied(AutostartAct.Disable, new AutostartDenial(AutostartDenialKind.Unreadable, Own, true, Denied));

        Assert.DoesNotContain("this account's logon task,", unreadable, StringComparison.Ordinal);
        Assert.DoesNotContain("If it was registered", unreadable, StringComparison.Ordinal);
        Assert.StartsWith($"the task named `{Own}`", unreadable, StringComparison.Ordinal);
    }

    /// <summary>A tool that said nothing is not quoted as having said something.</summary>
    [Fact]
    public void A_denial_the_tool_gave_no_words_for_quotes_none() =>
        Assert.EndsWith(
            "and never run `jason` under another account.",
            WindowsTasks.Denied(AutostartAct.Disable, new AutostartDenial(AutostartDenialKind.Own, Own, false, string.Empty)),
            StringComparison.Ordinal);

    // ---- enable ----

    /// <summary>A clean machine: the old name is asked after, this account's name is asked after, and it is registered.</summary>
    [Fact]
    public void Enable_on_a_clean_machine_asks_twice_and_registers()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, TaskNotFound)
            .Answer("/Create", Own, 0);
        var documents = 0;

        WindowsTasks.Apply(tasks.Run, Registration, Ada, register => { documents++; register(); });

        Assert.Equal(["/Query Jason", $"/Query {Own}", $"/Create {Own}"], tasks.Asked);
        Assert.Equal(1, documents);
    }

    /// <summary>This account's 0.1.0 task is taken away before this version's is registered.</summary>
    [Fact]
    public void Enable_takes_this_account_s_legacy_task_away_first()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, TaskNotFound)
            .Answer("/Create", Own, 0);

        WindowsTasks.Apply(tasks.Run, Registration, Ada, register => register());

        Assert.Equal(["/Query Jason", "/Delete Jason", $"/Query {Own}", $"/Create {Own}"], tasks.Asked);

        // Deleted with /F: without it schtasks asks "(Y/N)?" on the standard input it inherited, and the verb hangs.
        Assert.Equal(AutostartArtifacts.TaskDelete(Legacy), tasks.Commands[1]);
    }

    /// <summary>Somebody else's task of that name is never touched, whether this prompt can read it or not.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(AccessDenied)]
    public void Enable_never_touches_another_account_s_legacy_task(int legacyExit)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, legacyExit, legacyExit == 0 ? Stored("S-1-5-21-1004336348-1177238915-682003330-1002") : string.Empty, legacyExit == 0 ? string.Empty : Denied)
            .Answer("/Query", Own, TaskNotFound)
            .Answer("/Create", Own, 0);

        WindowsTasks.Apply(tasks.Run, Registration, Ada, register => register());

        Assert.DoesNotContain("/Delete Jason", tasks.Asked);
        Assert.Contains($"/Create {Own}", tasks.Asked);
    }

    /// <summary>
    /// A legacy task this prompt may not remove stops <c>enable</c> before anything is registered: two tasks for
    /// one account would start two runtimes at every logon, one of them in a session with no desktop.
    /// </summary>
    [Fact]
    public void Enable_registers_nothing_when_the_legacy_task_cannot_be_removed()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, AccessDenied, error: Denied);
        var documents = 0;

        var refused = Assert.Throws<AutostartException>(
            () => WindowsTasks.Apply(tasks.Run, Registration, Ada, register => { documents++; register(); }));

        Assert.Equal(["/Query Jason", "/Delete Jason"], tasks.Asked);
        Assert.Equal(0, documents);
        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Equal(new AutostartDenial(AutostartDenialKind.Legacy, Legacy, false, Denied), refused.Denial);
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Enable, refused.Denial!), refused.Message);
    }

    /// <summary>This account's own task out of this prompt's reach: nothing is written, nothing is registered.</summary>
    [Fact]
    public void Enable_stops_at_this_account_s_task_when_this_prompt_cannot_read_it()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, AccessDenied, error: Denied);
        var documents = 0;

        var refused = Assert.Throws<AutostartException>(
            () => WindowsTasks.Apply(tasks.Run, Registration, Ada, register => { documents++; register(); }));

        Assert.Equal(0, documents);
        Assert.DoesNotContain($"/Create {Own}", tasks.Asked);
        Assert.Equal(new AutostartDenial(AutostartDenialKind.Unreadable, Own, false, Denied), refused.Denial);
    }

    /// <summary>
    /// A denied <c>/Create</c> means one of two things, and what was asked first tells them apart: over a task
    /// this account already has, it is that task's; over nothing, it is the machine.
    /// </summary>
    [Theory]
    [InlineData(0, AutostartDenialKind.Own)]
    [InlineData(TaskNotFound, AutostartDenialKind.Machine)]
    public void A_denied_registration_is_the_task_s_or_the_machine_s(int ownExit, AutostartDenialKind expected)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, ownExit)
            .Answer("/Create", Own, AccessDenied, error: Denied);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Apply(tasks.Run, Registration, Ada, register => register()));

        Assert.Equal(new AutostartDenial(expected, Own, false, Denied), refused.Denial);
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Enable, refused.Denial!), refused.Message);
    }

    /// <summary>And a legacy task removed before a refusal is said to have been removed, because it was.</summary>
    [Fact]
    public void A_refusal_after_the_legacy_task_went_says_that_it_went()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(@"ADA-PC\ada"))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, TaskNotFound)
            .Answer("/Create", Own, AccessDenied, error: Denied);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Apply(tasks.Run, Registration, Ada, register => register()));

        Assert.Equal(new AutostartDenial(AutostartDenialKind.Machine, Own, true, Denied), refused.Denial);
        Assert.DoesNotContain("Nothing was registered or removed", refused.Message, StringComparison.Ordinal);

        // Said exactly once: the denial's own words, and no "Before this, ..." added after them.
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Enable, refused.Denial!), refused.Message);
    }

    /// <summary>
    /// "There is no such task" from the command that registers is not "done": there was nothing for it to find
    /// missing. It and every other code stay what they were — the tool's own line, and no advice about prompts.
    /// </summary>
    [Theory]
    [InlineData(TaskNotFound, "ERROR: The system cannot find the file specified.")]
    [InlineData(1, "anything at all")]
    [InlineData(unchecked((int)0x8007000E), "ОШИБКА: Недостаточно памяти.")]
    public void Any_other_code_from_the_registration_carries_the_tool_s_line(int exit, string error)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, TaskNotFound)
            .Answer("/Create", Own, exit, error: error);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Apply(tasks.Run, Registration, Ada, register => register()));

        Assert.Equal($"schtasks refused: {error}", refused.Message);
        Assert.Null(refused.Denial);
    }

    /// <summary>
    /// Once this account's 0.1.0 task is gone, whatever fails afterwards says that it went. Only the denials said so;
    /// a scheduler that stopped answering, a registration refused for another reason or a document that could not be
    /// written left a person believing nothing had changed.
    /// </summary>
    [Theory]
    [InlineData("own-query")]
    [InlineData("create")]
    [InlineData("document")]
    public void Enable_says_the_legacy_task_went_whatever_fails_after(string failing)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, failing == "own-query" ? ServiceNotRunning : TaskNotFound, error: failing == "own-query" ? "ERROR: The Task Scheduler service is not running." : string.Empty)
            .Answer("/Create", Own, failing == "create" ? 1 : 0, error: failing == "create" ? "ERROR: something else." : string.Empty);
        Action<Action> withDocument = failing == "document"
            ? _ => throw new AutostartException(AutostartCodes.Refused, @"'C:\data\autostart\jason-task.xml' could not be written: The disk is full.")
            : register => register();

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Apply(tasks.Run, Registration, Ada, withDocument));

        Assert.True(refused.LegacyRemoved);
        Assert.Null(refused.Denial);
        Assert.EndsWith(
            " Before this, the task named `Jason` that an earlier version registered for this account was removed.",
            refused.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("own-query")]
    [InlineData("delete")]
    public void Disable_says_the_legacy_task_went_whatever_fails_after(string failing)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, failing == "own-query" ? ServiceNotRunning : 0, error: failing == "own-query" ? "ERROR: The Task Scheduler service is not running." : string.Empty)
            .Answer("/Delete", Own, 1, error: "ERROR: something else.");

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Remove(tasks.Run, Registration, Ada));

        Assert.True(refused.LegacyRemoved);
        Assert.StartsWith("schtasks refused: ERROR: ", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            " Before this, the task named `Jason` that an earlier version registered for this account was removed.",
            refused.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A denial carries the same fact, for <c>jason uninstall</c> to report; one with no legacy removal does not.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_denial_carries_whether_the_legacy_task_went(bool legacyThere)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, legacyThere ? 0 : TaskNotFound, legacyThere ? Stored(Sid) : string.Empty)
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, AccessDenied, error: Denied);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Remove(tasks.Run, Registration, Ada));

        Assert.Equal(legacyThere, refused.LegacyRemoved);
        Assert.Equal(legacyThere, refused.Denial!.LegacyRemoved);

        // And says it exactly once, in the denial's own words.
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Disable, refused.Denial), refused.Message);
    }

    // ---- disable, and the first step of jason uninstall ----

    /// <summary>Nothing registered: two questions, nothing removed, and not an error.</summary>
    [Fact]
    public void Disable_on_a_clean_machine_removes_nothing()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, TaskNotFound);

        var removed = WindowsTasks.Remove(tasks.Run, Registration, Ada);

        Assert.Equal(["/Query Jason", $"/Query {Own}"], tasks.Asked);
        Assert.Equal(new AutostartRemoval(Registration: false, Legacy: false), removed);
    }

    /// <summary>Both of this account's tasks go, the old one first.</summary>
    [Fact]
    public void Disable_removes_this_account_s_legacy_task_and_its_own()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, 0)
            .Answer("/Delete", Own, 0);

        var removed = WindowsTasks.Remove(tasks.Run, Registration, Ada);

        Assert.Equal(["/Query Jason", "/Delete Jason", $"/Query {Own}", $"/Delete {Own}"], tasks.Asked);
        Assert.Equal(new AutostartRemoval(Registration: true, Legacy: true), removed);
    }

    /// <summary>
    /// An account that upgraded from 0.1.0 and never ran this version's <c>enable</c> has only the old task. Taking
    /// it away is said, because whoever reads the report is owed the one thing that was removed.
    /// </summary>
    [Fact]
    public void Removing_only_the_legacy_task_says_so()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, 0)
            .Answer("/Query", Own, TaskNotFound);

        Assert.Equal(new AutostartRemoval(Registration: false, Legacy: true), WindowsTasks.Remove(tasks.Run, Registration, Ada));
    }

    /// <summary>
    /// Somebody else's task of the old name is left where it is. An elevated <c>disable</c> used to delete it by
    /// name, which on a shared machine was the one thing this verb must never do.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(AccessDenied)]
    public void Disable_never_touches_another_account_s_legacy_task(int legacyExit)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, legacyExit, legacyExit == 0 ? Stored(@"ADA-PC\bob") : string.Empty, legacyExit == 0 ? string.Empty : Denied)
            .Answer("/Query", Own, 0)
            .Answer("/Delete", Own, 0);

        WindowsTasks.Remove(tasks.Run, Registration, Ada);

        Assert.DoesNotContain("/Delete Jason", tasks.Asked);
        Assert.Contains($"/Delete {Own}", tasks.Asked);
    }

    /// <summary>A task somebody removed by hand between the question and the removal is removed.</summary>
    [Fact]
    public void A_task_gone_between_the_question_and_the_removal_is_done()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, 0)
            .Answer("/Delete", Own, TaskNotFound, error: "ERROR: The system cannot find the file specified.");

        // Done, and not by this command: somebody else took it away first.
        Assert.Equal(new AutostartRemoval(Registration: false, Legacy: false), WindowsTasks.Remove(tasks.Run, Registration, Ada));
    }

    /// <summary>A legacy task this prompt may not remove stops the verb before this account's own task is touched.</summary>
    [Fact]
    public void Disable_removes_nothing_when_the_legacy_task_cannot_be_removed()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, 0, Stored(Sid))
            .Answer("/Delete", Legacy, AccessDenied, error: Denied);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Remove(tasks.Run, Registration, Ada));

        Assert.Equal(["/Query Jason", "/Delete Jason"], tasks.Asked);
        Assert.Equal(new AutostartDenial(AutostartDenialKind.Legacy, Legacy, false, Denied), refused.Denial);
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Disable, refused.Denial!), refused.Message);
    }

    /// <summary>This account's own task out of this prompt's reach, whether at the question or at the removal.</summary>
    [Theory]
    [InlineData(AccessDenied, 0, AutostartDenialKind.Unreadable)]
    [InlineData(0, AccessDenied, AutostartDenialKind.Own)]
    public void Disable_says_whose_task_it_could_not_remove(int queryExit, int deleteExit, AutostartDenialKind expected)
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, TaskNotFound)
            .Answer("/Query", Own, queryExit, error: queryExit == 0 ? string.Empty : Denied)
            .Answer("/Delete", Own, deleteExit, error: deleteExit == 0 ? string.Empty : Denied);

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Remove(tasks.Run, Registration, Ada));

        Assert.Equal(new AutostartDenial(expected, Own, false, Denied), refused.Denial);
        Assert.Equal(WindowsTasks.Denied(AutostartAct.Disable, refused.Denial!), refused.Message);
    }

    /// <summary>A scheduler that will not answer about the old name is a refusal, and nothing further is asked.</summary>
    [Fact]
    public void A_legacy_question_the_scheduler_will_not_answer_stops_the_verb()
    {
        var tasks = new Scripted()
            .Answer("/Query", Legacy, ServiceNotRunning, error: "ERROR: The Task Scheduler service is not running.");

        var refused = Assert.Throws<AutostartException>(() => WindowsTasks.Remove(tasks.Run, Registration, Ada));

        Assert.Equal(["/Query Jason"], tasks.Asked);
        Assert.Null(refused.Denial);
    }

    /// <summary>
    /// The Task Scheduler, played by a script: one answer per command and task name, every command recorded, and
    /// every command held to asking for a code rather than a sentence.
    /// </summary>
    private sealed class Scripted
    {
        private readonly Dictionary<string, (int Exit, string Output, string Error)> answers = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        /// <summary>Every command as it was run, word for word.</summary>
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Scripted Answer(string verb, string task, int exit, string output = "", string error = "")
        {
            answers[$"{verb} {task}"] = (exit, output, error);
            return this;
        }

        public (int Exit, string Output, string Error) Run(IReadOnlyList<string> command)
        {
            Assert.Equal("schtasks", command[0]);
            Assert.Contains("/HRESULT", command);

            var words = command.ToList();
            var asked = $"{command[1]} {words[words.IndexOf("/TN") + 1]}";
            Asked.Add(asked);
            Commands.Add(words);

            return answers.TryGetValue(asked, out var answer)
                ? answer
                : throw new InvalidOperationException($"Nothing was scripted for '{asked}'.");
        }
    }
}
