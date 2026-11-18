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

    /// <summary>
    /// Clears a disabled record of <paramref name="label"/> in each of <paramref name="domains"/> that holds one, and writes
    /// nothing anywhere else: each domain is read with <c>launchctl print-disabled</c>, and <c>launchctl enable</c> runs
    /// only where the label is recorded there as disabled. A domain that cannot be read, or whose answer names the label
    /// otherwise or not at all, is left as it is; nothing it is answered is an error.
    /// </summary>
    /// <remarks>
    /// <c>enable</c> writes a record of its own, "enabled", which no command run without administrator rights removes, so run
    /// on a label no record names it would leave a record where none stood. Where a disabled record did stand, the
    /// "enabled" one it leaves in its place does nothing but let the job load.
    /// </remarks>
    public static void ClearDisabled(string label, IEnumerable<string> domains, BoundedRun? run = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(domains);

        run ??= AutostartRegistrars.Run;
        foreach (var domain in domains)
        {
            try
            {
                var (exit, output, _) = run([Launchctl, "print-disabled", domain], Limit);
                if (exit == 0 && RecordsDisabled(output, label))
                {
                    run([Launchctl, "enable", $"{domain}/{label}"], Limit);
                }
            }
            catch (AutostartException)
            {
                // launchctl could not be started, or did not answer in time: nothing is written for that domain.
            }
        }
    }

    /// <summary>
    /// Whether launchctl's <c>print-disabled</c> answer records <paramref name="label"/> as disabled: its line
    /// <c>"&lt;label&gt;" =&gt; disabled</c>, or <c>=&gt; true</c> as earlier versions of macOS print it.
    /// </summary>
    public static bool RecordsDisabled(string answer, string label)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        foreach (var line in answer.ReplaceLineEndings("\n").Split('\n'))
        {
            var text = line.Trim();
            var quoted = $"\"{label}\"";
            if (!text.StartsWith(quoted, StringComparison.Ordinal))
            {
                continue;
            }

            var state = text[quoted.Length..].Trim();
            if (state.StartsWith("=>", StringComparison.Ordinal))
            {
                state = state[2..].Trim();
                return state is "disabled" or "true";
            }
        }

        return false;
    }
}
