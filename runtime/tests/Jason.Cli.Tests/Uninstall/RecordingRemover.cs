using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The machine, as far as an uninstall ever needs one: what it was asked to remove, in order, and nothing
/// actually removed unless this says otherwise.
/// </summary>
/// <remarks>
/// The counterpart of <c>RecordingRegistrar</c>, and here for the same reason with a sharper edge. These
/// tests — and the three guards that type documented command lines for real — run against a machine whose
/// PATH and whose executable belong to whoever started the suite. A remover that acted would act on them.
/// </remarks>
public sealed class RecordingRemover(StepLog? log = null) : IInstallationRemover
{
    /// <summary>Every call, in order, as <c>verb path</c>. The order is the safety property, so it is recorded.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Whether this platform pretends it can delete its own running image.</summary>
    public bool CanRemoveRunningImage { get; init; } = true;

    /// <summary>Paths this remover refuses, for the tests about a file that cannot go.</summary>
    public HashSet<string> Refuses { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the file operations really happen. Off by default: most of these tests are about what was
    /// <em>asked</em>, and a test that wants the tree afterwards says so rather than every test risking it.
    /// </summary>
    public bool ReallyRemoves { get; init; }

    public void RemoveFile(string path)
    {
        Record("file.remove", path);
        if (ReallyRemoves && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Whether a moved image is pretended to have been left with no cleanup to remove it.</summary>
    public bool LeavesACopy { get; init; }

    /// <summary>Where a running image is pretended to have been moved, when this platform cannot delete one.</summary>
    public string? MovesTo { get; set; }

    public ExecutableOutcome RemoveExecutable(string path)
    {
        Record("executable.remove", path);

        if (!File.Exists(path))
        {
            return new ExecutableOutcome(true, null, "It was already gone.");
        }

        if (CanRemoveRunningImage)
        {
            if (ReallyRemoves)
            {
                File.Delete(path);
            }

            return new ExecutableOutcome(true, null, null);
        }

        var moved = MovesTo ?? path + ".moved";
        if (ReallyRemoves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Move(path, moved, overwrite: true);
        }

        return LeavesACopy
            ? new ExecutableOutcome(false, moved, $"It is the running image, so it was moved to '{moved}'; but no cleanup could be started to remove it.", LeftBehind: true)
            : new ExecutableOutcome(false, moved, $"It is the running image, so it was moved to '{moved}'.");
    }

    public bool RemoveDirectoryIfEmpty(string path)
    {
        Record("directory.remove_if_empty", path);
        if (!ReallyRemoves || !Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
        {
            return false;
        }

        Directory.Delete(path, recursive: false);
        return true;
    }

    public void RemoveTree(string path)
    {
        Record("tree.remove", path);
        if (ReallyRemoves && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>What this pretend machine's PATH carries. A test that cares sets it.</summary>
    public PathEntryPlan? PathEntry { get; set; }

    /// <summary>Directories this was asked about. Separate from <see cref="Calls"/>: reading is not removing, and a test asserting that nothing was removed must not be tripped by a question.</summary>
    public List<string> Reads { get; } = [];

    public PathEntryPlan ReadPathEntry(string directory)
    {
        Reads.Add(directory);
        return PathEntry ?? new PathEntryPlan(directory, [], null, Ours: false);
    }

    public PathEntryOutcome RemovePathEntry(PathEntryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Record("path.remove", plan.Directory);
        return new PathEntryOutcome(plan.Ours, plan.Ours ? [.. plan.Profiles] : [], plan.Ours ? null : "not ours");
    }

    /// <summary>Where this pretend build unpacked its native libraries. A test that cares sets it.</summary>
    public string? ExtractedLibraries { get; set; }

    public string? ReadExtractedLibraries(string executable)
    {
        Reads.Add(executable);
        return ExtractedLibraries;
    }

    public void RemoveExtractedLibraries(string directory)
    {
        Record("extracted.remove", directory);
        if (ReallyRemoves && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Paths on which this remover fails with something nobody planned for, rather than refusing: the shape of
    /// an exception an uninstall did not expect, part-way through a run that has already removed things.
    /// </summary>
    public HashSet<string> Breaks { get; } = new(StringComparer.OrdinalIgnoreCase);

    private void Record(string verb, string path)
    {
        if (Breaks.Contains(path))
        {
            throw new InvalidOperationException($"nobody planned for '{path}'.", new FileNotFoundException(string.Empty, "Some.Assembly"));
        }

        if (Refuses.Contains(path))
        {
            throw new RemovalRefused(CliErrors.UninstallRefused, $"'{path}' could not be removed.");
        }

        Calls.Add($"{verb} {path}");
        log?.Add(verb);
    }
}

/// <summary>
/// One ordered list several fakes append to, so a test can assert the order the machine would have seen
/// rather than the order one fake happened to record.
/// </summary>
public sealed class StepLog
{
    private readonly Lock gate = new();

    public List<string> Steps { get; } = [];

    public void Add(string step)
    {
        lock (gate)
        {
            Steps.Add(step);
        }
    }
}
