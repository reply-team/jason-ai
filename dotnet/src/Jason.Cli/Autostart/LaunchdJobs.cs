namespace Jason.Cli.Autostart;

/// <summary>Runs one command, waiting for it no longer than the limit: <see cref="AutostartRegistrars.Run(IReadOnlyList{string}, TimeSpan?)"/>, or a test's stub.</summary>
internal delegate (int Exit, string Output, string Error) BoundedRun(IReadOnlyList<string> command, TimeSpan? limit);

/// <summary>
/// macOS: a LaunchAgent's job taken out of this account's launchd before its file goes, wherever an uninstall or a
/// disable removes one, so no job is left loaded with its plist gone.
/// </summary>
/// <remarks>
/// <para>
/// The file's own unload comes first, where there is a file. Then the job is removed by its label, which holds where the
/// file is gone already or was moved since the job was loaded: <c>launchctl remove</c>, "Remove the job from launchd by
/// label." Both are legacy subcommands, which "select the target domain based on whether they are executed as root or not",
/// so run by the account they reach its own (launchctl(1)).
/// </para>
/// <para>
/// Each is waited for <see cref="Limit"/> at most and its answer is not read: a job that is not loaded is the ordinary case
/// and no error, and a launchctl that does not answer does not hold the caller. Every command goes through
/// <c>ChildProcess</c>.
/// </para>
/// </remarks>
internal static class LaunchdJobs
{
    /// <summary>How long each launchctl is waited for.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>launchctl, by the path macOS keeps it at.</summary>
    public const string Launchctl = "/bin/launchctl";

    /// <summary>Runs each unload given, then removes the job by <paramref name="label"/>; nothing it is answered is an error.</summary>
    public static void TakeOut(IEnumerable<IReadOnlyList<string>> unloads, string label, BoundedRun? run = null)
    {
        ArgumentNullException.ThrowIfNull(unloads);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        run ??= AutostartRegistrars.Run;
        foreach (var command in unloads.Append([Launchctl, "remove", label]))
        {
            try
            {
                run(command, Limit);
            }
            catch (AutostartException)
            {
                // launchctl could not be started, or did not answer in time: the file still goes, and the job with the
                // login session.
            }
        }
    }
}
