// A stand-in for the Reply CLI, as the official plugin is allowed to use it and nothing more:
//
//     reply [--json] [-q] [--profile <name>] [--team-id <id>] <noun> <verb> [<id>] [options]
//     reply [--json] [-q] [--profile <name>] [--team-id <id>] api <path> [--method <verb>] [--body -]
//
// It stands in for the program from the outside: each work command prints what reply-cli 0.12.0 documents — its
// result as JSON on stdout, or one `{"error":…}` line on stderr with exit 1, 2 or 3 — from a small record of what the
// account holds, so a test can plant a world and read back what happened. It never imitates Reply's API beyond the raw
// calls the plugin still makes: the contact search, and the list and the create of sequences. A flag this program does not know is a
// usage error, so a plugin that reached for `--api-key`, `--verbose`, `--user-id`, `--user-email`, `--pretty` or
// `--idempotency-key` fails here, before any assertion about the argument vector runs. Which account it works in is
// never on the command line: it finds its store the way the real CLI does, from the platform's own configuration
// directory and the profile name.

// Answered before anything else: a manifest that declares a minimum version makes a reload run this on whatever it
// resolved, long before any account is involved. reply-cli prints the bare version.
if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
{
    await Console.Out.WriteLineAsync("0.12.0");
    return 0;
}

string profile = "default";
var index = 0;

// The global options, which come before the command.
while (index < args.Length && args[index].StartsWith('-'))
{
    var argument = args[index];
    if (argument is "--json" or "-q")
    {
        index++;
        continue;
    }

    if (argument is "--profile" or "--team-id")
    {
        if (index + 1 >= args.Length)
        {
            return await UsageAsync($"option '{argument}' argument missing");
        }

        if (argument == "--profile")
        {
            profile = args[index + 1];
        }

        // The team is the caller's business and never this program's: it is recorded with the rest of the
        // argument vector, which is where a test reads it.
        index += 2;
        continue;
    }

    return await UsageAsync($"unknown option '{argument}'");
}

if (index >= args.Length)
{
    return await UsageAsync("missing command");
}

var noun = args[index++];
if (noun == "api")
{
    return await ApiAsync(profile, args, index);
}

if (index >= args.Length)
{
    return await UsageAsync($"missing command after '{noun}'");
}

var command = noun + " " + args[index++];
if (!Commands.IsKnown(command))
{
    return await UsageAsync($"unknown command '{command}'");
}

var rest = args[index..];
var stdin = Commands.ReadsStdin(rest) ? await Console.In.ReadToEndAsync() : null;
return await Account.RunCommandAsync(profile, command, rest, stdin, args);

// `api <path> [--method <verb>] [--body -]`: the raw call, kept for the requests the plugin still makes that way.
static async Task<int> ApiAsync(string profile, string[] args, int index)
{
    if (index >= args.Length)
    {
        return await UsageAsync("missing required argument 'path'");
    }

    var path = args[index++];
    if (path.StartsWith('-'))
    {
        return await UsageAsync($"unknown option '{path}'");
    }

    var method = "GET";
    var bodyOnStdin = false;
    while (index < args.Length)
    {
        var argument = args[index];
        if (string.Equals(argument, "--method", StringComparison.Ordinal))
        {
            if (index + 1 >= args.Length)
            {
                return await UsageAsync("option '--method' argument missing");
            }

            method = args[index + 1].ToUpperInvariant();
            index += 2;
            continue;
        }

        if (string.Equals(argument, "--body", StringComparison.Ordinal))
        {
            if (index + 1 >= args.Length)
            {
                return await UsageAsync("option '--body' argument missing");
            }

            if (!string.Equals(args[index + 1], "-", StringComparison.Ordinal))
            {
                // A body on the command line would put a person's address into the caller's log. This CLI takes a
                // body from stdin and from nowhere else, so the rule holds against a plugin that tried anyway.
                return await UsageAsync("option '--body' takes only '-': a request body is read from stdin");
            }

            bodyOnStdin = true;
            index += 2;
            continue;
        }

        return await UsageAsync(argument.StartsWith('-')
            ? $"unknown option '{argument}'"
            : $"unexpected argument '{argument}'");
    }

    if (!path.StartsWith('/'))
    {
        return await UsageAsync($"argument 'path' must start with '/': '{path}'");
    }

    var body = bodyOnStdin ? await Console.In.ReadToEndAsync() : null;
    return await Account.RunAsync(profile, method, path, body, args);
}

static async Task<int> UsageAsync(string message)
{
    // commander's own refusal: plain text and exit 2, decided before anything at all has been called.
    await Console.Error.WriteLineAsync("error: " + message);
    return 2;
}
