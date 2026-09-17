// A stand-in for the Reply CLI, as the official plugin is allowed to use it and nothing more:
//
//     reply [--json] [-q] [--profile <name>] [--team-id <id>] api <path> [--method <verb>] [--body -]
//
// A flag this program does not know is a usage error, so a plugin that reached for `--api-key`, `--verbose`,
// `--user-id`, `--user-email` or `--pretty` fails here, before any assertion about the argument vector runs.
// Everything else was measured from the real CLI rather than guessed: it prints {"code": <status>, "data":
// <body>} for any status, exits 1 from 400 up, exits 2 on a usage error before it has called anything, and —
// holding no credential — exits 1 having printed nothing a caller could parse. Which account it works in is
// never on the command line: it finds its store the way the real CLI does, from the platform's own
// configuration directory and the profile name, so a test chooses one by setting a variable on the process
// that starts it.

// The one thing answered before anything else: a manifest that declares a minimum version makes a reload run
// this on whatever it resolved, long before any account is involved, and a program that refused it would be
// refused in turn and never reach an operation at all.
if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
{
    // Newer than the oldest release the official package will work with, and printed the way the real one prints
    // it: a line with the program's own name in front of the number.
    await Console.Out.WriteLineAsync("reply 0.5.1");
    return 0;
}

string profile = "default";
string path;
var method = "GET";
var bodyOnStdin = false;
var index = 0;

// The global prefix, which comes before the subcommand.
while (index < args.Length && !string.Equals(args[index], "api", StringComparison.Ordinal))
{
    var argument = args[index];
    if (string.Equals(argument, "--json", StringComparison.Ordinal) || string.Equals(argument, "-q", StringComparison.Ordinal))
    {
        index++;
        continue;
    }

    if (string.Equals(argument, "--profile", StringComparison.Ordinal) || string.Equals(argument, "--team-id", StringComparison.Ordinal))
    {
        if (index + 1 >= args.Length)
        {
            return await UsageAsync($"option '{argument}' argument missing");
        }

        if (string.Equals(argument, "--profile", StringComparison.Ordinal))
        {
            profile = args[index + 1];
        }

        // The team is the caller's business and never this program's: it is recorded with the rest of the
        // argument vector, which is where a test reads it.
        index += 2;
        continue;
    }

    return await UsageAsync(argument.StartsWith('-')
        ? $"unknown option '{argument}'"
        : $"unknown command '{argument}'");
}

if (index >= args.Length)
{
    return await UsageAsync("missing command 'api'");
}

index++;
if (index >= args.Length)
{
    return await UsageAsync("missing required argument 'path'");
}

path = args[index++];
if (path.StartsWith('-'))
{
    return await UsageAsync($"unknown option '{path}'");
}

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

static async Task<int> UsageAsync(string message)
{
    // Exit 2 is a usage error, and it is decided before anything at all has been called.
    await Console.Error.WriteLineAsync("error: " + message);
    return 2;
}
