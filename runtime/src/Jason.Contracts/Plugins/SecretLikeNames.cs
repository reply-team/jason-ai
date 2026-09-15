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

    /// <summary>Whether a field of this name would read as a credential to whoever looks at it later.</summary>
    public static bool Matches(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var fragment in Fragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
