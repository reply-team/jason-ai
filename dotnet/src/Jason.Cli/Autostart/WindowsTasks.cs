using System.Xml;
using System.Xml.Linq;

namespace Jason.Cli.Autostart;

/// <summary>Runs one <c>schtasks</c> command and says what it said.</summary>
public delegate (int Exit, string Output, string Error) Schtasks(IReadOnlyList<string> command);

/// <summary>This account as the Task Scheduler may write it in a task: its SID, or its <c>MACHINE\name</c>.</summary>
/// <param name="Sid">The account's SID string, which is what this product writes and what the task is named for.</param>
/// <param name="Name">The account's name with its domain or machine in front, which the Task Scheduler may store instead.</param>
public sealed record WindowsAccount(string Sid, string Name);

/// <summary>Which verb met a denial, because each one ends its advice differently.</summary>
public enum AutostartAct
{
    /// <summary><c>jason runtime autostart enable</c>.</summary>
    Enable,

    /// <summary><c>jason runtime autostart disable</c>.</summary>
    Disable,

    /// <summary>The first step of <c>jason uninstall</c>, which takes the registration away as <c>disable</c> does.</summary>
    Uninstall,
}

/// <summary>Which task this prompt was not allowed to touch.</summary>
public enum AutostartDenialKind
{
    /// <summary>This account's task under the name 0.1.0 registered, <see cref="AutostartArtifacts.LegacyTaskName"/>.</summary>
    Legacy,

    /// <summary>This account's own task, <c>Jason-&lt;SID&gt;</c>, which this prompt could read and may not change.</summary>
    Own,

    /// <summary>
    /// A task under this account's name, <c>Jason-&lt;SID&gt;</c>, that this prompt may not even read: all it knows is
    /// the name, so it is never called this account's.
    /// </summary>
    Unreadable,

    /// <summary>None: the Task Scheduler refused to register this account's task where there was none.</summary>
    Machine,
}

/// <summary>
/// A refusal a person can act on, as data: which case it is, the task it is about, whether this same command had
/// already removed this account's legacy task, and the tool's own line. <c>jason uninstall</c> reads it to say the
/// same thing in its own terms.
/// </summary>
public sealed record AutostartDenial(AutostartDenialKind Kind, string Task, bool LegacyRemoved, string Said);

/// <summary>What the question about the name 0.1.0 registered came back with.</summary>
public enum LegacyTask
{
    /// <summary>There is no task of that name.</summary>
    Absent,

    /// <summary>There may be one, and it is not this account's: another account's, or one this prompt cannot read.</summary>
    NotThisAccount,

    /// <summary>There is one, and its principal is this account.</summary>
    ThisAccount,
}

/// <summary>Whether this account's own task is there.</summary>
public enum TaskPresence
{
    /// <summary>It is, and this prompt could read it.</summary>
    Present,

    /// <summary>It is not.</summary>
    Absent,

    /// <summary>A task of that name is there and this prompt may not read it.</summary>
    Denied,
}

/// <summary>
/// The Windows registration, one <c>schtasks</c> command at a time, with the thing that runs <c>schtasks</c> handed
/// in. A test hands in a script, so every order and every answer below is asserted without a machine; the
/// registrar hands in the real one. Nothing here writes a file or starts a process of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two names.</b> This account's task is <c>Jason-&lt;SID&gt;</c>. 0.1.0 registered <c>Jason</c>, in the
/// Task Scheduler's root folder, which every account on the machine shares: a task of that name may be this
/// account's or somebody else's. <c>enable</c>, <c>disable</c> and <c>jason uninstall</c> ask after it once and
/// take it away only where its principal is this account. Another account's task answers an ordinary prompt with
/// access denied — measured on Windows 11 24H2 — and is never touched; before this, an elevated <c>disable</c>
/// deleted whatever was called <c>Jason</c>.
/// </para>
/// <para>
/// <b>The old name first.</b> A legacy task this prompt may not remove stops the verb before anything else
/// happens, so "Nothing was registered or removed" stays true, and <c>enable</c> does not register this version's
/// task beside a 0.1.0 task it has recognised as this account's. Recognised means its principal names this account
/// by SID, as 0.1.0 wrote it, or by <c>MACHINE\name</c>; a principal stored in any other form is not recognised
/// and is left where it is, which is the safe direction for a task that may be somebody else's.
/// </para>
/// <para>
/// <b>Denials name a case, conditionally.</b> An ordinary prompt registers this version's task on a standard
/// account — measured on a real machine — so access denied is no longer what everybody meets. What it can still
/// meet is a task registered from an elevated prompt, which only an elevated prompt may take away, and a machine
/// whose policy forbids registering logon tasks. Nothing here can see why a request was denied, so every denial
/// says what to do <i>if</i> it is the first, and never decides by the tool's sentence, which it only repeats.
/// </para>
/// </remarks>
public static class WindowsTasks
{
    /// <summary>ERROR_FILE_NOT_FOUND as an HRESULT: the Task Scheduler's "there is no such task".</summary>
    private const int TaskNotFound = unchecked((int)0x80070002);

    /// <summary>ERROR_ACCESS_DENIED as an HRESULT: the Task Scheduler's "not from this prompt".</summary>
    private const int AccessDenied = unchecked((int)0x80070005);

    /// <summary>Registers this account's task, after taking this account's 0.1.0 task away.</summary>
    /// <param name="run">What runs <c>schtasks</c>.</param>
    /// <param name="registration">This account's registration, composed by <see cref="AutostartArtifacts.Compose"/>.</param>
    /// <param name="account">This account, as a task's principal may name it.</param>
    /// <param name="withDocument">
    /// Puts the task document where <c>/Create</c> reads it, runs what it is given, and puts things back if that
    /// refuses: <see cref="AutostartRegistrars.WriteThen"/>, for the registrar.
    /// </param>
    public static void Apply(Schtasks run, AutostartRegistration registration, WindowsAccount account, Action<Action> withDocument)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(withDocument);

        var legacyRemoved = RemoveLegacy(run, account, AutostartAct.Enable);
        try
        {
            Register(run, registration, withDocument, legacyRemoved);
        }
        catch (AutostartException failure) when (legacyRemoved && !failure.LegacyRemoved)
        {
            throw AfterTheLegacyTask(failure);
        }
    }

    /// <summary>What <see cref="Apply"/> does once the task 0.1.0 left has been dealt with.</summary>
    private static void Register(Schtasks run, AutostartRegistration registration, Action<Action> withDocument, bool legacyRemoved)
    {
        // Asked first so that a denied /Create can be told apart: over a task this account already has, the
        // task is out of this prompt's reach; over nothing, the machine would not register one.
        var (queried, output, error) = run(registration.Query);
        var presence = Presence(queried, output, error);
        if (presence is TaskPresence.Denied)
        {
            throw Refusal(AutostartAct.Enable, new(AutostartDenialKind.Unreadable, registration.Name, legacyRemoved, AutostartRegistrars.Said(error, output)));
        }

        withDocument(() =>
        {
            foreach (var command in registration.Apply)
            {
                var (exit, said, why) = run(command);
                if (exit == 0)
                {
                    continue;
                }

                if (exit == AccessDenied)
                {
                    var kind = presence is TaskPresence.Present ? AutostartDenialKind.Own : AutostartDenialKind.Machine;
                    throw Refusal(AutostartAct.Enable, new(kind, registration.Name, legacyRemoved, AutostartRegistrars.Said(why, said)));
                }

                // "There is no such task" from the command that registers is not "done": there was nothing for it
                // to find missing.
                throw AutostartRegistrars.Refused("schtasks", why, said);
            }
        });
    }

    /// <summary>
    /// Takes this account's tasks away: the 0.1.0 one, then its own. Removing what is not there is not an error.
    /// <c>jason uninstall</c> runs this as its first step and says a denial in its own terms.
    /// </summary>
    /// <returns>What this call removed, which only it can know for the 0.1.0 task.</returns>
    public static AutostartRemoval Remove(Schtasks run, AutostartRegistration registration, WindowsAccount account)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(registration);

        var legacyRemoved = RemoveLegacy(run, account, AutostartAct.Disable);
        try
        {
            return Unregister(run, registration, legacyRemoved);
        }
        catch (AutostartException failure) when (legacyRemoved && !failure.LegacyRemoved)
        {
            throw AfterTheLegacyTask(failure);
        }
    }

    /// <summary>What <see cref="Remove"/> does once the task 0.1.0 left has been dealt with.</summary>
    private static AutostartRemoval Unregister(Schtasks run, AutostartRegistration registration, bool legacyRemoved)
    {
        // The question decides whether anything is run at all, and the removal says "not there" a second time
        // for the task somebody removed by hand between the two.
        var (queried, output, error) = run(registration.Query);
        switch (Presence(queried, output, error))
        {
            case TaskPresence.Absent:
                return new AutostartRemoval(Registration: false, Legacy: legacyRemoved);
            case TaskPresence.Denied:
                throw Refusal(AutostartAct.Disable, new(AutostartDenialKind.Unreadable, registration.Name, legacyRemoved, AutostartRegistrars.Said(error, output)));
        }

        var removed = true;
        foreach (var command in registration.Remove)
        {
            var (exit, said, why) = run(command);
            if (exit == 0)
            {
                continue;
            }

            if (exit == TaskNotFound)
            {
                // Somebody took it away between the question and this: done, and not by this call.
                removed = false;
                continue;
            }

            throw exit == AccessDenied
                ? Refusal(AutostartAct.Disable, new(AutostartDenialKind.Own, registration.Name, legacyRemoved, AutostartRegistrars.Said(why, said)))
                : AutostartRegistrars.Refused("schtasks", why, said);
        }

        return new AutostartRemoval(removed, legacyRemoved);
    }

    /// <summary>
    /// Whose task the name 0.1.0 registered is, from what the question about it came back with.
    /// </summary>
    /// <remarks>
    /// Denied is somebody else's: an ordinary prompt cannot read another account's task. A task that can be read is
    /// this account's 0.1.0 task only where its principal names this account — by SID, or by the <c>MACHINE\name</c>
    /// the Task Scheduler may store instead, compared as Windows compares account names — <b>and</b> it runs what 0.1.0
    /// registered (<see cref="AutostartArtifacts.RunsTheRuntime"/>). The name alone is not enough: a person may have a
    /// task of their own called <c>Jason</c>. Anything that cannot be shown to be 0.1.0's for this account, a document
    /// that names nobody or cannot be read included, is left exactly where it is.
    /// </remarks>
    public static LegacyTask Legacy(int exit, string output, string error, WindowsAccount account)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(account);

        return exit switch
        {
            TaskNotFound => LegacyTask.Absent,
            AccessDenied => LegacyTask.NotThisAccount,
            0 => Principals(output).Any(userId => Names(userId, account)) && RunsTheRuntime(output) ? LegacyTask.ThisAccount : LegacyTask.NotThisAccount,
            _ => throw AutostartRegistrars.Refused("schtasks", error, output),
        };
    }

    /// <summary>Whether this account's own task is there, from what the question about it came back with.</summary>
    public static TaskPresence Presence(int exit, string output, string error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return exit switch
        {
            0 => TaskPresence.Present,
            TaskNotFound => TaskPresence.Absent,
            AccessDenied => TaskPresence.Denied,
            _ => throw AutostartRegistrars.Refused("schtasks", error, output),
        };
    }

    /// <summary>
    /// The whole of what a denial says to the verb that met it: what was refused, what did and did not happen, the
    /// one thing a person can do if the likely case is theirs, and the tool's own line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The advice ends where the verb does. <c>enable</c> ends by enabling again from an ordinary prompt;
    /// <c>disable</c> ends at the removal, which is all it wanted; <c>jason uninstall</c> ends by running itself again.
    /// </para>
    /// <para>
    /// The elevated <c>disable</c> is an administrator's act on their own account: there it runs as the same account,
    /// so it takes both of that account's tasks away. On a standard account "Run as administrator" runs as the
    /// administrator whose password is typed, and a <c>disable</c> there would take the administrator's own tasks away
    /// and leave this person's where they are. So a standard account is told to ask an administrator to delete the
    /// named task, and never to run <c>jason</c> under another account.
    /// </para>
    /// </remarks>
    public static string Denied(AutostartAct act, AutostartDenial denial)
    {
        ArgumentNullException.ThrowIfNull(denial);

        var state = denial.LegacyRemoved
            ? $"The task named `{AutostartArtifacts.LegacyTaskName}` that an earlier version registered for this account was removed; nothing else was registered or removed."
            : "Nothing was registered or removed.";
        var then = act switch
        {
            AutostartAct.Enable => " Then run `jason runtime autostart enable` from an ordinary prompt.",
            AutostartAct.Uninstall => " Then run `jason uninstall` again.",
            _ => string.Empty,
        };
        var said = denial.Said.Length == 0 ? string.Empty : $" schtasks said: {denial.Said}";
        var administrator = "On an administrator's own account, run `jason runtime autostart disable` from an elevated prompt once"
            + (denial.Kind is AutostartDenialKind.Legacy ? ", which removes it and this version's task alike." : ".");
        var standard = " On a standard account an elevated prompt runs as somebody else, so ask an administrator to delete the "
            + $"task named `{denial.Task}` instead, and never run `jason` under another account.";

        return denial.Kind switch
        {
            AutostartDenialKind.Legacy =>
                $"this account has a logon task named `{denial.Task}`, the name an earlier version of Jason registered its task "
                + $"under, and this prompt may not remove it. {state} If an earlier version registered it from an elevated "
                + $"prompt, only an elevated prompt can remove it. {administrator}{standard}{then}{said}",
            AutostartDenialKind.Own =>
                $"this account's logon task, `{denial.Task}`, cannot be changed from this prompt. {state} If `{denial.Task}` was "
                + $"registered from an elevated prompt, only an elevated prompt can remove it. {administrator}{standard}{then}{said}",
            AutostartDenialKind.Unreadable =>
                $"the task named `{denial.Task}` cannot be read from this prompt, so whose it is cannot be told from here. "
                + $"{state} If `{denial.Task}` was registered from an elevated prompt, only an elevated prompt can remove it. "
                + $"{administrator}{standard}{then}{said}",
            _ =>
                $"this machine's Task Scheduler refused to register a logon task for this account. {state} This account may "
                + "not register logon tasks here, and `jason runtime start` still starts the runtime by hand." + said,
        };
    }

    /// <summary>The denial as the refusal a verb raises, carrying the case so another verb can say it its own way.</summary>
    private static AutostartException Refusal(AutostartAct act, AutostartDenial denial) =>
        new(AutostartCodes.Refused, Denied(act, denial)) { Denial = denial, LegacyRemoved = denial.LegacyRemoved };

    /// <summary>
    /// Any other failure after this account's 0.1.0 task was taken away, saying that it was. The failure's own words
    /// come first; a person reading only them would believe nothing had changed.
    /// </summary>
    private static AutostartException AfterTheLegacyTask(AutostartException failure) =>
        new(failure.Code, $"{failure.Message} Before this, the task named `{AutostartArtifacts.LegacyTaskName}` that an earlier version registered for this account was removed.", failure)
        {
            LegacyRemoved = true,
        };

    /// <summary>Asks after the name 0.1.0 registered, and takes this account's task of that name away.</summary>
    /// <returns>Whether a task was taken away.</returns>
    private static bool RemoveLegacy(Schtasks run, WindowsAccount account, AutostartAct act)
    {
        var (queried, output, error) = run(AutostartArtifacts.TaskQuery(AutostartArtifacts.LegacyTaskName));
        if (Legacy(queried, output, error, account) is not LegacyTask.ThisAccount)
        {
            return false;
        }

        var (exit, said, why) = run(AutostartArtifacts.TaskDelete(AutostartArtifacts.LegacyTaskName));
        return exit switch
        {
            0 => true,
            TaskNotFound => false,
            AccessDenied => throw Refusal(act, new(AutostartDenialKind.Legacy, AutostartArtifacts.LegacyTaskName, false, AutostartRegistrars.Said(why, said))),
            _ => throw AutostartRegistrars.Refused("schtasks", why, said),
        };
    }

    /// <summary>Every principal's account a task document names, or none where it names none or is not one.</summary>
    private static IEnumerable<string> Principals(string document)
    {
        try
        {
            return XDocument.Parse(document)
                .Descendants(AutostartArtifacts.TaskSchema + "Principal")
                .Select(principal => principal.Element(AutostartArtifacts.TaskSchema + "UserId")?.Value.Trim() ?? string.Empty)
                .Where(userId => userId.Length > 0)
                .ToList();
        }
        catch (XmlException)
        {
            return [];
        }
    }

    /// <summary>Whether a task document runs what this product registers; a document that cannot be read does not.</summary>
    private static bool RunsTheRuntime(string document)
    {
        try
        {
            return AutostartArtifacts.RunsTheRuntime(AutostartArtifacts.Read(AutostartPlatform.Windows, document));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Whether a principal names this account. Windows compares SIDs and account names without regard to case.</summary>
    private static bool Names(string userId, WindowsAccount account) =>
        string.Equals(userId, account.Sid, StringComparison.OrdinalIgnoreCase)
        || string.Equals(userId, account.Name, StringComparison.OrdinalIgnoreCase);
}
