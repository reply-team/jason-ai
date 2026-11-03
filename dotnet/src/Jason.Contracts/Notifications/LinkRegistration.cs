namespace Jason.Contracts.Notifications;

/// <summary>
/// The per-user registration that starts this executable for a <c>jason:</c> link, named once: the toast host writes it
/// beside <see cref="ToastRegistration.Key"/> before a toast, and <c>jason uninstall</c> removes it where its command
/// names that installation's executable.
/// </summary>
/// <remarks>
/// <para>
/// The key is Windows' own form for a URI scheme: a class under <c>HKEY_CURRENT_USER\Software\Classes</c> with a
/// <c>URL Protocol</c> value and a <c>shell\open\command</c> whose <c>%1</c> the shell replaces with the whole link,
/// undecoded. What makes the key ours is <see cref="MarkerValue"/> holding <see cref="Marker"/>: a key without it is
/// another program's and is never written over, and a machine-wide one is never shadowed.
/// </para>
/// <para>
/// The command names the executable only. It carries no data directory: a click finds the runtime the way a
/// <c>jason</c> typed in a terminal does, so a runtime under test can never take the link from the person's own.
/// </para>
/// </remarks>
public static class LinkRegistration
{
    /// <summary>The class under this account's classes.</summary>
    public const string Key = @"Software\Classes\" + AttentionLinks.Scheme;

    /// <summary>The command the shell runs for a link.</summary>
    public const string CommandKey = Key + @"\shell\open\command";

    /// <summary>The same class in the merged view Windows resolves a scheme through, where a machine-wide key shows.</summary>
    public const string ClassesKey = AttentionLinks.Scheme;

    /// <summary>The command in that merged view.</summary>
    public const string ClassesCommandKey = ClassesKey + @"\shell\open\command";

    /// <summary>A key's default value.</summary>
    public const string DefaultValue = "";

    /// <summary>What the key's default value says it is.</summary>
    public const string Description = "URL:Jason";

    /// <summary>The value, empty, that makes the class a URI scheme.</summary>
    public const string ProtocolValue = "URL Protocol";

    /// <summary>The value that says the key is Jason's.</summary>
    public const string MarkerValue = "AppUserModelID";

    /// <summary>What <see cref="MarkerValue"/> holds on Jason's key: the AppUserModelID its toasts are shown under.</summary>
    public const string Marker = ToastRegistration.Aumid;

    /// <summary>
    /// The command for an executable started as <paramref name="self"/>: each element quoted, then the link as
    /// <c>"%1"</c>.
    /// </summary>
    public static string Command(IReadOnlyList<string> self)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self.Count == 0 || self.Any(string.IsNullOrWhiteSpace) || self.Any(part => part.Contains('"', StringComparison.Ordinal)))
        {
            throw new ArgumentException("An executable's command is one or more paths, none holding a quote.", nameof(self));
        }

        return string.Join(' ', self.Select(part => $"\"{part}\"")) + " \"%1\"";
    }
}
