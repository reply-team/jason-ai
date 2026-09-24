using System.Diagnostics.CodeAnalysis;

namespace Jason.Contracts.Discovery;

/// <summary>
/// How to run this program again: the command, as far as the mode word that comes next. One executable serves
/// every mode, so every part of Jason that starts another copy of itself — the CLI starting a detached runtime,
/// the runtime starting a plugin host — is asking this same question and must get this same answer.
/// </summary>
/// <remarks>
/// A published executable runs itself. A build started as <c>dotnet jason.dll</c> has the muxer for its process
/// path and the assembly is not recoverable from it, so the entry assembly has to be named again — both because
/// the child would otherwise be <c>dotnet &lt;mode&gt;</c>, which is not a program, and because naming
/// <c>jason</c> alone would start whatever is on the PATH rather than this build. The muxer is named by the path
/// the operating system gives for this very process wherever there is one: a bare <c>dotnet</c> is looked up on
/// the PATH, and a runtime started by a muxer that is not on it — an installation beside the app, a shim, a PATH
/// the plugin host's cleared environment does not carry — would start no child at all.
/// </remarks>
public static class SelfExecutable
{
    private const string DotnetHost = "dotnet";

    /// <summary>What to run to get another copy of this very process.</summary>
    public static IReadOnlyList<string> Command { get; } =
        Resolve(Environment.ProcessPath, Environment.GetCommandLineArgs()[0]);

    /// <summary>The one file this program is <em>installed</em> as, or null where there is no such file.</summary>
    /// <remarks>
    /// <para>
    /// The other question <see cref="Command"/> answers, and the one every verb that removes or replaces this
    /// installation has to ask. A build started as <c>dotnet jason.dll</c> has the muxer for its process path,
    /// so the honest answer is that there is no single file — and emphatically not the muxer's own path, which
    /// is somebody else's program and on a Windows machine is usually
    /// <c>C:\Program Files\dotnet\dotnet.exe</c>.
    /// </para>
    /// <para>
    /// Here rather than restated by each caller. <c>jason update apply</c> asked it correctly from the start
    /// and <c>jason uninstall</c> asked <c>Environment.ProcessPath</c> directly while its comment claimed it
    /// worked it out "the same way" — so it planned to delete the muxer, take its directory off this account's
    /// PATH, and on Windows move it aside, which succeeds. Two copies of one rule is how that happens.
    /// </para>
    /// <para>
    /// <b>And only a single-file build is installed as a file at all.</b> <c>dotnet run --project
    /// runtime/src/Jason.App</c> — the way this repository's README runs from source — does not go through the
    /// muxer: it starts the build's own launcher, <c>bin/Debug/net10.0/jason</c>, whose process path is that
    /// launcher. So the uninstall planned to remove the launcher and its build directory, and the path check
    /// printed that build directory as the line to put on the PATH. A published release is one file with every
    /// assembly inside it; a build's launcher is one file of many, and none of them is an installation.
    /// </para>
    /// </remarks>
    public static string? InstalledImage => Image(Command, IsSingleFile);

    /// <summary>
    /// Whether this process is a single-file bundle: every assembly inside the one executable, so none has a
    /// file of its own.
    /// </summary>
    public static bool IsSingleFile
    {
        [UnconditionalSuppressMessage(
            "SingleFile",
            "IL3000:Avoid accessing Assembly file path when publishing as a single file",
            Justification = "The empty location a single-file bundle gives is the answer this asks for.")]
        get => string.IsNullOrEmpty(typeof(SelfExecutable).Assembly.Location);
    }

    /// <summary>
    /// The same decision over what it depends on, so what a muxed installation or a build's launcher answers can
    /// be asked without being one.
    /// </summary>
    /// <param name="command">What runs this program again: one element for an executable, two for the muxer.</param>
    /// <param name="singleFile">Whether that executable is a single-file bundle rather than a build's launcher.</param>
    public static string? Image(IReadOnlyList<string> command, bool singleFile)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Count == 1 && singleFile ? command[0] : null;
    }

    /// <summary>
    /// The same decision over the two values it depends on: the path the operating system says this process is
    /// running, and the entry assembly it was handed. Public so that the answer can be tested without a process.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? processPath, string entryAssembly)
    {
        if (processPath is not null
            && !string.Equals(Path.GetFileNameWithoutExtension(processPath), DotnetHost, StringComparison.OrdinalIgnoreCase))
        {
            return [processPath];
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssembly);

        // The muxer that is running this process, by the path it is running at. The bare name is only for a host
        // that publishes no process path at all, where there is nothing better to say.
        return processPath is null ? [DotnetHost, entryAssembly] : [processPath, entryAssembly];
    }
}
