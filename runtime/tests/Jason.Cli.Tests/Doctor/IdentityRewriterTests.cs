using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// A diagnostic bundle is sent to somebody else, so the names of the installation it came from are rewritten before it
/// is written: the home directory, the account, the machine, its domain and any Windows account identifier. Every name
/// here is made up and handed in, so what the tests say does not depend on whose machine runs them.
/// </summary>
public sealed class IdentityRewriterTests
{
    private const string ThisSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private static readonly IdentityFacts Windows = new(@"C:\Users\Ada", "Ada", "WORKSTATION-7", "CORP", ThisSid);

    private static readonly IdentityFacts Unix = new("/home/ada", "ada", "build-host", null, null);

    private static IdentityFacts WithUser(string user) => new(@"C:\Users\Someone", user, "WORKSTATION-7", null, null);

    /// <summary>The one string a document of one string says, once the names are rewritten.</summary>
    private static string Rewritten(IdentityFacts facts, string text) =>
        new IdentityRewriter(facts).Rewrite(JsonValue.Create(text)!).GetValue<string>();

    [Theory]
    [InlineData(@"C:\Users\Ada", "~")]
    [InlineData(@"C:\Users\Ada\.jason\logs", @"~\.jason\logs")]
    [InlineData("C:/Users/Ada/.jason/logs", "~/.jason/logs")]
    [InlineData(@"C:\Users\Ada\", @"~\")]
    public void The_home_directory_is_a_tilde_whichever_way_its_separators_lean(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Windows, text));

    [Fact]
    public void Either_separator_matches_whichever_one_the_home_directory_is_written_with()
    {
        Assert.Equal(@"~\settings.json", Rewritten(Unix, @"\home\ada\settings.json"));
        Assert.Equal("~/settings.json", Rewritten(Windows, "C:/Users/Ada/settings.json"));
    }

    [Fact]
    public void The_home_directory_is_found_inside_a_longer_string()
    {
        Assert.Equal(
            @"Could not open '~\.jason\config\settings.json' for reading: access denied",
            Rewritten(Windows, @"Could not open 'C:\Users\Ada\.jason\config\settings.json' for reading: access denied"));
        Assert.Equal(
            "Loaded ~/.jason/plugins/reply and ~/.jason/plugins/fake",
            Rewritten(Unix, "Loaded /home/ada/.jason/plugins/reply and /home/ada/.jason/plugins/fake"));
    }

    /// <summary>A path that was put into JSON text and then into a string again has its backslashes doubled: the same directory.</summary>
    [Fact]
    public void A_json_escaped_home_directory_is_the_home_directory()
    {
        Assert.Equal(
            @"{""path"":""~\\.jason\\logs""}",
            Rewritten(Windows, @"{""path"":""C:\\Users\\Ada\\.jason\\logs""}"));
    }

    [Fact]
    public void On_Windows_the_home_directory_matches_in_whatever_case_the_path_was_written()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows paths are the same path in another case.");

        Assert.Equal(@"~\x", Rewritten(Windows, @"c:\USERS\ada\x"));
    }

    [Fact]
    public void Elsewhere_the_home_directory_matches_in_the_case_it_has()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the same path in another case is the same path.");

        var text = Rewritten(Unix, "/home/ADA/x");

        Assert.DoesNotContain('~', text);
        Assert.DoesNotContain("ADA", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_directory_that_merely_starts_with_the_same_letters_is_not_the_home_directory()
    {
        Assert.Equal(@"C:\Users\Adam\x", Rewritten(Windows, @"C:\Users\Adam\x"));
        Assert.Equal(@"C:\Users\Ada2\x", Rewritten(Windows, @"C:\Users\Ada2\x"));
        Assert.Equal("/home/adam/x", Rewritten(Unix, "/home/adam/x"));
    }

    /// <summary>
    /// The home directory goes first: its last component is the user name, and had the name been rewritten first the
    /// path would read <c>C:\Users\&lt;user&gt;</c>, which says where it was and not that it was the home directory.
    /// </summary>
    [Fact]
    public void The_home_directory_is_rewritten_before_the_user_name_inside_it()
    {
        Assert.Equal(@"~\notes.txt", Rewritten(Windows, @"C:\Users\Ada\notes.txt"));
        Assert.Equal("~/notes.txt", Rewritten(Unix, "/home/ada/notes.txt"));
    }

    [Fact]
    public void A_home_directory_that_is_only_a_root_rewrites_nothing_rather_than_every_separator()
    {
        var facts = new IdentityFacts("/", "service", "build-host", null, null);

        Assert.Equal("/var/lib/jason and /tmp", Rewritten(facts, "/var/lib/jason and /tmp"));
    }

    [Fact]
    public void A_home_directory_with_a_trailing_separator_is_still_the_home_directory()
    {
        var facts = new IdentityFacts(@"C:\Users\Ada\", "Ada", "WORKSTATION-7", null, null);

        Assert.Equal(@"~\x", Rewritten(facts, @"C:\Users\Ada\x"));
    }

    [Fact]
    public void The_user_name_is_a_placeholder_standing_alone()
    {
        Assert.Equal("user <user> signed in", Rewritten(Windows, "user Ada signed in"));
        Assert.Equal("owner=<user>;", Rewritten(Windows, "owner=Ada;"));
    }

    [Fact]
    public void The_user_name_is_a_placeholder_inside_a_path_that_is_not_the_home_directory()
    {
        Assert.Equal(@"D:\work\<user>\notes", Rewritten(Windows, @"D:\work\Ada\notes"));
        Assert.Equal("/srv/<user>/mail", Rewritten(Unix, "/srv/ada/mail"));
    }

    [Fact]
    public void The_user_name_is_found_in_any_case_because_a_name_is_still_the_name_in_capitals()
    {
        Assert.Equal("<user> and <user>", Rewritten(Windows, "ADA and ada"));
    }

    [Fact]
    public void A_user_name_inside_a_longer_word_or_number_is_not_the_user_name()
    {
        Assert.Equal("Adam Adaptive Ada2 2Ada Cadabra", Rewritten(Windows, "Adam Adaptive Ada2 2Ada Cadabra"));
    }

    /// <summary>
    /// A user who is called <c>log</c> or <c>a</c> makes every word that holds it a candidate. The name is matched as a
    /// whole token and never as a substring, so what is rewritten is the name and not the words around it; the cost
    /// that remains, a word that is the name being rewritten wherever it stands alone, is the safe side to be wrong on.
    /// </summary>
    [Theory]
    [InlineData("log", "logs and catalog and Logging and log2 and 2log", "logs and catalog and Logging and log2 and 2log")]
    [InlineData("log", "the log said: log", "the <user> said: <user>")]
    [InlineData("log", "logs\\log\\logs.txt", "logs\\<user>\\logs.txt")]
    [InlineData("a", "a failure in the data, not Alpha", "<user> failure in the data, not Alpha")]
    [InlineData("a", "an apple", "an apple")]
    public void A_user_name_that_is_an_ordinary_word_is_rewritten_as_a_token_and_never_as_a_substring(string user, string text, string expected) =>
        Assert.Equal(expected, Rewritten(WithUser(user), text));

    [Fact]
    public void A_user_name_with_characters_that_mean_something_to_a_pattern_is_matched_as_it_is_written()
    {
        var facts = WithUser("a.b+c");

        Assert.Equal("<user>", Rewritten(facts, "a.b+c"));
        Assert.Equal("axb+c aabbc", Rewritten(facts, "axb+c aabbc"));
    }

    /// <summary>The name is Cyrillic "Ada", then the same in capitals, then a longer name that begins with it.</summary>
    [Fact]
    public void A_user_name_in_another_alphabet_is_bounded_by_letters_of_that_alphabet_too()
    {
        var facts = WithUser("Ада");

        Assert.Equal("<user> <user>", Rewritten(facts, "Ада АДА"));
        Assert.Equal("Адам", Rewritten(facts, "Адам"));
    }

    [Fact]
    public void A_user_name_made_of_two_words_is_one_name()
    {
        var facts = WithUser("Ada Lovelace");

        Assert.Equal("<user> and Ada and Lovelace", Rewritten(facts, "Ada Lovelace and Ada and Lovelace"));
    }

    [Fact]
    public void The_machine_name_is_a_placeholder_as_a_whole_token_in_any_case()
    {
        Assert.Equal("host <machine> refused; <machine> too", Rewritten(Windows, "host WORKSTATION-7 refused; workstation-7 too"));
        Assert.Equal("WORKSTATION-70 and WORKSTATION-7x and 1WORKSTATION-7", Rewritten(Windows, "WORKSTATION-70 and WORKSTATION-7x and 1WORKSTATION-7"));
    }

    [Fact]
    public void The_domain_is_a_placeholder_where_it_differs_from_the_machine()
    {
        Assert.Equal(@"<domain>\<user> on <machine>", Rewritten(Windows, @"CORP\Ada on WORKSTATION-7"));
    }

    [Fact]
    public void A_domain_that_is_the_machine_name_is_the_machine_name_and_not_a_second_name()
    {
        var facts = new IdentityFacts(@"C:\Users\Ada", "Ada", "WORKSTATION-7", "workstation-7", null);

        Assert.Equal(@"<machine>\<user>", Rewritten(facts, @"WORKSTATION-7\Ada"));
    }

    [Fact]
    public void A_domain_that_is_not_known_rewrites_nothing()
    {
        var facts = new IdentityFacts(@"C:\Users\Ada", "Ada", "WORKSTATION-7", null, null);

        Assert.Equal(@"CORP\<user>", Rewritten(facts, @"CORP\Ada"));
    }

    [Fact]
    public void This_accounts_identifier_is_a_placeholder_wherever_it_stands_including_in_a_task_name()
    {
        Assert.Equal("Jason-<sid>", Rewritten(Windows, $"Jason-{ThisSid}"));
        Assert.Equal(@"\Jason\Jason-<sid>: ready", Rewritten(Windows, $@"\Jason\Jason-{ThisSid}: ready"));
    }

    [Fact]
    public void Any_other_accounts_identifier_in_free_text_is_a_placeholder_as_well()
    {
        Assert.Equal(
            "owner <sid> and <sid> and <sid>",
            Rewritten(Windows, "owner S-1-5-21-9-8-7-500 and S-1-5-21-4000000000-5-6 and s-1-5-21-1-2-3-4"));
    }

    /// <summary>A Microsoft or work account's identifier is not one of the long local kind, and it is still this account's.</summary>
    [Fact]
    public void This_accounts_identifier_is_rewritten_even_when_it_is_not_of_the_usual_kind()
    {
        var facts = new IdentityFacts(@"C:\Users\Ada", "Ada", "WORKSTATION-7", null, "S-1-12-1-111-222-333-444");

        Assert.Equal("task <sid>", Rewritten(facts, "task S-1-12-1-111-222-333-444"));
    }

    [Fact]
    public void A_well_known_identifier_that_names_no_account_is_left_alone()
    {
        Assert.Equal("run as S-1-5-18 and S-1-5-32-544", Rewritten(Windows, "run as S-1-5-18 and S-1-5-32-544"));
    }

    /// <summary>
    /// A name that is also a word in a placeholder must not be found inside the placeholder an earlier rewrite wrote:
    /// all the names are looked for in the text as it was, in one pass.
    /// </summary>
    [Fact]
    public void A_placeholder_is_never_read_again_as_text_to_rewrite()
    {
        var facts = new IdentityFacts("/home/ada", "Ada", "user", "machine", null);

        Assert.Equal("<user>@<machine>:<domain>", Rewritten(facts, "Ada@user:machine"));
        Assert.Equal("<user> <machine> <domain>", Rewritten(facts, "Ada user machine"));
    }

    /// <summary>When two names meet at the same place, the longer one is the one that is meant.</summary>
    [Fact]
    public void Of_two_names_that_begin_in_the_same_place_the_longer_wins()
    {
        var facts = new IdentityFacts(@"C:\Users\desk", "desk", "desk-7", null, null);

        Assert.Equal("<machine> and <user>", Rewritten(facts, "desk-7 and desk"));
    }

    [Fact]
    public void A_name_that_is_not_there_to_rewrite_is_skipped_rather_than_matching_everything()
    {
        var facts = new IdentityFacts("", "", "", "", "");

        Assert.Equal(@"C:\Users\Ada says hello", Rewritten(facts, @"C:\Users\Ada says hello"));
    }

    [Fact]
    public void Every_string_of_a_document_is_rewritten_keys_included_and_nothing_else_changes()
    {
        var input = JsonNode.Parse(
            """
            {
              "C:\\Users\\Ada\\.jason": { "owner": "Ada", "count": 3, "ratio": 0.5, "ok": true, "none": null },
              "Ada": ["WORKSTATION-7", 4, [ "CORP\\Ada" ], { "host": "WORKSTATION-7" }],
              "plain": "nothing to hide"
            }
            """)!;
        var before = input.ToJsonString();
        var expected = JsonNode.Parse(
            """
            {
              "~\\.jason": { "owner": "<user>", "count": 3, "ratio": 0.5, "ok": true, "none": null },
              "<user>": ["<machine>", 4, [ "<domain>\\<user>" ], { "host": "<machine>" }],
              "plain": "nothing to hide"
            }
            """)!;

        var output = new IdentityRewriter(Windows).Rewrite(input);

        Assert.True(JsonNode.DeepEquals(expected, output), output.ToJsonString());
        Assert.Equal(before, input.ToJsonString());
    }

    [Fact]
    public void What_is_written_still_parses_to_what_was_rewritten()
    {
        var input = new JsonObject
        {
            ["C:\\Users\\Ada\\a \"quoted\" key"] = JsonValue.Create("line one\nC:\\Users\\Ada\\x\ttab and \"quotes\" and Ada"),
            ["list"] = new JsonArray(JsonValue.Create("WORKSTATION-7"), JsonValue.Create(1)),
        };

        var output = new IdentityRewriter(Windows).Rewrite(input);
        var reparsed = JsonNode.Parse(output.ToJsonString());

        Assert.True(JsonNode.DeepEquals(output, reparsed));
        Assert.DoesNotContain("Ada", output.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("WORKSTATION", output.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>Two keys that become one must not lose a value, and must not stop a bundle from being written.</summary>
    [Fact]
    public void Two_keys_that_are_the_same_once_rewritten_are_both_kept()
    {
        var input = JsonNode.Parse("""{ "Ada": 1, "ADA": 2, "ada (2)": 3 }""")!;

        var output = (JsonObject)new IdentityRewriter(Windows).Rewrite(input);

        Assert.Equal(3, output.Count);
        Assert.Equal([1, 2, 3], output.Select(pair => pair.Value!.GetValue<int>()).Order());
    }

    private static IdentityFacts Long(string user, string home) => new(home, user, "WORKSTATION-7", null, null);

    /// <summary>
    /// Windows keeps a second, short name for a folder whose name is long or has a space in it: the first six letters in
    /// capitals and a number. It is what a path reads like where it was never put through a call that spells it out —
    /// the raw value of a temporary-directory variable, a command line as it was launched, another program's output —
    /// and its six letters are most of a name.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\JOHNSM~1\AppData\Local\Temp\x", @"C:\Users\<user>\AppData\Local\Temp\x")]
    [InlineData(@"C:\Users\JOHNSM~12\x", @"C:\Users\<user>\x")]
    [InlineData(@"c:\users\johnsm~1\x", @"c:\users\<user>\x")]
    [InlineData("JASON_DATA_DIR=C:/Users/JOHNSM~1/data", "JASON_DATA_DIR=C:/Users/<user>/data")]
    public void The_short_alias_of_a_long_user_name_is_the_user_name(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Long("JohnSmith", @"C:\Users\JohnSmith"), text));

    [Fact]
    public void A_user_name_with_a_space_has_a_short_alias_without_it()
    {
        Assert.Equal(@"C:\Users\<user>\x", Rewritten(Long("John Smith", @"C:\Users\John Smith"), @"C:\Users\JOHNSM~1\x"));
    }

    [Fact]
    public void The_short_alias_of_the_last_component_of_the_home_directory_is_a_name_as_well()
    {
        Assert.Equal(@"D:\<user>\x", Rewritten(Long("dev", @"C:\Users\LongFolderName"), @"D:\LONGFO~1\x"));
    }

    /// <summary>A name of eight letters or fewer with nothing in it that a short name cannot hold is its own short name, and has no alias to find.</summary>
    [Theory]
    [InlineData("Jonathan", @"C:\Users\JONATH~1\x", @"C:\Users\JONATH~1\x")]
    [InlineData("Jonathans", @"C:\Users\JONATH~1\x", @"C:\Users\<user>\x")]
    [InlineData("Jonathans", @"C:\Users\JONATH~ and JONATH~x and JONATH1", @"C:\Users\JONATH~ and JONATH~x and JONATH1")]
    [InlineData("Jonathans", @"C:\Users\XJONATH~1\x", @"C:\Users\XJONATH~1\x")]
    public void A_short_alias_is_looked_for_only_where_there_can_be_one_and_only_as_a_whole_token(string user, string text, string expected) =>
        Assert.Equal(expected, Rewritten(Long(user, @"C:\Users\Someone"), text));

    /// <summary>
    /// When four folders of one stem already have their first alias, Windows names the next by the first two letters,
    /// four digits of hexadecimal and a number: <c>JO1A2B~1</c> for <c>JohnSmith</c>.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\JO1A2B~1\x", @"C:\Users\<user>\x")]
    [InlineData(@"c:\users\jo1a2b~12\x", @"c:\users\<user>\x")]
    [InlineData("JOABCD~3 and joffff~1", "<user> and <user>")]
    [InlineData(@"C:\Users\JO1A2~1\x", @"C:\Users\JO1A2~1\x")]
    [InlineData(@"C:\Users\JO1A2B7~1\x", @"C:\Users\JO1A2B7~1\x")]
    [InlineData(@"C:\Users\JOXYZW~1\x", @"C:\Users\JOXYZW~1\x")]
    [InlineData(@"C:\Users\XJO1A2B~1\x", @"C:\Users\XJO1A2B~1\x")]
    [InlineData(@"C:\Users\JO1A2B~\x", @"C:\Users\JO1A2B~\x")]
    public void The_second_form_of_a_short_alias_is_the_user_name_too(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Long("JohnSmith", @"C:\Users\JohnSmith"), text));

    [Fact]
    public void The_second_form_of_a_short_alias_is_looked_for_only_where_the_first_could_be()
    {
        Assert.Equal(@"C:\Users\JO1A2B~1\x", Rewritten(Long("Jonathan", @"C:\Users\Jonathan"), @"C:\Users\JO1A2B~1\x"));
    }

    /// <summary>
    /// A name with a period in it is cut at its last one: what is before it, without the spaces and the other periods,
    /// gives the six letters, and the first three letters after it are kept as an extension after the number — so
    /// <c>artem.kosolap</c> is <c>ARTEM~1.KOS</c> and not <c>ARTEMK~1</c>. The extension is part of the alias, and is
    /// rewritten with it rather than left behind.
    /// </summary>
    [Theory]
    [InlineData("artem.kosolap", @"C:\Users\ARTEM~1.KOS\AppData\Local\Temp\x", @"C:\Users\<user>\AppData\Local\Temp\x")]
    [InlineData("artem.kosolap", @"c:\users\artem~12.kos\x", @"c:\users\<user>\x")]
    [InlineData("artem.kosolap", @"C:\Users\ARTEM~1\x", @"C:\Users\<user>\x")]
    [InlineData("artem.kosolap", @"C:\Users\AR1A2B~1.KOS\x", @"C:\Users\<user>\x")]
    [InlineData("john.smith", @"C:\Users\JOHN~1.SMI\x", @"C:\Users\<user>\x")]
    [InlineData("maria.garcia-lopez", @"C:\Users\MARIA~1.GAR\x", @"C:\Users\<user>\x")]
    [InlineData("j.r.r.tolkien", @"C:\Users\JRR~1.TOL\x", @"C:\Users\<user>\x")]
    [InlineData("john.smith.longname", @"C:\Users\JOHNSM~1.LON\x", @"C:\Users\<user>\x")]
    [InlineData("JohnSmith.000", @"C:\Users\JOHNSM~1.000\x", @"C:\Users\<user>\x")]
    [InlineData("John Smith", @"C:\Users\JOHNSM~1\x", @"C:\Users\<user>\x")]
    public void The_short_alias_of_a_name_with_a_period_keeps_the_extension_the_way_ntfs_writes_it(string user, string text, string expected) =>
        Assert.Equal(expected, Rewritten(Long(user, @"C:\Users\" + user), text));

    /// <summary>
    /// A name ends where a word does, and a percent sign is where one does: only the percent escapes of the bytes of a
    /// longer letter — a lead byte and the byte that follows it — make the name the beginning of a longer word.
    /// </summary>
    [Fact]
    public void A_name_followed_by_a_percent_sign_ends_there_unless_a_letter_goes_on_in_percent_escapes()
    {
        Assert.Equal("<user>%Dev <user>%Face <user>%Be <user>%20x <user>%", Rewritten(Windows, "Ada%Dev Ada%Face Ada%Be Ada%20x Ada%"));
        Assert.Equal("Ada%C3%A9 Ada%c3%a9", Rewritten(Windows, "Ada%C3%A9 Ada%c3%a9"));
        Assert.Equal("%D0%90%D0%B4%D0%B0%D0%BC", Rewritten(Long("Ада", @"C:\Users\Ада"), "%D0%90%D0%B4%D0%B0%D0%BC"));
        Assert.Equal("<user>%Dev", Rewritten(Long("Ада", @"C:\Users\Ада"), "%D0%90%D0%B4%D0%B0%Dev"));
    }

    /// <summary>
    /// The escapes after a name are read as the character they spell, as the same character written plainly would be: a
    /// letter or a digit goes on with the word and leaves the name where it is, and anything else — a no-break space, the
    /// curly apostrophe of "Ada's", a dash, a currency sign — ends the word, and the name with it. The same holds after the
    /// home directory.
    /// </summary>
    [Fact]
    public void A_name_followed_by_percent_escapes_ends_there_unless_they_spell_a_letter_or_a_digit()
    {
        Assert.Equal(
            "<user>%E2%80%99s <user>%C2%A0Lovelace <user>%E2%80%93x q=<user>%E2%80%99s+laptop <user>%E2%82%AC <user>%E2%80",
            Rewritten(Windows, "Ada%E2%80%99s Ada%C2%A0Lovelace Ada%E2%80%93x q=Ada%E2%80%99s+laptop Ada%E2%82%AC Ada%E2%80"));
        Assert.Equal("Ada%C3%A9 Ada%D9%A3 Ada%F0%9D%90%80", Rewritten(Windows, "Ada%C3%A9 Ada%D9%A3 Ada%F0%9D%90%80"));
        Assert.Equal("file:///~%C2%A0/x", Rewritten(Windows, "file:///C:/Users/Ada%C2%A0/x"));
        Assert.Equal("<user>%E2%80%99s", Rewritten(Long("Ада", @"C:\Users\Ада"), "%D0%90%D0%B4%D0%B0%E2%80%99s"));
    }

    /// <summary>
    /// The last component of the home directory is a name only where the directory is a profile: a folder that is under
    /// the root of profiles. Elsewhere it is a folder like any other, and a word that is also the name of one — an
    /// application's, or <c>empty</c> — is a word in the text and not somebody.
    /// </summary>
    [Fact]
    public void The_last_component_of_a_home_directory_that_is_not_a_profile_is_not_a_name()
    {
        Assert.Equal("jason started in ~/data for <user>", Rewritten(Long("svc", "/opt/jason"), "jason started in /opt/jason/data for svc"));
        Assert.Equal("the empty queue in ~/x", Rewritten(Long("nobody", "/var/empty"), "the empty queue in /var/empty/x"));
        Assert.Equal(@"acme in ~\x", Rewritten(Long("svc", @"D:\Work\Acme"), @"acme in D:\Work\Acme\x"));
        Assert.Equal("zed in ~/x", Rewritten(Long("svc", "/data/home/zed"), "zed in /data/home/zed/x"));
        Assert.Equal("zed in ~", Rewritten(Long("svc", "/home/zed/inner"), "zed in /home/zed/inner"));
    }

    [Theory]
    [InlineData(@"C:\Users\John Smith", "jsmith", @"~ and <user> and <user>", @"C:\Users\John Smith and John Smith and jsmith")]
    [InlineData(@"c:\USERS\John Smith", "jsmith", @"~ and <user> and <user>", @"c:\USERS\John Smith and John Smith and jsmith")]
    [InlineData(@"C:\Documents and Settings\jdoe", "jane", @"~ and <user> and <user>", @"C:\Documents and Settings\jdoe and jdoe and jane")]
    [InlineData("/home/jdoe", "jdoe2", "~ and <user> and <user>", "/home/jdoe and jdoe and jdoe2")]
    [InlineData("/Users/jdoe", "jdoe2", "~ and <user> and <user>", "/Users/jdoe and jdoe and jdoe2")]
    public void The_last_component_of_a_profile_directory_is_a_name_where_it_is_not_the_account_name(string home, string user, string expected, string text) =>
        Assert.Equal(expected, Rewritten(Long(user, home), text));

    /// <summary>A name that is also a word in a path that is not a home directory stays a word when the home directory is not a profile.</summary>
    [Fact]
    public void A_home_directory_that_is_not_a_profile_still_becomes_a_tilde_wherever_it_stands()
    {
        Assert.Equal("~ ~/x ~\\y", Rewritten(Long("svc", "/opt/jason"), "/opt/jason /opt/jason/x \\opt\\jason\\y"));
    }

    /// <summary>A name that has a space or another character in it that a URL spells with a percent sign is spelled so where it is part of a URL.</summary>
    [Fact]
    public void The_percent_encoded_spelling_of_a_name_and_of_the_home_directory_is_rewritten_too()
    {
        var facts = Long("John Smith", @"C:\Users\John Smith");

        Assert.Equal("file:///~/x.js", Rewritten(facts, "file:///C:/Users/John%20Smith/x.js"));
        Assert.Equal("owner=<user>;", Rewritten(facts, "owner=John%20Smith;"));
        Assert.Equal("~%5Cx", Rewritten(facts, "C%3A%5CUsers%5CJohn%20Smith%5Cx"));
        Assert.Equal("D%3A%5C<user>%5Cx", Rewritten(facts, "D%3A%5CJohn%20Smith%5Cx"));
    }

    /// <summary>The name is Cyrillic "Ada"; in a URL each of its letters is two bytes, each of them a percent sign and two digits in either case.</summary>
    [Fact]
    public void A_name_in_another_alphabet_is_found_in_its_percent_encoded_form_in_either_case_and_only_whole()
    {
        var facts = Long("Ада", @"C:\Users\Ада");

        Assert.Equal("file:///~/x", Rewritten(facts, "file:///C:/Users/%D0%90%D0%B4%D0%B0/x"));
        Assert.Equal("<user> <user>", Rewritten(facts, "%D0%90%D0%B4%D0%B0 %d0%90%d0%b4%d0%b0"));
        Assert.Equal("%D0%90%D0%B4%D0%B0%D0%BC", Rewritten(facts, "%D0%90%D0%B4%D0%B0%D0%BC"));
    }

    /// <summary>
    /// Text that was JSON and is carried inside a string has its line breaks and tabs as a backslash and a letter, and
    /// a name that begins a line stands right after the letter.
    /// </summary>
    [Fact]
    public void A_name_right_after_a_backslash_escape_in_text_that_is_json_is_still_a_token()
    {
        var facts = Windows;

        Assert.Equal(
            @"{""m"":""first line\n<user> signed in\t<machine>\r\n<domain>""}",
            Rewritten(facts, @"{""m"":""first line\nAda signed in\tWORKSTATION-7\r\nCORP""}"));
        Assert.Equal(@"quote\""<user>\""", Rewritten(facts, @"quote\""Ada\"""));
        Assert.Equal("a" + "\\" + "u000a<user>", Rewritten(facts, "a" + "\\" + "u000aAda"));
        Assert.Equal(@"\nAdam and Ada2", Rewritten(facts, @"\nAdam and Ada2"));
    }

    [Fact]
    public void The_last_component_of_the_home_directory_is_a_name_when_it_is_not_the_account_name()
    {
        var facts = Long("artem.kosolap", @"C:\Users\artemko");

        Assert.Equal(@"~\x", Rewritten(facts, @"C:\Users\artemko\x"));
        Assert.Equal("/c/Users/<user>/x and <user>", Rewritten(facts, "/c/Users/artemko/x and artemko"));
        Assert.Equal("/c/Users/<user>/x", Rewritten(facts, "/c/Users/artem.kosolap/x"));
    }

    [Fact]
    public void An_identifier_of_any_work_or_microsoft_account_is_a_placeholder_as_well()
    {
        Assert.Equal(
            "<sid> and <sid> and <sid>",
            Rewritten(Windows, "S-1-12-1-111-222-333-444 and S-1-5-21-1-2-3 and s-1-12-1-9-8-7-6"));
        Assert.Equal("S-1-12-1-111 and S-1-12-2-1-2-3-4", Rewritten(Windows, "S-1-12-1-111 and S-1-12-2-1-2-3-4"));
    }

    /// <summary>
    /// Where the home directory and the account's name begin in the same place, the directory is the one that is meant.
    /// An account called <c>c</c> would otherwise leave the rest of every path as somebody's folder.
    /// </summary>
    [Fact]
    public void The_home_directory_wins_where_the_account_name_begins_in_the_same_place()
    {
        var facts = Long("c", @"C:\Users\Someone");

        Assert.Equal(@"~\x and <user>", Rewritten(facts, @"C:\Users\Someone\x and C"));
    }

    /// <summary>These behave as they should and are held so that they keep to it.</summary>
    [Theory]
    [InlineData(@"\\fs01\home\ada\notes", @"~\notes")]
    [InlineData(@"\\\\fs01\\home\\ada\\notes", @"~\\notes")]
    [InlineData("//fs01/home/ada/notes", "~/notes")]
    public void A_home_directory_on_a_network_share_is_the_home_directory(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Long("ada", @"\\fs01\home\ada"), text));

    [Theory]
    [InlineData(@"\\?\C:\Users\Ada\x", @"\\?\~\x")]
    [InlineData("file:///C:/Users/Ada/x", "file:///~/x")]
    [InlineData(@"\\?\UNC\WORKSTATION-7\share\Ada", @"\\?\UNC\<machine>\share\<user>")]
    public void The_forms_a_path_takes_in_a_message_are_rewritten_like_the_path_is(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Windows, text));

    [Fact]
    public void A_home_directory_with_a_space_in_it_is_the_home_directory()
    {
        Assert.Equal(@"~\AppData", Rewritten(Long("John Smith", @"C:\Users\John Smith"), @"C:\Users\John Smith\AppData"));
    }

    /// <summary>A record prints its members, and these are the one thing an installation's diagnostics must not print by accident.</summary>
    [Fact]
    public void The_names_do_not_print()
    {
        var text = Windows.ToString();
        var environment = new CliEnvironment(new StringWriter(), new StringWriter(), new JasonPaths("scratch"), Identity: Windows).ToString();

        foreach (var name in new[] { "Ada", @"C:\Users", "WORKSTATION", "CORP", ThisSid })
        {
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(name, environment, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_marks_say_what_each_kind_of_name_is_written_as()
    {
        var marks = new IdentityRewriter(Windows).Marks;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["home_written_as"] = "~",
                ["data_written_as"] = "<data>",
                ["runtime_data_written_as"] = "<runtime_data>",
                ["user_written_as"] = "<user>",
                ["machine_written_as"] = "<machine>",
                ["domain_written_as"] = "<domain>",
                ["sid_written_as"] = "<sid>",
            },
            marks.ToDictionary());
    }

    /// <summary>The mark words are this program's own, so they are read without anybody's names: nothing about an account is needed to say them.</summary>
    [Fact]
    public void The_marks_are_said_without_any_names()
    {
        Assert.Equal(new IdentityRewriter(Windows).Marks.ToDictionary(), IdentityRewriter.MarkWords.ToDictionary());
    }

    // ---- the directories: whole paths only ------------------------------------------------------------------------

    /// <summary>
    /// A directory is a path, and a path does not begin in the middle of a word: after a letter, a digit or an underscore
    /// the same characters are part of something else. Nor does it end in one.
    /// </summary>
    [Theory]
    [InlineData(@"xD:\Clients\Acme\jason\logs")]
    [InlineData(@"_D:\Clients\Acme\jason")]
    [InlineData(@"7D:\Clients\Acme\jason")]
    [InlineData(@"D:\Clients\Acme\jason_old\logs")]
    public void A_directory_is_never_found_in_the_middle_of_a_word(string text) =>
        Assert.Equal(text, Rewritten(Windows with { DataDirectory = @"D:\Clients\Acme\jason" }, text));

    [Fact]
    public void A_directory_inside_another_path_is_not_that_directory()
    {
        var facts = Unix with { DataDirectory = "/data" };

        Assert.Equal("/var/lib/data/x and <data>/logs and /srv/data", Rewritten(facts, "/var/lib/data/x and /data/logs and /srv/data"));
    }

    /// <summary>A directory that is only a root names nothing, so it is never a mark: a data directory on a drive of its own leaves every "d:" alone.</summary>
    [Theory]
    [InlineData(@"D:\")]
    [InlineData("D:")]
    [InlineData("d:/")]
    [InlineData(@"\\fileserver\share")]
    [InlineData(@"\\fileserver\share\")]
    [InlineData(@"\\?\D:\")]
    [InlineData("/")]
    public void A_directory_that_is_only_a_root_is_never_a_mark(string root)
    {
        const string Text = @"Attempt failed: ID: 7; Kind: x; read D:\work\x and d:/y on \\fileserver\share\z; {""old"":""bad""}";

        Assert.Equal(Text, Rewritten(Windows with { DataDirectory = root }, Text));
        Assert.Equal(Text, Rewritten(Windows with { RuntimeDataDirectory = root }, Text));
        Assert.Equal(Text, Rewritten(new IdentityFacts(root, "nobody", "box", null, null), Text));
    }

    // ---- the runtime's data directory -----------------------------------------------------------------------------

    /// <summary>
    /// The data directory a runtime answering from somewhere else keeps is a name of its own too, with a mark of its own:
    /// the two installations a bundle exists to tell apart stay apart in it.
    /// </summary>
    [Theory]
    [InlineData(@"D:\Clients\OtherCo\jason", "<runtime_data>")]
    [InlineData(@"D:\Clients\OtherCo\jason\logs\runtime-20261004.jsonl", @"<runtime_data>\logs\runtime-20261004.jsonl")]
    [InlineData(@"{""data_dir"":""D:\\Clients\\OtherCo\\jason""}", @"{""data_dir"":""<runtime_data>""}")]
    [InlineData(@"xD:\Clients\OtherCo\jason", @"xD:\Clients\OtherCo\jason")]
    public void A_runtimes_own_data_directory_is_a_mark_of_its_own(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Windows with { DataDirectory = @"D:\Clients\Acme\jason", RuntimeDataDirectory = @"D:\Clients\OtherCo\jason" }, text));

    [Fact]
    public void A_runtimes_data_directory_inside_home_reads_as_a_path_in_home() =>
        Assert.Equal(@"~\.jason-other\logs", Rewritten(Windows with { RuntimeDataDirectory = @"C:\Users\Ada\.jason-other" }, @"C:\Users\Ada\.jason-other\logs"));

    /// <summary>One directory never has two marks: where the runtime's is this installation's own, it is <c>&lt;data&gt;</c>.</summary>
    [Fact]
    public void A_runtimes_data_directory_that_is_this_one_is_only_data()
    {
        var facts = Windows with { DataDirectory = @"D:\Clients\Acme\jason", RuntimeDataDirectory = @"D:\Clients\Acme\jason\" };

        Assert.Equal(@"<data>\logs", Rewritten(facts, @"D:\Clients\Acme\jason\logs"));
    }

    /// <summary>
    /// A runtime's data directory under another account's profile is that whole directory, and the other account's name
    /// is not left behind in a piece of it.
    /// </summary>
    [Fact]
    public void A_runtimes_data_directory_in_another_profile_is_written_whole()
    {
        var facts = Windows with { DataDirectory = @"D:\jason", RuntimeDataDirectory = @"C:\Users\someoneelse\jason" };

        Assert.Equal(@"<runtime_data>\logs and <runtime_data>", Rewritten(facts, @"C:\Users\someoneelse\jason\logs and C:/Users/someoneelse/jason"));
    }

    /// <summary>Where the runtime's directory is inside this one, or this one inside the runtime's, the longer is meant, whichever it is.</summary>
    [Fact]
    public void Of_two_data_directories_one_inside_the_other_the_longer_wins_either_way()
    {
        var inside = Windows with { DataDirectory = @"D:\jason", RuntimeDataDirectory = @"D:\jason\other" };
        var outside = Windows with { DataDirectory = @"D:\jason\cli", RuntimeDataDirectory = @"D:\jason" };

        Assert.Equal(@"<runtime_data>\logs and <data>\logs", Rewritten(inside, @"D:\jason\other\logs and D:\jason\logs"));
        Assert.Equal(@"<data>\logs and <runtime_data>\logs", Rewritten(outside, @"D:\jason\cli\logs and D:\jason\logs"));
    }

    /// <summary>All three directories nest: home inside the runtime's data directory, which is inside this installation's.</summary>
    [Fact]
    public void Three_nested_directories_are_each_the_longest_that_matches()
    {
        var facts = new IdentityFacts("/srv/jason/other/home/ada", "ada", "box", null, null) { DataDirectory = "/srv/jason", RuntimeDataDirectory = "/srv/jason/other" };

        Assert.Equal("~/x <runtime_data>/y <data>/z", Rewritten(facts, "/srv/jason/other/home/ada/x /srv/jason/other/y /srv/jason/z"));
    }

    /// <summary>The whole-path marks run before the names: a machine named like a folder in the path does not break it.</summary>
    [Fact]
    public void A_whole_path_is_rewritten_before_any_name_inside_it()
    {
        var facts = new IdentityFacts(@"C:\Users\Ada", "Ada", "Acme", "Clients", null) { DataDirectory = @"D:\Clients\Acme\jason" };

        Assert.Equal(@"<data>\x on <machine> in <domain>", Rewritten(facts, @"D:\Clients\Acme\jason\x on Acme in Clients"));
    }

    /// <summary>
    /// Windows keeps a short form of a long folder name, and a path that was never spelled out long reads that way: each
    /// directory's short form, as the operating system gives it, is written with the directory's own mark.
    /// </summary>
    [Fact]
    public void Each_directorys_short_form_is_written_with_its_mark()
    {
        var shortForms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Users\Ada Lovelace"] = @"C:\Users\ADALOV~1",
            [@"D:\Clients\Acme Corp\jason"] = @"D:\Clients\ACMECO~1\jason",
            [@"D:\Clients\Other Co\jason"] = @"D:\Clients\OTHERC~1\jason",
        };
        var facts = new IdentityFacts(@"C:\Users\Ada Lovelace", "Ada Lovelace", "box", null, null)
        {
            DataDirectory = @"D:\Clients\Acme Corp\jason",
            RuntimeDataDirectory = @"D:\Clients\Other Co\jason",
        };
        var rewriter = new IdentityRewriter(facts, path => shortForms.GetValueOrDefault(path));

        Assert.Equal(
            @"~\AppData <data>\logs <runtime_data>\logs",
            rewriter.Rewrite(JsonValue.Create(@"C:\Users\ADALOV~1\AppData D:\Clients\ACMECO~1\jason\logs D:\Clients\OTHERC~1\jason\logs")!).GetValue<string>());
    }

    /// <summary>
    /// A data directory outside the home directory is a name of its own: <c>D:\Clients\Acme\jason</c> says whose work
    /// this installation does as plainly as a home directory says whose account it runs under.
    /// </summary>
    [Theory]
    [InlineData(@"D:\Clients\Acme\jason", "<data>")]
    [InlineData(@"D:\Clients\Acme\jason\logs\runtime-20261004.jsonl", @"<data>\logs\runtime-20261004.jsonl")]
    [InlineData("D:/Clients/Acme/jason/config", "<data>/config")]
    [InlineData(@"{""data_dir"":""D:\\Clients\\Acme\\jason""}", @"{""data_dir"":""<data>""}")]
    [InlineData(@"Content root path: D:\Clients\Acme\jason\", @"Content root path: <data>\")]
    public void A_data_directory_outside_home_is_a_mark_of_its_own(string text, string expected) =>
        Assert.Equal(expected, Rewritten(Windows with { DataDirectory = @"D:\Clients\Acme\jason" }, text));

    /// <summary>In another case it is the same directory where paths are matched without regard to case, as the home directory is.</summary>
    [Fact]
    public void A_data_directory_in_another_case_is_the_data_directory_where_paths_have_no_case()
    {
        var facts = Windows with { DataDirectory = @"D:\Clients\Acme\jason" };

        Assert.Equal(OperatingSystem.IsWindows() ? @"<data>\work" : @"d:\clients\acme\JASON\work", Rewritten(facts, @"d:\clients\acme\JASON\work"));
    }

    [Fact]
    public void A_data_directory_is_a_path_and_ends_where_a_name_ends()
    {
        var facts = Windows with { DataDirectory = @"D:\Clients\Acme\jason" };

        Assert.Equal(@"D:\Clients\Acme\jason2\logs", Rewritten(facts, @"D:\Clients\Acme\jason2\logs"));
        Assert.Equal(@"E:\Clients\Acme\jason", Rewritten(facts, @"E:\Clients\Acme\jason"));
    }

    /// <summary>Inside home it already reads <c>~</c> and a path below it, which says more than <c>&lt;data&gt;</c> would.</summary>
    [Fact]
    public void A_data_directory_inside_home_reads_as_a_path_in_home()
    {
        var facts = Windows with { DataDirectory = @"C:\Users\Ada\.jason" };

        Assert.Equal(@"~\.jason\logs", Rewritten(facts, @"C:\Users\Ada\.jason\logs"));
        Assert.Equal("~/.jason", Rewritten(Unix with { DataDirectory = "/home/ada/.jason" }, "/home/ada/.jason"));
    }

    /// <summary>Where one of the two directories is inside the other, the longer is the one that is meant, as it is for every name.</summary>
    [Fact]
    public void Of_a_home_inside_the_data_directory_the_longer_path_wins()
    {
        var facts = new IdentityFacts("/srv/jason/home/ada", "ada", "build-host", null, null) { DataDirectory = "/srv/jason" };

        Assert.Equal("~/notes and <data>/logs", Rewritten(facts, "/srv/jason/home/ada/notes and /srv/jason/logs"));
    }

    [Fact]
    public void Without_a_data_directory_nothing_is_written_as_one()
    {
        Assert.Null(Windows.DataDirectory);
        Assert.Equal(@"D:\Clients\Acme\jason", Rewritten(Windows, @"D:\Clients\Acme\jason"));
        Assert.Equal("/var/lib/jason", Rewritten(Unix with { DataDirectory = "/" }, "/var/lib/jason"));
    }

    [Fact]
    public void The_data_directory_does_not_print()
    {
        var facts = Windows with { DataDirectory = @"D:\Clients\Acme\jason" };

        Assert.DoesNotContain("Acme", facts.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_document_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => new IdentityRewriter(Windows).Rewrite(null!));
    }

    [Fact]
    public void The_names_of_this_machine_are_read_from_it_and_rewrite_its_home_directory()
    {
        var facts = IdentityFacts.ForThisMachine();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(home.Trim('\\', '/').Length == 0, "This account has no home directory to rewrite.");

        Assert.Equal(home, facts.Home);
        Assert.Equal(Environment.UserName, facts.User);
        Assert.Equal(Environment.MachineName, facts.Machine);
        // Only the start is held: the rest of the path is whatever it is, and an account with the name of one of its
        // components would have that rewritten too.
        var rewritten = Rewritten(facts, home.TrimEnd('\\', '/') + "/data");
        Assert.StartsWith("~/", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain(home, rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperatingSystem.IsWindows(), facts.Sid?.StartsWith("S-1-", StringComparison.Ordinal) ?? false);
    }

    [Fact]
    public void The_environment_names_this_machine_unless_a_test_hands_it_names_of_its_own()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);

        Assert.Null(new CliEnvironment(new StringWriter(), new StringWriter(), paths).Identity);
        Assert.Same(Windows, new CliEnvironment(new StringWriter(), new StringWriter(), paths, Identity: Windows).Identity);
    }
}
