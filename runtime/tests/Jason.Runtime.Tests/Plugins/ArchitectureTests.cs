using System.Reflection;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The boundaries that make the isolation claim true rather than intended. Plugin JavaScript runs in a separate
/// process because the runtime cannot run it at all: the engine is not in this assembly's world, and the host
/// that has it knows nothing of databases, web servers or the runtime's own code.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void The_runtime_has_no_way_to_run_a_plugin_in_its_own_process()
    {
        var referenced = References(typeof(PluginInvoker).Assembly);

        Assert.DoesNotContain("Jint", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Acornima", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Jason.PluginHost", referenced, StringComparer.Ordinal);
    }

    [Fact]
    public void The_contracts_carry_the_protocol_and_no_dependency_of_either_side()
    {
        var referenced = References(typeof(PluginInvocation).Assembly);

        Assert.DoesNotContain("YamlDotNet", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("Jint", referenced, StringComparer.Ordinal);
    }

    [Fact]
    public void The_plugin_host_knows_nothing_of_the_runtime_it_is_started_by()
    {
        var host = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Jason.PluginHost.dll"));
        var referenced = References(host);

        Assert.DoesNotContain("Jason.Runtime", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("YamlDotNet", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void The_manifest_reader_is_the_one_place_the_yaml_dependency_is_allowed()
    {
        // Stated as a fact rather than a prohibition: a manifest is written by people, and exactly one assembly
        // is allowed to know that. The two tests above are what keeps it from spreading.
        Assert.Contains("YamlDotNet", References(typeof(PluginInvoker).Assembly), StringComparer.Ordinal);
    }

    /// <summary>
    /// A7(a). Vendor neutrality is a property of the sources, not of an intention, so it is read off them: every
    /// string literal of every production assembly is scanned for a vendor's name, and each one found is listed.
    /// <para>
    /// The scan targets vendor tokens rather than the English word. "Reply", "replies" and "replied" are
    /// ordinary SDR vocabulary in this product — a prospect replies, a reply is classified — and they will keep
    /// appearing in operations, roles and seeded practice; a test that failed for one of those would be a test
    /// that fails for being right. What is refused is a vendor's product name, and the bare word used as an
    /// identifier: a plugin id, a route default.
    /// </para>
    /// </summary>
    [Fact]
    public void No_vendor_is_named_anywhere_in_the_sources()
    {
        var found = new List<string>();
        var files = 0;

        foreach (var file in SourceFiles())
        {
            files++;
            foreach (var (line, text) in Literals(File.ReadAllText(file)))
            {
                if (NamesAVendor(text))
                {
                    found.Add($"{Path.GetRelativePath(RepositoryRoot(), file)}({line}): \"{text}\"");
                }
            }
        }

        // A scan that read nothing would pass for the wrong reason.
        Assert.True(files > 50, $"Only {files} source files were scanned; the source tree was not found.");
        Assert.True(found.Count == 0, $"A vendor is named in {found.Count} string literal(s):{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    /// <summary>
    /// The scan above is only worth what its reader is worth: a reader that quietly found nothing would make
    /// the claim vacuous. This is the reader held to what C# actually has — quoted, verbatim and raw strings,
    /// comments and character literals — with a vendor's name hidden in each place it could hide.
    /// </summary>
    [Fact]
    public void The_reader_finds_every_kind_of_string_and_nothing_that_is_not_one()
    {
        const string source = """"
            // a comment naming reply.io is not a literal
            class Probe
            {
                /* nor is reply-cli in a block comment
                   across two lines */
                const char Quote = '"';
                const string Plain = "reply.io";
                const string Escaped = "a \" reply-cli";
                const string Verbatim = @"C:\replyapp\""quoted""";
                const string Raw = """
                    reply_cli
                    """;
                string Interpolated(int n) => $"n={n} reply";
            }
            """";

        var literals = Literals(source).ToArray();

        Assert.Equal(
            ["reply.io", "a \\\" reply-cli", "C:\\replyapp\\\"\"quoted\"\"", "reply_cli", "n={n} reply"],
            literals.Select(literal => literal.Text));
        Assert.Equal([7, 8, 9, 10, 13], literals.Select(literal => literal.Line));

        // Four of the five: the interpolated one only ends in the word, which is a prospect's doing, not a vendor.
        Assert.Equal(4, literals.Count(literal => NamesAVendor(literal.Text)));
    }

    [Theory]
    [InlineData("reply", true)]
    [InlineData("Reply", true)]
    [InlineData("reply.io", true)]
    [InlineData("https://api.reply.io/v3", true)]
    [InlineData("reply-cli", true)]
    [InlineData("reply_cli", true)]
    [InlineData("replyapp", true)]
    [InlineData("replies", false)]
    [InlineData("replied", false)]
    [InlineData("The prospect replied to the second step.", false)]
    [InlineData("conversation.classify_reply", false)]
    [InlineData("reply_received", false)]
    public void The_word_a_prospect_does_is_not_the_name_of_a_vendor(string literal, bool vendor) =>
        Assert.Equal(vendor, NamesAVendor(literal));

    private static readonly string[] VendorTokens = ["reply.io", "reply-cli", "reply_cli", "replyapp"];

    private static bool NamesAVendor(string literal) =>
        string.Equals(literal, "reply", StringComparison.OrdinalIgnoreCase)
        || VendorTokens.Any(token => literal.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every C# file of every production project, found by walking up to the repository's own marker.</summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "runtime", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// The repository root, found by walking up from the test binaries to the directory holding
    /// <c>global.json</c>. Nothing else in this class looks at the sources, so this is the first test that
    /// needs to know where they are.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// Every string literal of a C# file, with the line it starts on: quoted, verbatim and raw, interpolated or
    /// not. Comments and character literals are skipped, because a sentence about a vendor is not a dependency
    /// on one — what the sources must not carry is the name itself, where it would reach a user or a provider.
    /// </summary>
    private static IEnumerable<(int Line, string Text)> Literals(string source)
    {
        var literals = new List<(int Line, string Text)>();
        var line = 1;
        var index = 0;

        while (index < source.Length)
        {
            var character = source[index];

            if (character is '\n')
            {
                line++;
                index++;
            }
            else if (character is '/' && Next(source, index) is '/')
            {
                while (index < source.Length && source[index] is not '\n')
                {
                    index++;
                }
            }
            else if (character is '/' && Next(source, index) is '*')
            {
                index += 2;
                while (index < source.Length && !(source[index] is '*' && Next(source, index) is '/'))
                {
                    line += source[index] is '\n' ? 1 : 0;
                    index++;
                }

                index = Math.Min(index + 2, source.Length);
            }
            else if (character is '\'')
            {
                index++;
                while (index < source.Length && source[index] is not '\'')
                {
                    index += source[index] is '\\' ? 2 : 1;
                }

                index++;
            }
            else if (StartsAString(source, index, out var quote, out var quotes, out var verbatim))
            {
                var started = line;
                var text = Read(source, quote, quotes, verbatim, ref index, ref line);
                literals.Add((started, text));
            }
            else
            {
                index++;
            }
        }

        return literals;
    }

    private static char Next(string source, int index) => index + 1 < source.Length ? source[index + 1] : '\0';

    /// <summary>
    /// Whether a string begins here, after any <c>$</c> and <c>@</c> prefix: how many quotes open it — three or
    /// more is a raw string — and whether backslashes are ordinary characters inside it.
    /// </summary>
    private static bool StartsAString(string source, int index, out int quote, out int quotes, out bool verbatim)
    {
        quote = index;
        quotes = 0;
        verbatim = false;

        while (quote < source.Length && source[quote] is '$' or '@')
        {
            verbatim |= source[quote] is '@';
            quote++;
        }

        if (quote >= source.Length || source[quote] is not '"')
        {
            return false;
        }

        while (quote + quotes < source.Length && source[quote + quotes] is '"')
        {
            quotes++;
        }

        return true;
    }

    private static string Read(string source, int quote, int quotes, bool verbatim, ref int index, ref int line)
    {
        var start = quote + quotes;
        var end = start;

        if (quotes >= 3)
        {
            // A raw string ends at as many quotes as opened it; its own indentation is not worth undoing here.
            while (end < source.Length && !IsRun(source, end, quotes))
            {
                end++;
            }
        }
        else if (verbatim)
        {
            while (end < source.Length && !(source[end] is '"' && Next(source, end) is not '"'))
            {
                end += source[end] is '"' ? 2 : 1;
            }
        }
        else
        {
            while (end < source.Length && source[end] is not '"' and not '\n')
            {
                end += source[end] is '\\' ? 2 : 1;
            }
        }

        end = Math.Min(end, source.Length);
        var text = source[start..end];
        line += text.Count(character => character is '\n');
        index = Math.Min(end + (quotes >= 3 ? quotes : 1), source.Length);
        return quotes >= 3 ? text.Trim() : text;
    }

    private static bool IsRun(string source, int index, int quotes)
    {
        if (index + quotes > source.Length)
        {
            return false;
        }

        for (var offset = 0; offset < quotes; offset++)
        {
            if (source[index + offset] is not '"')
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];
}
