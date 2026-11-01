namespace Jason.Contracts.Tests.Channels;

/// <summary>
/// The one table of LinkedIn profile links and the key the runtime keeps for each: a link as somebody may have typed or
/// pasted it, and <c>https://www.linkedin.com/in/&lt;slug&gt;</c>, or null where the rule refuses it. The rule's own tests
/// read it, the plugin host answers <c>host.channelKey</c> over it through the real host, and the migration that re-keys
/// stored links is fed it, so each of them is held to the same rows. Every link here is made up.
/// </summary>
public static class LinkedInKeyVectors
{
    private const string Key = "https://www.linkedin.com/in/";

    /// <summary>A slug of exactly as many code points as the rule takes, and one more.</summary>
    public static readonly string LongestSlug = new('a', 100);

    /// <summary>
    /// The longest slug in UTF-16 units: as many code points as the rule takes, each a letter outside the Basic
    /// Multilingual Plane, so each is two units, and its lowercase is too.
    /// </summary>
    public static readonly string LongestAstralSlug = string.Concat(Enumerable.Repeat("\U00010400", 100));

    public static IReadOnlyList<(string Raw, string? Key)> Rows { get; } =
    [
        // The key itself, and the trailing slashes, sub-paths, queries and fragments a profile link carries.
        ("https://www.linkedin.com/in/marta-example", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example/", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example//", Key + "marta-example"),
        ("https://www.linkedin.com//in//marta-example", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example?queryString", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example?trk=public_profile_browsemap", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example?originalSubdomain=uk", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example?", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example/?trk=feed", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example/update/", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example/recent-activity/all/", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example/en", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example&foo=bar", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example#:~:text=Marta", Key + "marta-example"),

        // Case, scheme, host, port and user name.
        ("https://www.linkedin.com/in/Marta-Example", Key + "marta-example"),
        ("HTTPS://WWW.LinkedIn.com/in/Marta-Example/#about", Key + "marta-example"),
        ("https://www.linkedin.com/IN/marta-example", Key + "marta-example"),
        ("https://www.linkedin.com/In/IRMAK-Sample", Key + "irmak-sample"),
        ("https://ae.linkedin.com/in/marta-example", Key + "marta-example"),
        ("https://uk.linkedin.com/in/marta-example", Key + "marta-example"),
        ("https://m.linkedin.com/in/marta-example", Key + "marta-example"),
        ("https://linkedin.com/in/marta-example", Key + "marta-example"),
        ("linkedin.com/in/marta-example", Key + "marta-example"),
        ("www.linkedin.com/in/Marta-Example/", Key + "marta-example"),
        ("http://www.linkedin.com/in/marta-example", Key + "marta-example"),
        ("HTTP://www.linkedin.com:80/in/marta-example", Key + "marta-example"),
        ("https://www.linkedin.com:443/in/marta-example", Key + "marta-example"),
        ("https://www.linkedin.com:/in/marta-example", Key + "marta-example"),
        ("https://marta@www.linkedin.com/in/marta-example", Key + "marta-example"),

        // Escapes, decoded once, and letters outside ASCII, kept as letters.
        ("https://www.linkedin.com/in/marta%2Dexample", Key + "marta-example"),
        ("https://www.linkedin.com/in/%4Darta-example", Key + "marta-example"),
        ("https://www.linkedin.com/in/j%C3%B6rn-example", Key + "jörn-example"),
        ("https://www.linkedin.com/in/j%c3%b6rn-example", Key + "jörn-example"),
        ("https://www.linkedin.com/in/jörn-example", Key + "jörn-example"),
        ("https://www.linkedin.com/in/JÖRN-EXAMPLE", Key + "jörn-example"),
        ("https://www.linkedin.com/in/J%C3%96RN-Example", Key + "jörn-example"),
        ("https://www.linkedin.com/in/ΝΙΚΟΣ-sample", Key + "νικοσ-sample"),
        ("https://www.linkedin.com/in/\U00010400\U00010401-sample", Key + "\U00010428\U00010429-sample"),
        ("https://www.linkedin.com/in/ACoAAAm4rTaE1x", Key + "acoaaam4rtae1x"),
        ("https://www.linkedin.com/in/marta_example_2", Key + "marta_example_2"),
        ("https://www.linkedin.com/in/" + LongestSlug, Key + LongestSlug),
        ("https://www.linkedin.com/in/" + LongestAstralSlug, Key + string.Concat(Enumerable.Repeat("\U00010428", 100))),

        // Names whose letters carry marks: Devanagari and Thai vowel signs, an Arabic diacritic, and an accent written
        // precomposed or as a letter and a combining mark, which are one key.
        ("https://www.linkedin.com/in/नमूना", Key + "नमूना"),
        ("https://www.linkedin.com/in/%E0%A4%A8%E0%A4%AE%E0%A5%82%E0%A4%A8%E0%A4%BE", Key + "नमूना"),
        ("https://www.linkedin.com/in/ตัวอย่าง", Key + "ตัวอย่าง"),
        ("https://www.linkedin.com/in/مِثال", Key + "مِثال"),
        ("https://www.linkedin.com/in/jos%C3%A9-sample", Key + "josé-sample"),
        ("https://www.linkedin.com/in/jose%CC%81-sample", Key + "josé-sample"),
        ("https://www.linkedin.com/in/jose\u0301-sample", Key + "josé-sample"),
        ("https://www.linkedin.com/in/JOSE\u0301-SAMPLE", Key + "josé-sample"),

        // A mark after a digit, which no precomposed letter holds, so the key keeps the two.
        ("https://www.linkedin.com/in/marta-2\u0301", Key + "marta-2\u0301"),

        // White space and a byte-order mark at an edge.
        ("\uFEFFhttps://www.linkedin.com/in/marta-example\u00A0", Key + "marta-example"),
        ("  https://www.linkedin.com/in/marta-example\t\uFEFF", Key + "marta-example"),

        // Not a profile link: nothing to key, or a path that names no person.
        ("\uFEFF", null),
        ("https://www.linkedin.com", null),
        ("https://www.linkedin.com/", null),
        ("https://www.linkedin.com/in", null),
        ("https://www.linkedin.com/in/", null),
        ("https://www.linkedin.com/in//", null),
        ("https://www.linkedin.com/in/?x", null),
        ("https://www.linkedin.com/in/#x", null),
        ("https://www.linkedin.com#/in/marta-example", null),
        ("https://www.linkedin.com?/in/marta-example", null),
        ("https://www.linkedin.com/pub/marta-example/1/2/3", null),
        ("https://www.linkedin.com/company/example", null),
        ("https://www.linkedin.com/school/example", null),
        ("https://www.linkedin.com/sales/people/ACwAAA1,NAME_SEARCH,x", null),
        ("https://www.linkedin.com/talent/profile/marta-example", null),
        ("https://www.linkedin.com/profile/view?id=12345", null),
        ("https://www.linkedin.com/en/in/marta-example", null),

        // A spelling that a browser or .NET's Uri would read as a profile other than the one the rule keys is refused,
        // never followed: a step to the same place or back, written plainly or as escapes, which both follow; a backslash
        // anywhere in the value, which a browser reads as a slash before the host's end and in the path, so that a user
        // name holding one hides the host it opens — in a query or a fragment one is harmless in itself, and is refused
        // there too so that one check covers the whole value; a tab, a line feed or a carriage return anywhere in the
        // value once its edges are trimmed, which a browser drops.
        ("https://www.linkedin.com/in/x/../marta-example", null),
        ("https://www.linkedin.com/in/x/%2E%2e/marta-example", null),
        ("https://www.linkedin.com/in/marta-example/.", null),
        ("https://www.linkedin.com/in/x/..\\marta-example", null),
        ("https://www.linkedin.com/in/x/.\t./marta-example", null),
        ("https://example.com\\@www.linkedin.com/in/x", null),
        ("example.com\\@www.linkedin.com/in/marta-example", null),
        ("https://www.linkedin.com/in/marta-example/\\x", null),
        ("https://www.linkedin.com/in/marta\\example", null),
        ("https://www.linkedin.com/in/marta-example?ref=a\\b", null),
        ("https://www.linkedin.com/in/marta-example#a\\b", null),

        // A backslash written as an escape is no slash to a browser or to .NET's Uri: in a user name it stays the user
        // name's, and the host is the one after the '@'; after the host it is the host's, which no host may hold; in a
        // query it is the query's; in the name it is a letter outside the alphabet.
        ("https://example.com%5C@www.linkedin.com/in/marta-example", Key + "marta-example"),
        ("https://www.linkedin.com%5C/in/marta-example", null),
        ("https://www.linkedin.com/in/marta-example?ref=a%5Cb", Key + "marta-example"),
        ("https://www.linkedin.com/in/marta-example%5C", null),

        // Not LinkedIn: another host, one that only ends in its letters, a wrapper around a link, a port, a scheme.
        ("https://example.com/in/marta-example", null),
        ("https://notlinkedin.com/in/marta-example", null),
        ("https://linkedin.com.example.org/in/marta-example", null),
        ("https://.linkedin.com/in/marta-example", null),
        ("https://www..linkedin.com/in/marta-example", null),
        ("https://www.linkedin.com./in/marta-example", null),
        ("https://www.lin\u212Aedin.com/in/marta-example", null),
        ("https://www.linkedin.com@example.com/in/marta-example", null),
        ("https://search.example.com/url?q=https%3A%2F%2Fwww.linkedin.com%2Fin%2Fmarta-example", null),
        ("https://www.linkedin.com:8443/in/marta-example", null),
        ("https://www.linkedin.com:0443/in/marta-example", null),
        ("https://www.linkedin.com:80/in/marta-example", null),
        ("http://www.linkedin.com:443/in/marta-example", null),
        ("ftp://www.linkedin.com/in/marta-example", null),
        ("mailto:marta@example.com", null),
        ("https:/www.linkedin.com/in/marta-example", null),
        ("//www.linkedin.com/in/marta-example", null),
        ("https://www.linkedin.com\\in\\marta-example", null),

        // A slug outside the alphabet, after its one decoding.
        ("https://www.linkedin.com/in/marta example", null),
        ("https://www.linkedin.com/in/marta%20example", null),
        ("https://www.linkedin.com/in/marta+example", null),
        ("https://www.linkedin.com/in/marta.example", null),
        ("https://www.linkedin.com/in/marta@example", null),
        ("https://www.linkedin.com/in/marta;example", null),
        ("https://www.linkedin.com/in/marta%2Fexample", null),
        ("https://www.linkedin.com/in/marta%252Dexample", null),
        ("https://www.linkedin.com/in/%2E%2E", null),
        ("https://www.linkedin.com/in/marta%00", null),
        ("https://www.linkedin.com/in/marta-\U0001F600", null),
        ("https://www.linkedin.com/in/" + LongestSlug + "a", null),
        ("https://www.linkedin.com/in/" + LongestAstralSlug + "\U00010400", null),

        // A name that starts with anything but a letter or a digit, and a mark that follows nothing it can mark.
        ("https://www.linkedin.com/in/-marta-example", null),
        ("https://www.linkedin.com/in/_marta", null),
        ("https://www.linkedin.com/in/%CC%81", null),
        ("https://www.linkedin.com/in/\u0301", null),
        ("https://www.linkedin.com/in/marta-\u0301example", null),
        ("https://www.linkedin.com/in/marta_\u0301example", null),
        ("https://www.linkedin.com/in/marta\u20DD", null),

        // A percent sign that starts no escape, and escapes that are no UTF-8.
        ("https://www.linkedin.com/in/50%off", null),
        ("https://www.linkedin.com/in/a%4%5F", null),
        ("https://www.linkedin.com/in/marta%", null),
        ("https://www.linkedin.com/in/marta%4", null),
        ("https://www.linkedin.com/in/marta%C3", null),
        ("https://www.linkedin.com/in/marta%E2%82", null),
        ("https://www.linkedin.com/in/%C0%80marta", null),
        ("https://www.linkedin.com/in/%ED%A0%80", null),
        ("https://www.linkedin.com/in/%F4%90%80%80", null),
        ("https://www.linkedin.com/in/%FFmarta", null),
    ];

    /// <summary>A lone half of a surrogate pair, which no JSON document carries intact, so it is held here alone.</summary>
    public const string LoneSurrogate = "https://www.linkedin.com/in/marta\uD800";

    public static TheoryData<string, string?> Theory()
    {
        var data = new TheoryData<string, string?>();
        foreach (var (raw, key) in Rows)
        {
            data.Add(raw, key);
        }

        return data;
    }
}
