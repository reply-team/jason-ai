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

    /// <summary>
    /// The program this account chose for <c>jason:</c> links, which Windows routes a link to past any class key: its
    /// <see cref="ProgIdValue"/> names the class, and Jason's own is <see cref="AttentionLinks.Scheme"/>.
    /// </summary>
    public const string UserChoiceKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + AttentionLinks.Scheme + @"\UserChoice";

    /// <summary>The value of <see cref="UserChoiceKey"/> that names the chosen class.</summary>
    public const string ProgIdValue = "ProgId";

    /// <summary>A key's default value.</summary>
    public const string DefaultValue = "";

    /// <summary>What the key's default value says it is.</summary>
    public const string Description = "URL:Jason";

    /// <summary>The value, empty, that makes the class a URI scheme.</summary>
    public const string ProtocolValue = "URL Protocol";

    /// <summary>
    /// Whether two commands name the same executable: compared without regard to case, as Windows compares paths, and
    /// otherwise exactly; the key exists on Windows only.
    /// </summary>
    public static bool SameCommand(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>The value that says the key is Jason's.</summary>
    public const string MarkerValue = "AppUserModelID";

    /// <summary>What <see cref="MarkerValue"/> holds on Jason's key: the AppUserModelID its toasts are shown under.</summary>
    public const string Marker = ToastRegistration.Aumid;

    /// <summary>
    /// The command for an executable started as <paramref name="self"/>: each element quoted, then the link as
    /// <c>"%1"</c>. A path holding a quote or a <c>%</c> is refused: the shell substitutes its <c>%</c> tokens anywhere
    /// in the command, so such a path would start something other than this executable.
    /// </summary>
    public static string Command(IReadOnlyList<string> self)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self.Count == 0 || self.Any(string.IsNullOrWhiteSpace) || self.Any(part => part.AsSpan().IndexOfAny('"', '%') >= 0))
        {
            throw new ArgumentException("An executable's command is one or more paths, none holding a quote or a percent sign.", nameof(self));
        }

        return string.Join(' ', self.Select(part => $"\"{part}\"")) + " \"%1\"";
    }
}
