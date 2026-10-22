using System.Globalization;
using System.Text;
using Jason.Contracts.Channels;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Channels;

public class ChannelRulesTests
{
    [Theory]
    [InlineData("Email", "email")]
    [InlineData("  LinkedIn  ", "linkedin")]
    [InlineData("whatsapp", "whatsapp")]
    [InlineData("crm_note", "crm_note")]
    public void A_channel_name_is_lower_cased_and_kept(string raw, string expected)
    {
        Assert.Equal(expected, ChannelRules.NormalizeChannelName(raw, out var refusal));
        Assert.Equal(ChannelRefusal.None, refusal);
    }

    [Theory]
    [InlineData("e-mail", ChannelRefusal.Invalid)]
    [InlineData("1email", ChannelRefusal.Invalid)]
    [InlineData("_email", ChannelRefusal.Invalid)]
    [InlineData("e mail", ChannelRefusal.Invalid)]
    [InlineData("", ChannelRefusal.Required)]
    [InlineData("   ", ChannelRefusal.Required)]
    [InlineData(null, ChannelRefusal.Required)]
    public void A_channel_name_outside_the_vocabulary_shape_is_refused(string? raw, ChannelRefusal expected)
    {
        Assert.Null(ChannelRules.NormalizeChannelName(raw, out var refusal));
        Assert.Equal(expected, refusal);
    }

    [Fact]
    public void A_channel_name_longer_than_the_column_is_refused()
    {
        Assert.Null(ChannelRules.NormalizeChannelName("c" + new string('x', 32), out var refusal));
        Assert.Equal(ChannelRefusal.Invalid, refusal);
    }

    [Fact]
    public void The_known_channels_are_the_ones_with_a_value_rule()
    {
        Assert.True(ChannelRules.IsKnown("email"));
        Assert.True(ChannelRules.IsKnown("phone"));
        Assert.True(ChannelRules.IsKnown("whatsapp"));
        Assert.True(ChannelRules.IsKnown("linkedin"));
        Assert.False(ChannelRules.IsKnown("telegram"));
    }

    [Theory]
    // Critical: email and phone normalization.
    [InlineData("email", "  Foo@Bar.COM ", "foo@bar.com", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S1)])]
    [InlineData("email", "jane.doe+tag@sub.example.co.uk", "jane.doe+tag@sub.example.co.uk")]
    // Critical: email and phone normalization.
    [InlineData("phone", "+1 (604) 555-0100", "+16045550100", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S1)])]
    [InlineData("whatsapp", " +380.44.123.45.67 ", "+380441234567")]
    [InlineData("telegram", "  @jane  ", "@jane")]
    public void A_value_is_normalized_by_the_rule_of_its_channel(string channel, string raw, string expected)
    {
        Assert.Equal(expected, ChannelRules.NormalizeValue(channel, raw, out var refusal));
        Assert.Equal(ChannelRefusal.None, refusal);
    }

    [Theory]
    [InlineData("email", "a@b")]
    [InlineData("email", "\"Name\" <a@b.co>")]
    [InlineData("email", "a b@c.co")]
    [InlineData("email", "not-an-address")]
    [InlineData("phone", "6045550100")]
    [InlineData("phone", "+0445550100")]
    [InlineData("whatsapp", "+")]
    [InlineData("linkedin", "in/jane")]
    [InlineData("linkedin", "mailto:jane@example.com")]
    public void A_value_its_channel_cannot_accept_is_invalid(string channel, string raw)
    {
        Assert.Null(ChannelRules.NormalizeValue(channel, raw, out var refusal));
        Assert.Equal(ChannelRefusal.Invalid, refusal);
    }

    [Theory]
    [InlineData("telegram", "")]
    [InlineData("linkedin", "   ")]
    [InlineData("email", null)]
    public void A_value_with_nothing_in_it_is_required(string channel, string? raw)
    {
        Assert.Null(ChannelRules.NormalizeValue(channel, raw, out var refusal));
        Assert.Equal(ChannelRefusal.Required, refusal);
    }

    [Theory]
    [InlineData("telegram")]
    [InlineData("linkedin")]
    public void A_value_longer_than_the_column_is_too_long(string channel)
    {
        var raw = channel == "linkedin" ? "https://www.linkedin.com/in/" + new string('a', ChannelRules.MaxValueLength) : new string('x', ChannelRules.MaxValueLength + 1);

        Assert.Null(ChannelRules.NormalizeValue(channel, raw, out var refusal));
        Assert.Equal(ChannelRefusal.TooLong, refusal);
        Assert.Null(ChannelRules.KeyOf(channel, raw));
    }

    [Fact]
    public void The_linkedin_rule_says_what_a_profile_link_looks_like()
    {
        Assert.Equal(
            "a linkedin value is a link to a person's LinkedIn profile, linkedin.com/in/<name>, where <name> is 1 to 100 letters, digits, combining marks, hyphens or underscores, starts with a letter or a digit, and has a mark only after a letter, a digit or another mark.",
            ChannelRules.RuleOf("linkedin"));
    }

    // -------------------------------------------------------------------------------------------------------
    // The LinkedIn key
    // -------------------------------------------------------------------------------------------------------

    // Critical: one LinkedIn key: canonical, case+fragment, percent-encoded, dot-segment, look-alike host.
    public static TheoryData<string, string?> CriticalLinkedInVectors() =>
        CriticalRows.Mark(LinkedInKeyVectors.Theory(), Family.F1, data => data is
            ["https://www.linkedin.com/in/marta-example", _]
            or ["HTTPS://WWW.LinkedIn.com/in/Marta-Example/#about", _]
            or ["https://www.linkedin.com/in/j%C3%B6rn-example", _]
            or ["https://www.linkedin.com/in/x/../marta-example", _]
            or ["https://linkedin.com.example.org/in/marta-example", _]);

    /// <summary>
    /// Every row of the one table: a profile link is keyed by its slug, decoded once, composed and lowercased, under the
    /// canonical host, and anything that names no LinkedIn profile is refused as invalid. The channel name is read as the
    /// runtime reads one, so the key is the same whatever its case.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalLinkedInVectors))]
    public void A_linkedin_value_is_keyed_by_its_slug_or_refused(string raw, string? key)
    {
        var keyed = ChannelRules.NormalizeValue("linkedin", raw, out var refusal);

        Assert.True(keyed == key, $"'{raw}' is keyed '{keyed ?? "(refused)"}', not '{key ?? "(refused)"}'.");
        Assert.True(
            refusal == (key is null ? ChannelRefusal.Invalid : ChannelRefusal.None),
            $"'{raw}' was answered {refusal}.");
        Assert.True(ChannelRules.KeyOf("LinkedIn", raw) == key, $"'{raw}' is keyed '{ChannelRules.KeyOf("LinkedIn", raw)}' under the name LinkedIn.");
    }

    /// <summary>"Invariant culture" is a claim about every culture a machine may run in, Turkish casing included.</summary>
    [Fact]
    [Critical(Family.F1, "culture-sensitive lower-casing")]
    public void The_key_is_the_same_in_a_turkish_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            foreach (var (raw, key) in LinkedInKeyVectors.Rows)
            {
                var keyed = ChannelRules.KeyOf("linkedin", raw);
                Assert.True(keyed == key, $"Under tr-TR '{raw}' is keyed '{keyed}', not '{key}'.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    /// <summary>A key is its own key, so a stored value compared as it stands and a value keyed again never differ.</summary>
    [Fact]
    public void A_key_is_its_own_key()
    {
        foreach (var (raw, _) in LinkedInKeyVectors.Rows)
        {
            if (ChannelRules.KeyOf("linkedin", raw) is { } key)
            {
                var again = ChannelRules.KeyOf("linkedin", key);
                Assert.True(again == key, $"'{raw}' is keyed '{key}', and that key is keyed '{again}'.");
            }
        }
    }

    /// <summary>
    /// The same over every code point as a slug of one letter, in upper and lower case alike: whatever the rule accepts
    /// it lowercases into a letter it accepts again and leaves alone, so idempotence does not rest on the rows above.
    /// </summary>
    [Fact]
    public void Every_one_letter_slug_the_rule_accepts_keys_to_a_key_that_is_its_own()
    {
        var problems = new List<string>();
        var accepted = 0;
        for (var point = 0; point <= 0x10FFFF; point++)
        {
            if (point is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var letter = char.ConvertFromUtf32(point);
            if (ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + letter) is not { } key)
            {
                continue;
            }

            accepted++;
            if (ChannelRules.KeyOf("linkedin", key) != key)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"U+{point:X4}"));
            }
        }

        Assert.True(accepted > 100_000, $"only {accepted} code points were accepted as a slug.");
        Assert.True(problems.Count == 0, $"{problems.Count} keys are not their own key: {string.Join(", ", problems.Take(20))}.");
    }

    /// <summary>
    /// The same over every letter the rule accepts followed by each of a few combining marks, in upper and lower case
    /// alike: the name is composed before it is lowercased and again after, so a capital that composes with no mark but
    /// whose lowercase does — <c>J</c> and a caron — still keys to a key that is its own.
    /// </summary>
    [Fact]
    public void Every_letter_with_a_mark_keys_to_a_key_that_is_its_own()
    {
        int[] marks = [0x0301, 0x0308, 0x030C, 0x0323, 0x0345, 0x093C];
        var problems = new List<string>();
        var accepted = 0;
        for (var point = 0; point <= 0x10FFFF; point++)
        {
            if (point is >= 0xD800 and <= 0xDFFF || !Rune.IsLetter(new Rune(point)))
            {
                continue;
            }

            foreach (var mark in marks)
            {
                var name = char.ConvertFromUtf32(point) + char.ConvertFromUtf32(mark);
                if (ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + name) is not { } key)
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"U+{point:X4} U+{mark:X4} refused"));
                    continue;
                }

                accepted++;
                if (ChannelRules.KeyOf("linkedin", key) != key)
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"U+{point:X4} U+{mark:X4} not its own key"));
                }
            }
        }

        Assert.True(accepted > 500_000, $"only {accepted} letters with a mark were accepted.");
        Assert.True(problems.Count == 0, $"{problems.Count} problems: {string.Join(", ", problems.Take(20))}.");
    }

    /// <summary>
    /// A key is bounded by its name alone: the canonical start and at most 100 code points of two UTF-16 units each, 228
    /// in all, whatever the input was, so a value stored before the key can be keyed with no bound on its own length.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "the key fits its column")]
    public void Every_key_of_an_accepted_input_is_at_most_228_characters()
    {
        Assert.Equal(28, "https://www.linkedin.com/in/".Length);
        var longest = 0;
        foreach (var (raw, _) in LinkedInKeyVectors.Rows)
        {
            if (ChannelRules.KeyOf("linkedin", raw) is { } key)
            {
                Assert.True(key.Length <= 228, $"'{raw}' is keyed '{key}', {key.Length} characters.");
                longest = Math.Max(longest, key.Length);
            }
        }

        Assert.True(longest == 228, $"the longest key the table holds is {longest} characters, so the bound is not reached.");
    }

    /// <summary>
    /// A value this runtime stored before the key is keyed by the channel's own rule, which bounds it, and never by the
    /// bound on a value given: the rules before wrote a letter outside ASCII as its escapes and kept a query, so a link
    /// given within 500 characters could be stored as more, and it still names its profile.
    /// </summary>
    [Fact]
    public void A_value_stored_before_the_key_is_keyed_with_no_bound_on_its_length()
    {
        var stored = "https://www.linkedin.com/in/" + string.Concat(Enumerable.Repeat("%d0%b0", 30)) + "?trk=" + new string('x', 300);
        Assert.Equal(513, stored.Length);

        Assert.Null(ChannelRules.KeyOf("linkedin", stored));
        Assert.Equal("https://www.linkedin.com/in/" + new string('\u0430', 30), ChannelRules.KeyOfStored("linkedin", stored));
        Assert.Equal("https://www.linkedin.com/in/" + new string('\u0430', 30), ChannelRules.KeyOfStored("LinkedIn", " " + stored + " "));
        Assert.Null(ChannelRules.KeyOfStored("linkedin", "https://www.linkedin.com/pub/marta-example/" + new string('x', 600)));
        Assert.Null(ChannelRules.KeyOfStored("linkedin", "https://www.linkedin.com/in/" + LinkedInKeyVectors.LongestSlug + new string('a', 600)));
        Assert.Null(ChannelRules.KeyOfStored("linkedin", " "));
        Assert.Null(ChannelRules.KeyOfStored("e mail", "marta@example.com"));
    }

    /// <summary>Within the bound on a value given, a stored value and a given one are keyed alike: the same rule, row by row.</summary>
    [Fact]
    public void A_stored_value_within_the_bound_is_keyed_as_a_given_one()
    {
        foreach (var (raw, key) in LinkedInKeyVectors.Rows)
        {
            var stored = ChannelRules.KeyOfStored("linkedin", raw);
            Assert.True(stored == key, $"'{raw}' stored is keyed '{stored ?? "(refused)"}', and given '{key ?? "(refused)"}'.");
        }
    }

    [Fact]
    public void A_lone_half_of_a_surrogate_pair_is_no_slug()
    {
        Assert.Null(ChannelRules.KeyOf("linkedin", LinkedInKeyVectors.LoneSurrogate));
    }

    /// <summary>
    /// A slug decoded from escapes is held to the same bound of code points as one written plainly, and the bound of the
    /// value as it was given comes first: 70 letters written as escapes and 30 written plainly are 100. The bound counts
    /// the composed name, so 100 accented letters each written as a letter and a mark are 100 too.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "length after percent-decoding")]
    public void The_bound_counts_code_points_after_the_decoding()
    {
        var given = string.Concat(Enumerable.Repeat("%C3%B6", 70)) + new string('ö', 30);
        var plain = new StringBuilder().Insert(0, "ö", 100).ToString();
        var decomposed = string.Concat(Enumerable.Repeat("e" + char.ConvertFromUtf32(0x0301), 100));
        var precomposed = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x00E9), 100));

        Assert.Equal("https://www.linkedin.com/in/" + plain, ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + given));
        Assert.Null(ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + given + "%C3%B6"));
        Assert.Equal("https://www.linkedin.com/in/" + precomposed, ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + decomposed));
        Assert.Equal(
            "https://www.linkedin.com/in/" + string.Concat(Enumerable.Repeat("\U00010428", 100)),
            ChannelRules.KeyOf("linkedin", "https://www.linkedin.com/in/" + string.Concat(Enumerable.Repeat("\U00010400", 100))));
    }

    /// <summary>The path a pasted link names, as the key ends; the link's whole key when it names a profile, else nothing.</summary>
    [Theory]
    [InlineData("https://www.linkedin.com/in/Ada-Lovelace/?trk=feed", "/in/ada-lovelace")]
    [InlineData("uk.linkedin.com/in/J%C3%96RN-Example", "/in/jörn-example")]
    [InlineData("https://www.linkedin.com/company/example", null)]
    [InlineData("ada lovelace", null)]
    public void A_profile_path_is_the_end_of_the_key(string text, string? path)
    {
        Assert.Equal(path, ChannelRules.LinkedInProfilePath(text));
    }

    [Theory]
    // Critical: the stored key per channel, and none for an invalid value.
    [InlineData("LinkedIn", "https://www.linkedin.com/in/Marta-Example/", "https://www.linkedin.com/in/marta-example", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S1)])]
    // Critical: the stored key per channel, and none for an invalid value.
    [InlineData("email", " Marta@Example.com ", "marta@example.com", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S1)])]
    [InlineData("telegram", " @marta ", "@marta")]
    // Critical: the stored key per channel, and none for an invalid value.
    [InlineData("email", "marta", null, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S1)])]
    [InlineData("e mail", "marta@example.com", null)]
    [InlineData("linkedin", "", null)]
    public void A_key_is_what_the_runtime_stores_for_the_value_on_that_channel(string channel, string value, string? key)
    {
        Assert.Equal(key, ChannelRules.KeyOf(channel, value));
    }
}
