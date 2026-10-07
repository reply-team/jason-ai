namespace Jason.Cli.Process;

/// <summary>Who holds the process id an endpoint descriptor names, as far as this account can tell.</summary>
internal enum ProcessHolder
{
    /// <summary>No process has the id: the descriptor was left behind.</summary>
    Gone,

    /// <summary>A process has the id and started when the descriptor says the runtime did.</summary>
    TheRuntime,

    /// <summary>
    /// A process has the id and nothing shows whether it is the runtime: the descriptor records no start, or this
    /// account may not ask the process when it started.
    /// </summary>
    Unreadable,

    /// <summary>A process has the id that started after the descriptor was written: another program's.</summary>
    Stranger,
}

/// <summary>
/// The rule that tells the runtime a descriptor describes from a process that merely has its id. A pid names a
/// process only while that process lives; after a restart the id a descriptor recorded belongs to whatever the
/// machine started next under it, and "that pid is running" answers yes about a stranger. The descriptor carries
/// the moment the runtime started, and a process that started after it is somebody else's. The verbs that must
/// decide whether a descriptor found on disk still has a runtime behind it — <c>runtime start</c>, before it
/// launches over one, and <c>uninstall</c>, before it takes the installation away — ask here, so they cannot
/// disagree. <c>runtime stop</c> asks something else: whether the runtime that has just acknowledged its shutdown
/// has exited, and a bare "is that pid running" answers that.
/// </summary>
internal static class DescriptorProcess
{
    /// <summary>
    /// How far after the moment a runtime records as its start its own process may be read to have started, and
    /// still be it.
    /// </summary>
    /// <remarks>
    /// The process always starts first — the runtime takes the time once it is running — so this is not a window
    /// for the order to be wrong in. It covers how coarsely a platform reports a process's start: Linux derives it
    /// from the time since boot, to the clock tick.
    /// </remarks>
    public static readonly TimeSpan StartAllowance = TimeSpan.FromSeconds(5);

    /// <summary>Whether the process with that id is the runtime a descriptor describes — started when it said it did.</summary>
    /// <param name="processes">The process table.</param>
    /// <param name="pid">The id the descriptor names.</param>
    /// <param name="startedAt">The moment the descriptor says the runtime started, where it says one.</param>
    public static ProcessHolder Of(IRuntimeProcessControl processes, int pid, DateTimeOffset? startedAt)
    {
        ArgumentNullException.ThrowIfNull(processes);

        if (!processes.IsRunning(pid))
        {
            return ProcessHolder.Gone;
        }

        if (startedAt is not { } recorded || processes.StartTime(pid) is not { } started)
        {
            // Running, and not shown to be the runtime or not to be: as the runtime it may be, unless it has gone
            // in the meantime.
            return processes.IsRunning(pid) ? ProcessHolder.Unreadable : ProcessHolder.Gone;
        }

        return started <= recorded + StartAllowance ? ProcessHolder.TheRuntime : ProcessHolder.Stranger;
    }
}
