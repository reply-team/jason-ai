using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Jason.Cli.Autostart;

/// <summary>
/// What each platform's autostart registration is made of, composed from its parts and read back out of it.
/// Both directions are pure functions of what they are given: nothing here reads the environment, touches a
/// file or starts a process, which is what lets every artifact be asserted on any machine.
/// </summary>
/// <remarks>
/// The registered command line is the runtime itself — <c>runtime run --detached --data-dir &lt;root&gt;</c> —
/// and never something that starts one. <c>--detached</c> is in it on all three platforms because the
/// registered runtime is a background service everywhere: no console, standard streams on the null device, its
/// own session on Unix. The data directory is in it because a registration made under a <c>JASON_DATA_DIR</c>
/// must keep it: the alternative is a background runtime on <c>~/.jason</c> and two runtimes disagreeing about
/// which data is real.
/// </remarks>
public static class AutostartArtifacts
{
    /// <summary>The Windows task's name, under the Task Scheduler's root folder.</summary>
    public const string TaskName = "Jason";

    /// <summary>The LaunchAgent's label, which is also its file name.</summary>
    public const string Label = "ai.jason.runtime";

    /// <summary>The systemd user unit's name.</summary>
    public const string UnitName = "jason.service";

    /// <summary>Where a Windows task document is written before <c>schtasks</c> is asked to register it.</summary>
    public const string TaskDocument = "jason-task.xml";

    private static readonly XNamespace TaskSchema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    /// The registration this installation would make.
    /// </summary>
    /// <param name="platform">Which of the three shapes to compose.</param>
    /// <param name="run">How to run this program: the executable, or the muxer and this build's assembly.</param>
    /// <param name="dataDirectory">The data directory the registered runtime owns, absolute.</param>
    /// <param name="home">The account's home directory, which is where two of the three keep their document.</param>
    /// <param name="account">
    /// Who the registration runs as. On Windows this is the account's <b>SID</b> rather than its name: a
    /// Microsoft account's display name is not what the Task Scheduler stores, and a name with a domain in it
    /// is not what it compares. The other two platforms do not need it, because a file under an account's own
    /// home directory says whose it is by being there.
    /// </param>
    public static AutostartRegistration Compose(
        AutostartPlatform platform,
        IReadOnlyList<string> run,
        string dataDirectory,
        string home,
        string account)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        if (run.Count == 0)
        {
            throw new ArgumentException("There is no program to register.", nameof(run));
        }

        var line = Line(run, dataDirectory);
        return platform switch
        {
            AutostartPlatform.Windows => Windows(line, dataDirectory, account),
            AutostartPlatform.MacOs => MacOs(line, home),
            AutostartPlatform.Linux => Linux(line, home),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "There is nothing to compose for a platform that cannot register anything."),
        };
    }

    /// <summary>
    /// The command that asks a machine whether anything is registered. It depends on the platform and on
    /// nothing else — not on the data directory, not on the account — which is what lets a registrar ask
    /// before it knows either: what it is about to read may have been registered by another installation.
    /// </summary>
    /// <remarks>
    /// <c>/HRESULT</c> is not decoration. Without it <c>schtasks</c> exits 1 for a task that is not there and 1
    /// for a task this account may not read, and the only thing telling those apart is the sentence it prints —
    /// in the language Windows is installed in. With it, a missing task exits <c>0x80070002</c> and access
    /// denied exits <c>0x80070005</c>, in every language, and the words are never consulted again. The two
    /// commands that act ask for it too, for the same reason and one found the hard way: see
    /// <see cref="AutostartRegistrars.Acted"/>.
    /// </remarks>
    public static IReadOnlyList<string> QueryFor(AutostartPlatform platform) => platform switch
    {
        AutostartPlatform.Windows => ["schtasks", "/Query", "/TN", TaskName, "/XML", "ONE", "/HRESULT"],
        AutostartPlatform.MacOs => ["launchctl", "list", Label],
        AutostartPlatform.Linux => ["systemctl", "--user", "is-enabled", UnitName],
        _ => [],
    };

    /// <summary>
    /// Where a platform keeps the document that is its registration. One place, because a registrar has to find
    /// it without composing a whole registration first: what it is reading is what somebody else registered,
    /// possibly under a data directory this installation knows nothing about.
    /// </summary>
    public static string ArtifactPath(AutostartPlatform platform, string home, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        return platform switch
        {
            AutostartPlatform.Windows => Path.Combine(dataDirectory, "autostart", TaskDocument),
            AutostartPlatform.MacOs => Path.Combine(home, "Library", "LaunchAgents", Label + ".plist"),
            AutostartPlatform.Linux => Path.Combine(home, ".config", "systemd", "user", UnitName),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "A platform that registers nothing keeps no document."),
        };
    }

    /// <summary>The command line a registration names, read back out of the document a machine holds.</summary>
    public static IReadOnlyList<string> Read(AutostartPlatform platform, string artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return platform switch
        {
            AutostartPlatform.Windows => FromTask(artifact),
            AutostartPlatform.MacOs => FromPlist(artifact),
            AutostartPlatform.Linux => FromUnit(artifact),
            _ => [],
        };
    }

    /// <summary>What gets registered, whatever the platform: this program, told to be the runtime.</summary>
    private static IReadOnlyList<string> Line(IReadOnlyList<string> run, string dataDirectory) =>
        [.. run, "runtime", "run", "--detached", "--data-dir", dataDirectory];

    /// <summary>
    /// A logon task, as the document <c>schtasks /Create /XML</c> reads. The document rather than the command
    /// line: <c>/TR</c> carries the whole invocation as one string with a length limit and no way to say what
    /// kind of logon this is, and the two things this task must say are exactly those.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>S4U</c> is the logon type, and it is the whole of the no-window guarantee. A console application
    /// started by a task in the interactive session gets a console window on the desktop at every logon, and
    /// <c>--detached</c> does not close it: the detacher points the standard handles at the null device, and it
    /// never frees a console because nothing that starts a runtime has ever given it one. An S4U logon has no
    /// desktop to put a window on.
    /// </para>
    /// <para>
    /// <c>ExecutionTimeLimit</c> is <c>PT0S</c>, which means none. The Task Scheduler's own default is three
    /// days, after which it stops the task — and a runtime that is stopped after three days without anybody
    /// asking would be the strangest bug this product could ship.
    /// </para>
    /// </remarks>
    private static AutostartRegistration Windows(IReadOnlyList<string> line, string dataDirectory, string account)
    {
        var document = ArtifactPath(AutostartPlatform.Windows, string.Empty, dataDirectory);
        var xml = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="{TaskSchema}">
              <RegistrationInfo>
                <Description>Starts the Jason runtime when this account logs on. Nothing supervises it: a runtime that stops stays stopped until somebody starts it.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Xml(account)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Xml(account)}</UserId>
                  <LogonType>S4U</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Xml(line[0])}</Command>
                  <Arguments>{Xml(Join(line.Skip(1)))}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """);

        return new AutostartRegistration(
            AutostartPlatform.Windows,
            TaskName,
            document,
            xml,
            [["schtasks", "/Create", "/XML", document, "/TN", TaskName, "/F", "/HRESULT"]],
            [["schtasks", "/Delete", "/TN", TaskName, "/F", "/HRESULT"]],
            QueryFor(AutostartPlatform.Windows),
            line);
    }

    /// <summary>
    /// A LaunchAgent, which launchd loads at login and starts once. <c>RunAtLoad</c> and no <c>KeepAlive</c>:
    /// this is registration, not supervision, and a runtime that a person stops must stay stopped.
    /// </summary>
    private static AutostartRegistration MacOs(IReadOnlyList<string> line, string home)
    {
        var plistPath = ArtifactPath(AutostartPlatform.MacOs, home, string.Empty);
        var arguments = string.Join("\n", line.Select(word => $"      <string>{Xml(word)}</string>"));
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
              <dict>
                <key>Label</key>
                <string>{Label}</string>
                <key>ProgramArguments</key>
                <array>
            {arguments}
                </array>
                <key>RunAtLoad</key>
                <true/>
              </dict>
            </plist>
            """;

        return new AutostartRegistration(
            AutostartPlatform.MacOs,
            Label,
            plistPath,
            plist,
            [["launchctl", "load", "-w", plistPath]],
            [["launchctl", "unload", "-w", plistPath]],
            QueryFor(AutostartPlatform.MacOs),
            line);
    }

    /// <summary>
    /// A user unit, enabled into the account's own default target. <c>Restart=no</c> for the same reason the
    /// plist has no <c>KeepAlive</c>: nothing here supervises anything.
    /// </summary>
    private static AutostartRegistration Linux(IReadOnlyList<string> line, string home)
    {
        var unitPath = ArtifactPath(AutostartPlatform.Linux, home, string.Empty);
        var unit = $"""
            [Unit]
            Description=The Jason runtime
            Documentation=https://github.com/reply-team/jason-ai
            After=default.target

            [Service]
            Type=simple
            ExecStart={Systemd(line)}
            Restart=no

            [Install]
            WantedBy=default.target

            """;

        return new AutostartRegistration(
            AutostartPlatform.Linux,
            UnitName,
            unitPath,
            unit,
            [
                ["systemctl", "--user", "daemon-reload"],
                ["systemctl", "--user", "enable", UnitName],
            ],
            [
                ["systemctl", "--user", "disable", UnitName],
                ["systemctl", "--user", "daemon-reload"],
            ],
            QueryFor(AutostartPlatform.Linux),
            line);
    }

    /// <summary>One command line out of its words: a word with a space in it keeps its own quotes.</summary>
    private static string Join(IEnumerable<string> words) =>
        string.Join(' ', words.Select(word => word.Contains(' ', StringComparison.Ordinal) ? $"\"{word}\"" : word));

    /// <summary>
    /// The same line as a systemd unit spells it. Two characters are not the same there as everywhere else: a
    /// per-cent begins a specifier the manager substitutes, so it is doubled; and a double quote has no
    /// spelling that <c>ExecStart</c> and the reader below would both agree on, so a path carrying one is
    /// refused by name rather than written into a unit that would split into different words than it was given.
    /// </summary>
    /// <remarks>
    /// Refused at composition, which is where the path is known, and therefore refused by <c>disable</c> as
    /// well as by <c>enable</c> on such a path — there is nothing to disable, because nothing could have been
    /// registered. Windows cannot reach this: a double quote is not allowed in a path there, and the task
    /// document escapes what XML needs. The plist is one string per word and needs neither.
    /// </remarks>
    private static string Systemd(IReadOnlyList<string> line)
    {
        foreach (var word in line)
        {
            if (word.Contains('"', StringComparison.Ordinal))
            {
                throw new AutostartException(
                    AutostartCodes.Refused,
                    $"A systemd unit cannot carry a double quote in the command it runs, and there is one in '{word}'. "
                    + "Register from a path without it, or start the runtime yourself with `jason runtime start`.");
            }
        }

        return Join(line.Select(word => word.Replace("%", "%%", StringComparison.Ordinal)));
    }

    /// <summary>The words of a command line composed by <see cref="Join"/>, back as they went in.</summary>
    private static IReadOnlyList<string> Words(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        var started = false;

        foreach (var character in line)
        {
            switch (character)
            {
                case '"':
                    quoted = !quoted;
                    started = true;
                    break;

                case ' ' when !quoted:
                    if (started)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                        started = false;
                    }

                    break;

                default:
                    word.Append(character);
                    started = true;
                    break;
            }
        }

        if (started)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static IReadOnlyList<string> FromTask(string artifact)
    {
        var exec = XDocument.Parse(artifact).Descendants(TaskSchema + "Exec").FirstOrDefault();
        if (exec?.Element(TaskSchema + "Command")?.Value is not { } command)
        {
            return [];
        }

        return [command, .. Words(exec.Element(TaskSchema + "Arguments")?.Value ?? string.Empty)];
    }

    /// <summary>
    /// The plist's <c>ProgramArguments</c>. Read with the parser rather than a search, and with its document
    /// type declaration ignored: that declaration names a URL, and nothing in this product fetches one.
    /// </summary>
    private static IReadOnlyList<string> FromPlist(string artifact)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null };
        using var reader = System.Xml.XmlReader.Create(new StringReader(artifact), settings);
        var plist = XDocument.Load(reader);

        var keys = plist.Descendants("key").ToList();
        var arguments = keys.FirstOrDefault(key => key.Value == "ProgramArguments")?.ElementsAfterSelf().FirstOrDefault();
        return arguments is null ? [] : [.. arguments.Elements("string").Select(word => word.Value)];
    }

    private static IReadOnlyList<string> FromUnit(string artifact)
    {
        const string Start = "ExecStart=";
        var line = artifact
            .Split('\n')
            .Select(text => text.Trim())
            .FirstOrDefault(text => text.StartsWith(Start, StringComparison.Ordinal));

        // The doubling `Systemd` applied, undone: what a unit says it runs is what it was given.
        return line is null
            ? []
            : [.. Words(line[Start.Length..]).Select(word => word.Replace("%%", "%", StringComparison.Ordinal))];
    }

    /// <summary>The three characters a document cannot carry as themselves, in a path that may hold any of them.</summary>
    private static string Xml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
