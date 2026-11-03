using System.Text;
using System.Text.Json;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>One answer of the hook: the host's decision word and one fixed sentence that says why.</summary>
public sealed record HookDecision(string Decision, string Reason);

/// <summary>
/// The <c>host-hook antigravity-cli</c> mode: the answer to one tool call, which the host asks for through the workspace
/// hook this runtime writes into an attempt's work directory. It is a pure function of the rules on its command line and
/// the one JSON document on standard input. It starts no runtime and no child, opens no socket, reads no file and no
/// environment variable, and writes nothing but the one line of its answer, whose reason is a fixed sentence and never a
/// word of what it was asked: the input of a tool call can carry a person's text.
/// </summary>
/// <remarks>
/// <c>auto_approve</c> lifts the host's own ask where no rule of the person's settles it, honouring their deny and ask
/// rules; <c>allow</c> hands the call on to those rules and the host's default; <c>deny</c> is a hard block. Anything the
/// hook cannot read is a <c>deny</c>: a pass would let a write through.
/// </remarks>
public static class AntigravityHook
{
    /// <summary>The longest standard input read; a tool call larger than this is refused.</summary>
    public const int MaxInput = 4 * 1024 * 1024;

    public const string AutoApprove = "auto_approve";

    public const string Allow = "allow";

    public const string Deny = "deny";

    private static readonly HookDecision Unreadable = new(Deny, "Jason could not read this tool call or its rules, so it is refused.");

    private static readonly HookDecision Floor = new(Deny, "Jason does not allow a session of a role to do this.");

    private static readonly HookDecision NotAllowedHere = new(Deny, "This tool is not allowed for this session by its execution profile.");

    private static readonly HookDecision Approved = new(AutoApprove, "Jason allows a session of a role to do this.");

    private static readonly HookDecision Delegated = new(Allow, "Jason does not settle this; the person's own rules and the host's default decide.");

    /// <summary>The tools a role is never allowed, whatever a profile says: an edit of any file, and a question to the person.</summary>
    private static readonly string[] FloorTools = ["replace_file_content", "multi_replace_file_content", "ask_question"];

    /// <summary>
    /// Reads standard input as bytes, never through a console code page: a child started without a window of its own gets
    /// a console on the OEM code page, and the writer here is the agent host, which does not escape what it writes. The
    /// bytes are strict UTF-8, and a stream that is not, or is longer than <see cref="MaxInput"/>, is an unreadable call.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, Stream stdin, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        // The answer is always written and the exit is always 0: the host reads the decision, and what a non-zero exit
        // means to it is not documented. Whatever goes wrong on the way is an unreadable call, a cancellation aside.
        string line;
        try
        {
            var rules = args is ["antigravity-cli", { } text] ? text : "";
            var input = await ReadBoundedAsync(stdin, cancellationToken).ConfigureAwait(false);
            line = Answer(rules, input, OperatingSystem.IsWindows());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            line = JsonSerializer.Serialize(new Dictionary<string, string> { ["decision"] = Unreadable.Decision, ["reason"] = Unreadable.Reason });
        }

        await stdout.WriteLineAsync(line).ConfigureAwait(false);
        return 0;
    }

    /// <summary>The one line the host reads: <c>{"decision":...,"reason":...}</c>.</summary>
    public static string Answer(string rules, string? input, bool windows)
    {
        var decision = Decide(rules, input, windows);
        return JsonSerializer.Serialize(new Dictionary<string, string> { ["decision"] = decision.Decision, ["reason"] = decision.Reason });
    }

    public static HookDecision Decide(string? rulesArgument, string? input, bool windows)
    {
        try
        {
            if (AntigravityHookRules.Decode(rulesArgument) is not { } rules || input is null)
            {
                return Unreadable;
            }

            using var document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || AntigravityHookRules.HoldsAKeyTwice(document.RootElement, "toolCall")
                || !document.RootElement.TryGetProperty("toolCall", out var call) || call.ValueKind != JsonValueKind.Object
                || AntigravityHookRules.HoldsAKeyTwice(call, "name", "args")
                || !call.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
                || nameElement.GetString() is not { Length: > 0 } name
                || !call.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Object
                || AntigravityHookRules.HoldsAKeyTwice(args, "CommandLine", "AbsolutePath", "TargetFile"))
            {
                return Unreadable;
            }

            return Decide(rules, name, args, windows);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            // A lone surrogate in a string is an argument exception at the first read of it, not a parse failure.
            return Unreadable;
        }
    }

    private static HookDecision Decide(AntigravityHookRules rules, string name, JsonElement args, bool windows)
    {
        // A name the profile denies is denied before anything else is looked at, the callback and the memory files included.
        if (rules.Deny.Contains(name, StringComparer.Ordinal))
        {
            return NotAllowedHere;
        }

        if (FloorTools.Contains(name, StringComparer.Ordinal))
        {
            return Floor;
        }

        switch (name)
        {
            case "run_command":
                return Text(args, "CommandLine") is not { } line ? Unreadable : CallsTheCliAlone(line, rules.Cli, windows) ? Approved : Delegated;

            case "view_file":
                return Text(args, "AbsolutePath") is not { } viewed ? Unreadable : rules.Read.Any(file => SamePath(viewed, file, windows)) ? Approved : Delegated;

            case "write_to_file":
                return Text(args, "TargetFile") is not { } target ? Unreadable : SamePath(target, rules.Write, windows) ? Approved : Floor;

            case "read_url_content":
            case "search_web":
                return rules.Approve.Contains(name, StringComparer.Ordinal) ? Approved : NotAllowedHere;

            default:
                // A browser the host drives loads pages as the fetch does: it is not given where the fetch is not granted.
                if (name.StartsWith("browser_", StringComparison.Ordinal))
                {
                    return rules.Approve.Contains("read_url_content", StringComparer.Ordinal) && !rules.Deny.Contains("read_url_content", StringComparer.Ordinal)
                        ? Delegated
                        : NotAllowedHere;
                }

                return Delegated;
        }
    }

    private static string? Text(JsonElement args, string name)
    {
        foreach (var property in args.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a line is provably one invocation of the command word: the word, then arguments that are bare tokens or
    /// spans in single quotes holding no single quote. Anything a shell reads as syntax, whichever shell it is, is outside
    /// that; and on Windows, where a single quote is not one, so is every character the command interpreter reads as syntax
    /// and every line break, quoted or not. Never a prefix test.
    /// </summary>
    internal static bool CallsTheCliAlone(string line, string cli, bool windows)
    {
        if (cli.Length == 0 || !cli.All(IsBare))
        {
            return false;
        }

        var position = 0;
        var first = true;
        while (position < line.Length)
        {
            if (line[position] is ' ' or '\t')
            {
                position++;
                continue;
            }

            var start = position;
            var quoted = false;
            while (position < line.Length && line[position] is not (' ' or '\t'))
            {
                if (line[position] == '\'')
                {
                    quoted = true;
                    var close = line.IndexOf('\'', position + 1);
                    if (close < 0)
                    {
                        return false;
                    }

                    position = close + 1;
                }
                else if (IsBare(line[position]))
                {
                    position++;
                }
                else
                {
                    return false;
                }
            }

            if (first)
            {
                var word = line[start..position];
                if (quoted || !string.Equals(word, cli, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return false;
                }

                first = false;
            }
        }

        return !first && !(windows && line.Any(character => character is '&' or '|' or '<' or '>' or '^' or '%' or '!' or '\r' or '\n'));
    }

    private static bool IsBare(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '=' or '.' or '/' or ':' or ',' or '@' or '+';

    /// <summary>Two full paths are one file when they are the same after normalising; case counts except on Windows. A path that is not full is no file.</summary>
    private static bool SamePath(string given, string known, bool windows)
    {
        if (string.IsNullOrEmpty(given) || given.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(given) || !Path.IsPathFullyQualified(known))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(given), Path.GetFullPath(known), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static async Task<string?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            bytes.Write(buffer, 0, read);
            if (bytes.Length > MaxInput)
            {
                return null;
            }
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
