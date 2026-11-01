namespace Jason.Runtime.Hosting;

/// <summary>What a detached runtime found attached to it when it let go of its standard streams.</summary>
/// <remarks>
/// On Windows 11 24H2 and later the executable's <c>consoleAllocationPolicy</c> means a runtime nobody handed a
/// console has none. An older Windows ignores the setting and hands a runtime started at logon a console with a
/// window on the desktop, which is what <see cref="Freed"/> records letting go of. A console with no window —
/// what a parent that asked for none may still hand over — is <see cref="FreedHidden"/>, so that freeing it never
/// reads as the policy having failed.
/// </remarks>
public enum ConsoleAtStart
{
    /// <summary>No console: the executable's <c>consoleAllocationPolicy</c>, or a parent that gave it none.</summary>
    None,

    /// <summary>A console with a visible window was attached and the runtime let go of it: a Windows that ignores the policy.</summary>
    Freed,

    /// <summary>A console with no visible window was attached and the runtime let go of it: nothing anyone saw.</summary>
    FreedHidden,

    /// <summary>A console was attached and <c>FreeConsole</c> refused, so any window it has stays.</summary>
    NotFreed,
}
