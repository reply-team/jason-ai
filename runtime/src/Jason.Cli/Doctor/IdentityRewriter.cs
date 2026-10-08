using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Cli.Doctor;

/// <summary>
/// Rewrites the names of an installation out of a document before it is written: the home directory as <c>~</c>, a data
/// directory outside it as <c>&lt;data&gt;</c>, the account's name, the machine's name and the domain as placeholders,
/// and any Windows account identifier as one. A diagnostic bundle is sent to somebody else, and a path in a log line names
/// the person as much as a name typed in a settings file does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every string.</b> Every string of the document is rewritten, the keys of an object with the values, as decoded
/// text: what a person reading the file sees is what is rewritten, whatever escapes the file used. A key that becomes
/// another key's name is given a number rather than dropped, because a bundle must still be written, with every value
/// in it.
/// </para>
/// <para>
/// <b>What matches.</b> The home directory matches as a path wherever it stands in a string, with either separator and,
/// since a path that has been through JSON text has its separators doubled, with any number of them; on Windows it
/// matches in any case. It must end where a name ends — <c>C:\Users\Adam</c> is not the home directory of <c>Ada</c> —
/// and it goes first, because its last component is the account's name and the path would otherwise read as somebody's
/// folder rather than as the home directory. A data directory outside home matches the same way, as a path, and of the
/// two the longer goes first where one is inside the other; inside home it is left to the home directory, under which it
/// reads <c>~</c> and a path, which says more than a mark would. The account's, the machine's and the domain's names
/// match as whole tokens: bounded by anything that is not a letter or a digit, or by the backslash escape of a line
/// break, a tab or a quote, or the percent escape of a separator, which is how text that was JSON or a URL carries one;
/// in any case, in a path or out of one. A name that is an ordinary word is rewritten wherever it stands alone and never inside a longer
/// word — <c>log</c> is not found in <c>logs</c> — and the cost of that, a placeholder where a word was, is the side to
/// be wrong on. A Windows account's identifier matches by its shape, <c>S-1-5-21</c> or the <c>S-1-12-1</c> of a work or
/// Microsoft account and three or four numbers, wherever it stands: this account's, any other's, and the one in a
/// scheduled task's name.
/// </para>
/// <para>
/// <b>Other spellings.</b> A name is also written in the ways a path comes by it. Where the home directory is a profile —
/// a folder under <c>Users</c> or <c>Documents and Settings</c> on Windows, on whatever drive and in whatever case,
/// under <c>Users</c> on macOS, or under <c>/home</c> on Linux — its last component is a name too, since a profile folder
/// is not always the account's name, and it is written as the account's. Any other home directory, <c>/opt/jason</c> or
/// <c>/var/empty</c>, is rewritten as a path and nothing more: its last component is a folder, and a word that is also
/// the name of one is left alone. A name that has a space in it or a letter a URL spells with percent signs is found so
/// spelled, in either case of its digits, and so is the home directory, separators included. And a name that is long,
/// or has a space or another character in it that a short name cannot hold, has the short alias Windows keeps for the
/// folder — its first six letters in capitals, <c>~</c> and a number, or, where four folders already have that one, its
/// first two letters, four digits of hexadecimal, <c>~</c> and a number — which is what a path reads like where it was
/// never put through a call that spells the long name out: the raw value of a temporary-directory variable, a command
/// line as it was launched, another program's output. A name with a period is cut at the last one, as a file's name
/// is: the six letters are the ones before it, without the other periods, and the first three after it come last as an
/// extension, so <c>artem.kosolap</c> is <c>ARTEM~1.KOS</c>. The alias, extension and all, is rewritten as the
/// account's name, wherever it stands as a token.
/// </para>
/// <para>
/// <b>Not covered.</b> The short alias of a name that is not ASCII: Windows puts a substitute for each character that
/// its code page lacks, and which one is not known here.
/// </para>
/// <para>
/// <b>One pass.</b> All of it is one search over the text as it was, and where two names begin at the same place the
/// longer one wins, so a placeholder is never read again as text and a machine called <c>desk-7</c> is not left as
/// <c>&lt;user&gt;-7</c> for an account called <c>desk</c>. A name that is empty is skipped: it would match everywhere.
/// </para>
/// <para>
/// <b>And one for what a URL spells.</b> A name or a directory followed by the percent escapes of a character outside
/// ASCII — a lead byte and the byte after it — is left by that search, because whether the word goes on depends on which
/// character the escapes spell, and a pattern cannot read that. A second search finds exactly those, in what the first
/// left, and reads the escapes: a letter or a digit goes on with the word and the name stays, as it would before the
/// same letter written plainly; anything else — a no-break space, the curly apostrophe of "Ada's" — ends it, and the name
/// is rewritten. Every other name the first search tried, at the same place or inside, it has already rewritten.
/// </para>
/// <para>
/// A cap on the length of a text, <see cref="ExceptionText"/>, comes after this and not before: a cut through the
/// middle of a path leaves half a name that nothing here would recognise. The cap reads its own marks back, so a mark
/// that somebody wrote into a text on purpose is kept uncounted; the bounds of the sections of a bundle still hold its
/// size.
/// </para>
/// </remarks>
public sealed class IdentityRewriter
{
    private const string HomeGroup = "home";
    private const string DataGroup = "data";
    private const string RuntimeDataGroup = "runtimedata";
    private const string SidGroup = "sid";
    private const string UserGroup = "user";
    private const string MachineGroup = "machine";
    private const string DomainGroup = "domain";

    private const string HomeMark = "~";
    private const string DataMark = "<data>";
    private const string RuntimeDataMark = "<runtime_data>";
    private const string UserMark = "<user>";
    private const string MachineMark = "<machine>";
    private const string DomainMark = "<domain>";
    private const string SidMark = "<sid>";

    /// <summary>
    /// Any run of path separators of either kind: one, or several where a path has been through JSON text, or the
    /// percent escape of one where it has been through a URL.
    /// </summary>
    private const string Separators = @"(?>(?:[\\/]|%5[cC]|%2[fF])+)";

    /// <summary>
    /// Where a name may begin: after anything that is not a letter or a digit, or right after a backslash escape of a
    /// line break, a tab or a quote, a unicode escape, or a percent escape, each of which ends in a letter or a digit
    /// that is not part of the name.
    /// </summary>
    private const string BeforeName = @"(?:(?<![\p{L}\p{N}])|(?<=\\[nrtbf""\\/])|(?<=\\u[0-9A-Fa-f]{4})|(?<=%[0-9A-Fa-f]{2}))";

    /// <summary>
    /// Where a directory may begin: where a name may, and never after an underscore either — after a letter, a digit or
    /// an underscore the same characters are the end of another word, not a path.
    /// </summary>
    private const string BeforePath = @"(?:(?<![\p{L}\p{N}_])|(?<=\\[nrtbf""\\/])|(?<=\\u[0-9A-Fa-f]{4})|(?<=%[0-9A-Fa-f]{2}))";

    /// <summary>
    /// The percent escapes of a lead byte and the byte after it: how a URL writes a character outside ASCII, which may be
    /// the next letter of a word in another alphabet or a space or a mark of punctuation.
    /// </summary>
    private const string EscapedPair = "%[C-Fc-f][0-9A-Fa-f]%[89A-Ba-b][0-9A-Fa-f]";

    /// <summary>Where a directory may end: where a name may, and never before an underscore, which would make it another folder's name.</summary>
    private const string AfterPath = @"(?![\p{L}\p{N}_])(?!" + EscapedPair + ")";

    /// <summary>
    /// Where a name may end in the first search: before anything that is not a letter or a digit — a percent sign among
    /// them — and not before <see cref="EscapedPair"/>, which only the second search, reading the character it spells,
    /// can decide. A percent sign and two hexadecimal digits that are not that are only a percent sign and the start of a
    /// word: <c>Ada%Dev</c> is a name and a word.
    /// </summary>
    private const string AfterName = @"(?![\p{L}\p{N}])(?!" + EscapedPair + ")";

    /// <summary>Where a name or a directory ends in the second search: right before <see cref="EscapedPair"/>.</summary>
    private const string BeforeEscapedPair = "(?=" + EscapedPair + ")";

    /// <summary>The shape of the identifier of an account that is a person's: a Windows one, or a work or Microsoft one, ending in the account or in the domain it belongs to.</summary>
    private const string AccountSid = "S-1-(?:5-21|12-1)(?:-[0-9]+){3,4}";

    /// <summary>The characters a short name cannot hold, which are left out of its alias, and the reason a name that has one needs an alias at all.</summary>
    private const string NotInShortName = " .\"/\\[]:;=,+";

    private static readonly IReadOnlyDictionary<string, string> Written = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>
        {
            ["home_written_as"] = HomeMark,
            ["data_written_as"] = DataMark,
            ["runtime_data_written_as"] = RuntimeDataMark,
            ["user_written_as"] = UserMark,
            ["machine_written_as"] = MachineMark,
            ["domain_written_as"] = DomainMark,
            ["sid_written_as"] = SidMark,
        });

    private readonly Regex _names;

    /// <summary>The second search: the same names and directories, each found only right before <see cref="EscapedPair"/>.</summary>
    private readonly Regex _beforeEscapes;

    /// <summary>A rewriter for these names, with each directory's short form as this machine gives it.</summary>
    public IdentityRewriter(IdentityFacts facts)
        : this(facts, ShortPaths.Of)
    {
    }

    /// <summary>A rewriter for these names, with each directory's short form as <paramref name="shortForm"/> gives it.</summary>
    /// <param name="facts">The names.</param>
    /// <param name="shortForm">
    /// The short form of a directory, or null where it has none other than itself: on Windows the one the operating system
    /// keeps for an existing directory, and nothing elsewhere. A test hands in its own, so that what it says does not
    /// depend on the folders of the machine that runs it.
    /// </param>
    public IdentityRewriter(IdentityFacts facts, Func<string, string?> shortForm)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(shortForm);

        _names = new Regex(Pattern(facts, shortForm, AfterName, AfterPath, withSid: true), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        _beforeEscapes = new Regex(Pattern(facts, shortForm, BeforeEscapedPair, BeforeEscapedPair, withSid: false), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
    }

    /// <summary>
    /// What each kind of name is written as, for a bundle to list what it did: <c>home_written_as</c>,
    /// <c>data_written_as</c>, <c>runtime_data_written_as</c>, <c>user_written_as</c>, <c>machine_written_as</c>,
    /// <c>domain_written_as</c> and <c>sid_written_as</c>. They are this program's own words, the same for every rewriter,
    /// so they are said without anybody's names: <see cref="MarkWords"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Marks => Written;

    /// <summary>The marks, read without making a rewriter — which reads nothing about an account to say them.</summary>
    public static IReadOnlyDictionary<string, string> MarkWords => Written;

    /// <summary>
    /// A copy of the document with every string rewritten, keys included. The document handed in is not changed, and
    /// what is returned is not part of it, so it can be put wherever the original could not.
    /// </summary>
    public JsonNode Rewrite(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Copy(node)!;
    }

    private JsonNode? Copy(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject members => CopyObject(members),
            JsonArray items => CopyArray(items),
            JsonValue value => CopyValue(value),
            _ => node.DeepClone(),
        };

    private JsonObject CopyObject(JsonObject members)
    {
        var copy = new JsonObject(members.Options);
        foreach (var (key, value) in members)
        {
            copy.Add(Unused(copy, RewriteText(key)), Copy(value));
        }

        return copy;
    }

    private JsonArray CopyArray(JsonArray items)
    {
        var copy = new JsonArray(items.Options);
        foreach (var item in items)
        {
            copy.Add(Copy(item));
        }

        return copy;
    }

    private JsonNode CopyValue(JsonValue value)
    {
        if (value.GetValueKind() != JsonValueKind.String)
        {
            return value.DeepClone();
        }

        var text = value.TryGetValue(out string? held) ? held : value.ToString();
        return JsonValue.Create(RewriteText(text), value.Options)!;
    }

    /// <summary>The key, or the key with a number after it when an earlier key was already rewritten to it.</summary>
    private static string Unused(JsonObject members, string key)
    {
        if (!members.ContainsKey(key))
        {
            return key;
        }

        for (var number = 2; ; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{key} ({number})");
            if (!members.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private string RewriteText(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        // The second search is only for a name before a percent escape, so text with no percent sign left skips it.
        var first = _names.Replace(text, Mark);
        return first.Contains('%', StringComparison.Ordinal)
            ? _beforeEscapes.Replace(first, match => SpellsLetterOrDigit(first, match.Index + match.Length) ? match.Value : Mark(match))
            : first;
    }

    /// <summary>
    /// Whether the percent escapes at <paramref name="at"/> spell, in UTF-8, a letter or a digit — the next character of a
    /// word. Escapes that spell any other character, or no whole character at all, do not.
    /// </summary>
    private static bool SpellsLetterOrDigit(string text, int at)
    {
        Span<byte> bytes = stackalloc byte[4];
        var count = 0;
        while (count < bytes.Length
            && at + 2 < text.Length
            && text[at] == '%'
            && byte.TryParse(text.AsSpan(at + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            bytes[count++] = value;
            at += 3;
            switch (Rune.DecodeFromUtf8(bytes[..count], out var rune, out _))
            {
                case OperationStatus.Done:
                    return Rune.IsLetter(rune) || Rune.IsNumber(rune);
                case OperationStatus.InvalidData:
                    return false;
            }
        }

        return false;
    }

    private static string Mark(Match match) =>
        match.Groups[HomeGroup].Success ? HomeMark
        : match.Groups[DataGroup].Success ? DataMark
        : match.Groups[RuntimeDataGroup].Success ? RuntimeDataMark
        : match.Groups[SidGroup].Success ? SidMark
        : match.Groups[UserGroup].Success ? UserMark
        : match.Groups[MachineGroup].Success ? MachineMark
        : DomainMark;

    /// <summary>
    /// The one expression for every name, its alternatives in the order that decides a tie at one place: the home and the
    /// two data directories, with their short forms, the longest first; an identifier; then the account's, the machine's
    /// and the domain's names, longest first. Every whole path comes before every name, so a name inside a path never
    /// breaks it.
    /// </summary>
    /// <param name="facts">The names.</param>
    /// <param name="shortForm">The short form of a directory, where it has one.</param>
    /// <param name="afterName">Where a name may end: <see cref="AfterName"/>, or <see cref="BeforeEscapedPair"/> for the second search.</param>
    /// <param name="afterPath">Where a directory may end: <see cref="AfterPath"/>, or <see cref="BeforeEscapedPair"/> for the second search.</param>
    /// <param name="withSid">Whether the identifier is among the alternatives: it ends wherever its shape does, so only the first search has it.</param>
    private static string Pattern(IdentityFacts facts, Func<string, string?> shortForm, string afterName, string afterPath, bool withSid)
    {
        var alternatives = new List<string>();

        // The directories, the longest first: where one is inside another, the longer is the one that is meant. A data
        // directory inside home is left to the home directory's rule, under which it reads as a path in home; and the
        // runtime's, where it is this installation's own, is that and nothing else, so one directory has one mark.
        var directories = new List<(string Group, string Path)> { (HomeGroup, facts.Home) };
        if (facts.DataDirectory is { } data && !Inside(data, facts.Home))
        {
            directories.Add((DataGroup, data));
        }

        if (facts.RuntimeDataDirectory is { } runtimeData
            && !Inside(runtimeData, facts.Home)
            && !(facts.DataDirectory is { } own && SameDirectory(runtimeData, own)))
        {
            directories.Add((RuntimeDataGroup, runtimeData));
        }

        foreach (var (group, path) in directories.ToList())
        {
            // A network path's short form is the server's to give, and asking would wait on the network.
            if (!IsRoot(path) && !IsNetworkPath(path) && shortForm(path) is { Length: > 0 } alias && !SameDirectory(alias, path))
            {
                directories.Add((group, alias));
            }
        }

        foreach (var (group, path) in directories.OrderByDescending(directory => Components(directory.Path).Sum(component => component.Length + 1)))
        {
            if (PathPattern(path) is { } pattern)
            {
                // Windows paths are the same path in another case; elsewhere they are not.
                alternatives.Add($"(?<{group}>{BeforePath}{(OperatingSystem.IsWindows() ? $"(?i:{pattern})" : pattern)}){afterPath}");
            }
        }

        if (withSid)
        {
            var sid = string.IsNullOrWhiteSpace(facts.Sid) ? AccountSid : $"{Regex.Escape(facts.Sid)}|{AccountSid}";
            alternatives.Add($"(?<{SidGroup}>(?i:{sid}))");
        }

        var names = new List<(string Group, string Name)>
        {
            (UserGroup, facts.User),
            (MachineGroup, facts.Machine),
        };
        if (facts.Domain is { } domain && !string.Equals(domain, facts.Machine, StringComparison.OrdinalIgnoreCase))
        {
            names.Add((DomainGroup, domain));
        }

        // A profile folder is not always named for the account, and where it is not, it is a name of the account's —
        // but only a folder that is a profile is: any other home directory's last component is a word like any other.
        var folder = ProfileFolder(facts.Home);
        if (folder is not null && !string.Equals(folder, facts.User, StringComparison.OrdinalIgnoreCase))
        {
            names.Add((UserGroup, folder));
        }

        // The sort is stable, so of two names of one length the account's comes before the machine's.
        foreach (var (group, name) in names.Where(known => !string.IsNullOrWhiteSpace(known.Name)).OrderByDescending(known => known.Name.Length))
        {
            alternatives.Add($"(?<{group}>{BeforeName}(?i:{Spelled(name)}){afterName})");
        }

        var shorts = new[] { facts.User, folder }.Select(ShortAliasOf).OfType<ShortAlias>().Distinct().ToList();
        if (shorts.Count > 0)
        {
            // The first form is the stem; the second, which Windows turns to when four folders already have the first,
            // is the stem's first two letters and four digits of hexadecimal. Either is followed by the extension, when
            // the name has one, which goes with it.
            var aliases = shorts.Select(alias =>
                $"(?:{Regex.Escape(alias.Stem)}|{Regex.Escape(alias.Stem[..Math.Min(2, alias.Stem.Length)])}[0-9A-F]{{4}})~[0-9]+"
                + (alias.Extension.Length > 0 ? $"(?:\\.{Regex.Escape(alias.Extension)})?" : string.Empty));
            alternatives.Add($"(?<{UserGroup}>{BeforeName}(?i:{string.Join('|', aliases)}){afterName})");
        }

        return string.Join('|', alternatives);
    }

    /// <summary>
    /// A name as a pattern: as it is written and, where a URL would write it differently — a space, or a letter outside
    /// ASCII — as a URL writes it, whose digits are the same in either case.
    /// </summary>
    private static string Spelled(string name)
    {
        var encoded = Uri.EscapeDataString(name);
        return encoded == name ? Regex.Escape(name) : $"{Regex.Escape(name)}|(?i:{Regex.Escape(encoded)})";
    }

    /// <summary>
    /// What a short alias of a folder is made of, in capitals: the first six letters, and the first three letters of an
    /// extension, which is empty where the name has no period.
    /// </summary>
    private readonly record struct ShortAlias(string Stem, string Extension);

    /// <summary>
    /// The short alias Windows keeps for a folder with this name — or nothing, where it keeps none. A name has an alias
    /// when it is longer than eight characters or has a character in it that a short name cannot hold, a period among
    /// them. It is made as a file's name is: the last period starts the extension, of which three letters are kept; the
    /// letters before it, without the spaces and the other periods, give the first six. So <c>John Smith</c> is
    /// <c>JOHNSM~1</c> and <c>artem.kosolap</c> is <c>ARTEM~1.KOS</c>.
    /// </summary>
    private static ShortAlias? ShortAliasOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || (name.Length <= 8 && name.AsSpan().IndexOfAny(NotInShortName) < 0))
        {
            return null;
        }

        var lastPeriod = name.LastIndexOf('.');
        var (stem, extension) = lastPeriod > 0 ? (name[..lastPeriod], name[(lastPeriod + 1)..]) : (name, string.Empty);
        var letters = Letters(stem, 6);
        return letters.Length == 0 ? null : new ShortAlias(letters, Letters(extension, 3));
    }

    /// <summary>The first letters of a name in capitals, leaving out the characters a short name cannot hold.</summary>
    private static string Letters(string name, int count) =>
        new string(name.Where(letter => !NotInShortName.Contains(letter)).Take(count).ToArray()).ToUpperInvariant();

    /// <summary>
    /// A directory — the home directory or a data directory — as a pattern: its components in order, each in the
    /// spellings of <see cref="Spelled"/>, with a run of separators between them and a leading run where it starts with
    /// one. A directory that is only a root has no pattern: it names nothing, and a mark for it would land on every path
    /// that starts there and every word that spells it.
    /// </summary>
    private static string? PathPattern(string directory)
    {
        var components = Components(directory);
        if (components.Length == 0 || IsRoot(directory))
        {
            return null;
        }

        // A run of separators is only entered at its first character: a search that began at every character of one would
        // read the rest of the run each time, and a megabyte of slashes would take hours.
        var leading = directory[0] is '\\' or '/' ? @"(?<![\\/])(?<!%5[cC])(?<!%2[fF])" + Separators : string.Empty;
        return leading + string.Join(Separators, components.Select(component => $"(?:{Spelled(component)})"));
    }

    /// <summary>
    /// A path as a sentence written for a person gives it: <c>~</c> and the rest where the path is the home directory or
    /// inside it, by the rule this rewriter decides that by, and the path as it is anywhere else.
    /// </summary>
    public static string FromHome(string path, string home)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(home);

        if (!Inside(path, home))
        {
            return path;
        }

        // Past as many of the path's components as the home directory has, separators and all; the rest is kept as it was
        // written. Inside has said the path has at least that many, so this ends.
        var components = Components(home).Length;
        var at = 0;
        for (var passed = 0; passed < components;)
        {
            while (at < path.Length && path[at] is '\\' or '/')
            {
                at++;
            }

            var start = at;
            while (at < path.Length && path[at] is not ('\\' or '/'))
            {
                at++;
            }

            if (!string.IsNullOrWhiteSpace(path[start..at]))
            {
                passed++;
            }
        }

        return HomeMark + path[at..];
    }

    /// <summary>
    /// Whether a directory is the home directory or inside it, component by component, without regard to case where paths
    /// have none. A home directory that is only a root holds nothing here, as it matches nothing.
    /// </summary>
    private static bool Inside(string directory, string home)
    {
        var parent = Components(home);
        var child = Components(directory);
        var comparison = PathComparison;
        return parent.Length > 0
            && !IsRoot(home)
            && child.Length >= parent.Length
            && parent.Select((component, at) => string.Equals(component, child[at], comparison)).All(same => same);
    }

    /// <summary>Whether two spellings name one directory: component by component, without regard to case where paths have none.</summary>
    private static bool SameDirectory(string one, string other) =>
        Components(one).SequenceEqual(Components(other), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Whether a directory is only a root: <c>/</c>, a drive such as <c>D:\</c>, or a network share's root such as
    /// <c>\\server\share</c>, with or without the <c>\\?\</c> prefix of a long path.
    /// </summary>
    private static bool IsRoot(string directory)
    {
        var components = Components(directory).ToList();
        var network = IsNetworkPath(directory);
        if (network && components.Count > 0 && components[0] is "?" or ".")
        {
            components.RemoveAt(0);
            network = components.Count > 0 && components[0].Equals("UNC", StringComparison.OrdinalIgnoreCase);
            if (network)
            {
                components.RemoveAt(0);
            }
        }

        if (components.Count > 0 && components[0].Length == 2 && char.IsAsciiLetter(components[0][0]) && components[0][1] == ':')
        {
            components.RemoveAt(0);
        }
        else if (network)
        {
            components.RemoveRange(0, Math.Min(2, components.Count));
        }

        return components.Count == 0;
    }

    /// <summary>A path that starts with two separators: a network share, or a path read through the <c>\\?\</c> or <c>\\.\</c> prefix.</summary>
    private static bool IsNetworkPath(string directory) =>
        directory.Length >= 2 && directory[0] is '\\' or '/' && directory[1] is '\\' or '/';

    /// <summary>
    /// The last component of the home directory where the directory is a profile, which is to say where its parent is
    /// where profiles are kept: <c>Users</c> or <c>Documents and Settings</c> on Windows, whose root may be any drive
    /// and in any case, <c>Users</c> on macOS, and <c>/home</c> on Linux. Anywhere else — <c>/opt/jason</c>,
    /// <c>/var/empty</c> — the last component is a folder, and nothing says it is somebody's name.
    /// </summary>
    private static string? ProfileFolder(string home)
    {
        var components = Components(home);
        if (components.Length < 2)
        {
            return null;
        }

        var parent = components[^2];
        var isProfileRoot = parent.Equals("Users", StringComparison.OrdinalIgnoreCase)
            || parent.Equals("Documents and Settings", StringComparison.OrdinalIgnoreCase)
            || (components.Length == 2 && parent == "home" && home[0] is '\\' or '/');
        return isProfileRoot ? components[^1] : null;
    }

    private static string[] Components(string home) =>
        home.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Where(component => !string.IsNullOrWhiteSpace(component)).ToArray();
}
