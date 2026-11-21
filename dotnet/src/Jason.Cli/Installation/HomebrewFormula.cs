namespace Jason.Cli.Installation;

/// <summary>
/// Homebrew's older formula <c>jason</c>, linked into a Homebrew prefix beside the jason Jason Desktop carries: an older
/// Jason, which a terminal may still find, and whose link keeps Homebrew from linking the cask's jason.
/// </summary>
/// <param name="Link"><c>&lt;prefix&gt;/bin/jason</c>, the link Homebrew made.</param>
/// <param name="Owner">The keg it resolves to, as <see cref="PackageOwner"/> reads it: the formula's, by its receipt.</param>
/// <param name="Version">The keg's version, its folder's name under <c>Cellar/jason</c>.</param>
public sealed record HomebrewFormula(string Link, PackageOwner Owner, string Version)
{
    /// <summary>The command that takes the formula off, beside a cask of the same name.</summary>
    public const string UninstallCommand = "brew uninstall --formula jason";

    /// <summary>
    /// The formula's keg linked under one of Homebrew's prefixes (<see cref="HomebrewCask.Prefixes"/>): a link at
    /// <c>&lt;prefix&gt;/bin/jason</c> resolving to <c>&lt;prefix&gt;/Cellar/jason/&lt;v&gt;/bin/jason</c>, whose keg carries
    /// Homebrew's receipt. Null where none is, or where the environment names no records.
    /// </summary>
    public static HomebrewFormula? Linked(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (env.Managers is not { } records)
        {
            return null;
        }

        var given = env.Variables is { } variables ? variables.GetValueOrDefault(HomebrewCask.PrefixVariable) : Environment.GetEnvironmentVariable(HomebrewCask.PrefixVariable);
        foreach (var prefix in HomebrewCask.Prefixes(given, env.Setup?.Home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
        {
            if (Linked(Path.Combine(prefix, "bin", "jason"), records) is { } linked)
            {
                return linked;
            }
        }

        return null;
    }

    /// <summary>The rule over one link: Homebrew's link to the formula's keg, or null.</summary>
    public static HomebrewFormula? Linked(string link, IManagerRecords records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentNullException.ThrowIfNull(records);

        string? target;
        try
        {
            target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (target is null || PackageOwner.Of(target, link, records) is not { Manager: PackageOwner.Homebrew } owner)
        {
            return null;
        }

        var version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(owner.Executable))) ?? string.Empty;
        return new HomebrewFormula(link, owner, version);
    }

    /// <summary>What is said where the formula is linked beside the carried jason, and what takes it off.</summary>
    public string Sentence() =>
        $"Homebrew's formula jason {Version} is linked at {Link} beside this jason: it is an older Jason, and while it is linked Homebrew does not link the cask's jason. `jason runtime stop`, then `{UninstallCommand}`, takes it off.";

    /// <summary>What set-up says: the sentence, and that it left the formula as it is.</summary>
    public string Problem() => Sentence() + " It was left as it is.";

    /// <summary>The name of the <c>path</c> check's detail that carries <see cref="Sentence"/>.</summary>
    public const string DetailName = "homebrew_formula";
}
