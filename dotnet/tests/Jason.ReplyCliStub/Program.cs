// A stand-in for the Reply CLI, as the official plugin is allowed to use it and nothing more:
//
//     reply [--json] [-q] [--profile <name>] [--team-id <id>] <noun> <verb> [<id>] [options]
//
// It stands in for the program from the outside: each work command prints what the reply-cli release `ReplyCliRelease`
// names documents — its result as JSON on stdout, or one `{"error":…}` line on stderr with exit 1, 2 or 3 — from a
// small record of what the account holds, so a test can plant a world and read back what happened. It never imitates
// Reply's API: the plugin makes no raw call, so `api` is a command this program does not know, and a plugin that
// reached for it fails here. A flag this program does not know is a usage error too, so a plugin that reached for
// `--api-key`, `--verbose`, `--user-id`, `--user-email` or `--pretty`, or for `--idempotency-key` on anything but a direct
// send, fails here, before any assertion about the argument vector runs. Which account it works in is never on the command line: it finds its store
// the way the real CLI does, from the platform's own configuration directory and the profile name.

// Read and written as reply-cli is, a node program: UTF-8 on every stream, whatever the console's code page, with no
// byte-order mark looked for, as node's JSON.parse looks for none. A stand-in that used the console's page would agree
// with a caller making the same mistake and hide it; bytes that are not UTF-8 end the program instead of reading as
// something.
var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false));
Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });

// Answered before anything else: a manifest that declares a minimum version makes a reload run this on whatever it
// resolved, long before any account is involved. reply-cli prints the bare version.
if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
{
    await Console.Out.WriteLineAsync(Jason.ReplyCliStub.ReplyCliRelease.Version);
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
    // The raw call is the real CLI's own, and the one thing the official package never runs: every request it makes has
    // a command made for it.
    return await UsageAsync("unknown command 'api'");
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

static async Task<int> UsageAsync(string message)
{
    // commander's own refusal: plain text and exit 2, decided before anything at all has been called.
    await Console.Error.WriteLineAsync("error: " + message);
    return 2;
}
