namespace Jason.Contracts.Discovery;

/// <summary>
/// What a child is, which decides its standard streams: the two kinds speak in different encodings, and the door
/// chooses for each so that no caller has to remember which.
/// </summary>
public enum ChildKind
{
    /// <summary>
    /// A child that speaks to Jason: a plugin's program, an agent, the plugin host, the toast host, the runtime a CLI
    /// starts. All three streams are redirected and each is UTF-8 without a byte-order mark, on every platform.
    /// </summary>
    SpeaksToJason,

    /// <summary>
    /// A system tool: a task scheduler, a service manager, a shell, a program asked its version. Standard output and
    /// standard error are redirected and read in the console's code page, because that is what such a tool writes;
    /// its standard input is this process's own.
    /// </summary>
    SystemTool,

    /// <summary>
    /// A system tool given a standard input of its own, which its caller closes as soon as the tool has started, so
    /// that it reads an end of file rather than anything of this process's.
    /// </summary>
    SystemToolWithInput,
}
