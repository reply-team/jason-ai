namespace Jason.Contracts.Plugins;

/// <summary>
/// The names a field must not be given where credentials are not allowed to travel. A binding is the case this
/// exists for: it selects which identity a plugin should act as — an account, a workspace, a mailbox — while the
/// credential itself stays in the plugin's own store, reached through the environment variables the user granted.
/// A binding is meant to be read by the people who operate the installation, so a field named like a secret would
/// invite someone to paste one where it can be seen.
/// </summary>
/// <remarks>
/// The rule is enforced twice over, and the vocabulary is therefore spelled once here: a plugin's manifest may not
/// declare such a property, so an author learns it at reload rather than from a user's mistake, and a binding may
/// not carry such a key. Matching is a case-insensitive substring, because <c>api_token</c> and <c>tokenValue</c>
/// are the same mistake as <c>token</c>, and a name that merely reads like a secret is not a name worth keeping.
/// <para>
/// A name is read with its separators dropped, and so are the fragments. The rule is "reads like a credential";
/// the list below is how that question is asked, not what it says, so <c>x-api-key</c> — the way that name is
/// spelled almost everywhere it is spelled at all — must not get through because the list happens to write it
/// with an underscore. One name has one answer, however it is punctuated.
/// </para>
/// </remarks>
public static class SecretLikeNames
{
    /// <summary>The fragments a field name may not contain, in the order the documentation lists them.</summary>
    public static IReadOnlyList<string> Fragments { get; } =
    [
        "token",
        "secret",
        "password",
        "passwd",
        "api_key",
        "apikey",
        "credential",
        "private_key",
        "authorization",
        "bearer",
        "cookie",
    ];

    /// <summary>The same fragments with their separators gone, which is the form a name is compared against.</summary>
    private static readonly string[] Unpunctuated = Unpunctuate(Fragments);

    /// <summary>Whether a field of this name would read as a credential to whoever looks at it later.</summary>
    public static bool Matches(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var read = Unpunctuate(name);
        foreach (var fragment in Unpunctuated)
        {
            if (read.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name with everything that is not a letter or a digit taken out, so that punctuation cannot make one
    /// name look like another: <c>x-api-key</c>, <c>api.key</c>, <c>API_KEY</c> and <c>apikey</c> are one name.
    /// </summary>
    private static string Unpunctuate(string name)
    {
        var kept = new char[name.Length];
        var length = 0;
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                kept[length++] = character;
            }
        }

        return length == name.Length ? name : new string(kept, 0, length);
    }

    private static string[] Unpunctuate(IReadOnlyList<string> fragments)
    {
        var unpunctuated = new string[fragments.Count];
        for (var index = 0; index < fragments.Count; index++)
        {
            unpunctuated[index] = Unpunctuate(fragments[index]);
        }

        return unpunctuated;
    }
}
